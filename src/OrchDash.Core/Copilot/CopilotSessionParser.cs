using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using OrchDash.Core.Model;

namespace OrchDash.Core.Copilot;

/// <summary>
/// Turns the lines of a Copilot CLI <c>events.jsonl</c> into <see cref="SessionContent"/> (spec 4.1-4.6), with the
/// sub-agents' calls and items tagged by their <c>agentId</c> (35.4-35.7).
/// </summary>
public sealed class CopilotSessionParser : ISessionParser
{
    private const string Fence = "```";

    private readonly string? _workDir;
    private readonly List<ModelCall> _calls = [];
    // 35.4: by AgentId and turnId, since a sub-agent's turn ids repeat the agent's.
    private readonly Dictionary<(string? AgentId, string TurnId), int> _callIndex = new();
    private readonly List<ConversationItem> _items = [];
    // Tool call ids are unique across the agent and its sub-agents.
    private readonly Dictionary<string, int> _toolIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StartArguments> _arguments = new(StringComparer.Ordinal);
    private readonly List<SubAgent> _subAgents = [];
    private readonly Dictionary<string, int> _subAgentIndex = new(StringComparer.Ordinal);
    private string? _sessionId;
    private string? _model;
    private string? _finalAnswer;
    private SessionResult? _result;
    private DateTimeOffset? _firstEventAt;
    private DateTimeOffset? _lastEventAt;
    private int _unparsedLines;
    private string? _sentPrompt;
    private ContextCheckpoint? _checkpoint;
    private SessionContent? _built = SessionContent.Empty;

    /// <param name="workDir">The session's working folder; tool paths under it are shown relative to it.</param>
    public CopilotSessionParser(string? workDir) => _workDir = workDir;

    public void AddLine(string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line ?? "");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // ArgumentException: the text is not valid UTF-16, for example a lone surrogate.
            CountUnparsed();
            return;
        }

        using (document)
            AddEvent(document.RootElement);
    }

    public SessionContent Build() => _built ??= new SessionContent(
        _sessionId, _model, null,
        _calls.ToImmutableArray(), _items.ToImmutableArray(), _result,
        _firstEventAt, _lastEventAt, _unparsedLines)
    {
        SentPrompt = _sentPrompt,
        Checkpoint = _checkpoint,
        SubAgents = _subAgents.ToImmutableArray(),
    };

    private void AddEvent(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            CountUnparsed();
            return;
        }

        if (CopilotJson.Boolean(root, "ephemeral") == true)
            return;

        var time = CopilotJson.Time(root, "timestamp");
        var timesChanged = SetEventTimes(time);
        var type = CopilotJson.String(root, "type");
        var data = CopilotJson.Property(root, "data");
        var agentId = NonEmpty(CopilotJson.String(root, "agentId"));   // 35.4: set on a sub-agent's events

        if (type is "subagent.completed" or "subagent.failed")
        {
            // 35.5: named by agentId, else by the starting call. A finish event for an unknown or finished
            // sub-agent changes nothing (4.5), so Build keeps its instance.
            var index = agentId is not null ? SubAgentIndex(agentId) : StartedBy(CopilotJson.String(data, "toolCallId"));
            var state = type == "subagent.completed" ? SessionState.Succeeded : SessionState.Failed;
            if (FinishSubAgent(index, state, time) || timesChanged)
                _built = null;
            return;
        }

        _built = null;
        if (agentId is not null)
            AddSubAgent(agentId, time);

        switch (type)
        {
            case "assistant.turn_start":
                StartTurn(agentId, data, time);
                break;
            case "assistant.message":
                AddMessage(agentId, data, time);
                break;
            case "tool.execution_start":
                StartTool(agentId, data, time);
                break;
            case "tool.execution_complete":
                CompleteTool(data, time);
                break;
            case "subagent.started":
                if (agentId is not null)
                    StartSubAgent(agentId, data, time);
                break;
            case "user.message":
                // 10.2: the first prompt as sent; an empty one does not count. 35.6: not a sub-agent's.
                if (agentId is null && _sentPrompt is null &&
                    CopilotJson.String(data, "transformedContent") is { Length: > 0 } prompt)
                    _sentPrompt = prompt;
                break;
            case "session.usage_checkpoint":
                // 35.6: a sub-agent's checkpoint is not the session's.
                if (agentId is null)
                    _checkpoint = ReadCheckpoint(data);
                break;
            case "result":
                SetResult(root);
                break;
        }
    }

    /// <summary>Takes the time of a kept line; true when the first or last event time changed.</summary>
    private bool SetEventTimes(DateTimeOffset? time)
    {
        if (time is not { } at || (_lastEventAt is { } last && last.EqualsExact(at)))
            return false;

        _firstEventAt ??= at;
        _lastEventAt = at;
        return true;
    }

    private void CountUnparsed()
    {
        _unparsedLines++;
        _built = null;
    }

    private void StartTurn(string? agentId, JsonElement data, DateTimeOffset? time)
    {
        if (CopilotJson.String(data, "turnId") is not { } turnId)
            return;
        _callIndex[(agentId, turnId)] = _calls.Count;
        _calls.Add(new ModelCall(turnId, null, time, null) { AgentId = agentId });
    }

    private void AddMessage(string? agentId, JsonElement data, DateTimeOffset? time)
    {
        var turnId = CopilotJson.String(data, "turnId");
        if (CopilotJson.String(data, "model") is { } model)
        {
            // 35.7: the session's model comes from the agent's own messages only.
            if (agentId is null)
                _model = model;
            if (turnId is not null && _callIndex.TryGetValue((agentId, turnId), out var index))
                _calls[index] = _calls[index] with { Model = model };
        }

        if (CopilotJson.String(data, "reasoningText") is { Length: > 0 } reasoning)
            _items.Add(new Thinking(turnId, time, reasoning, null) { AgentId = agentId });

        var content = CopilotJson.String(data, "content");
        if (content is { Length: > 0 })
            _items.Add(new AssistantText(turnId, time, content) { AgentId = agentId });

        if (CopilotJson.Property(data, "toolRequests") is { ValueKind: JsonValueKind.Array } requests)
        {
            foreach (var request in requests.EnumerateArray())
            {
                if (request.ValueKind != JsonValueKind.Object)
                    continue;
                AddToolCall(agentId, turnId, time,
                    CopilotJson.String(request, "toolCallId"),
                    CopilotJson.String(request, "name"),
                    CopilotJson.Property(request, "arguments"),
                    CopilotJson.String(request, "intentionSummary"));
            }
        }

        if (CopilotJson.String(data, "phase") != "final_answer")
            return;

        // 35.5, 35.6: a sub-agent's final answer is its report, not the session's result text.
        if (agentId is null)
        {
            _finalAnswer = content;
            return;
        }

        var sub = _subAgentIndex[agentId];
        _subAgents[sub] = _subAgents[sub] with { Report = content };
    }

    private void StartTool(string? agentId, JsonElement data, DateTimeOffset? time)
    {
        var toolId = CopilotJson.String(data, "toolCallId");
        var arguments = CopilotJson.Property(data, "arguments");
        if (toolId is not null && _toolIndex.TryGetValue(toolId, out var index))
        {
            var call = (ToolCall)_items[index];
            _items[index] = call with { Time = time ?? call.Time };
            RememberArguments(toolId, arguments);
            return;
        }

        AddToolCall(agentId, null, time, toolId, CopilotJson.String(data, "toolName"), arguments, null);
    }

    private void AddToolCall(string? agentId, string? callId, DateTimeOffset? time, string? toolId, string? name,
        JsonElement arguments, string? intentionSummary)
    {
        name ??= "";
        var call = new ToolCall(callId, time, toolId ?? "", name, CopilotJson.Write(arguments),
            CopilotToolSummary.Create(name, arguments, intentionSummary, _workDir), null)
        {
            AgentId = agentId,
        };
        if (toolId is not null)
        {
            _toolIndex[toolId] = _items.Count;
            RememberArguments(toolId, arguments);
        }
        _items.Add(call);
    }

    /// <summary>Keeps the arguments a <c>subagent.started</c> may read from its starting call (35.4).</summary>
    private void RememberArguments(string toolId, JsonElement arguments)
    {
        if (StartArguments.Read(arguments) is { } read)
            _arguments[toolId] = read;
    }

    private void CompleteTool(JsonElement data, DateTimeOffset? time)
    {
        if (CopilotJson.String(data, "toolCallId") is not { } toolId || !_toolIndex.TryGetValue(toolId, out var index))
        {
            CountUnparsed();
            return;
        }

        var result = CopilotJson.Property(data, "result");
        var content = CopilotJson.String(result, "content") is { Length: > 0 } text
            ? text
            : CopilotJson.Write(CopilotJson.Property(data, "error"));
        var detail = CopilotJson.String(result, "detailedContent");
        var diff = detail is not null && detail.Contains("diff --git", StringComparison.Ordinal) ? detail : null;
        var exitCode = CopilotJson.Int32(CopilotJson.Property(data, "shellExecution"), "exitCode");
        var isError = CopilotJson.Boolean(data, "success") != true;

        var call = (ToolCall)_items[index];
        _items[index] = call with { Result = new ToolResult(time, isError, content, diff, exitCode) };

        // 35.5: the starting call of a foreground sub-agent finishes it, and its result is the report when the
        // sub-agent gave none.
        var sub = StartedBy(toolId);
        if (sub < 0 || _subAgents[sub].Background)
            return;
        FinishSubAgent(sub, isError ? SessionState.Failed : SessionState.Succeeded, time);
        if (_subAgents[sub].Report is null && content.Length > 0)
            _subAgents[sub] = _subAgents[sub] with { Report = content };
    }

    /// <summary>Adds sub-agent <paramref name="agentId"/> when it is new (35.4).</summary>
    private void AddSubAgent(string agentId, DateTimeOffset? time)
    {
        if (_subAgentIndex.ContainsKey(agentId))
            return;

        _subAgentIndex[agentId] = _subAgents.Count;
        _subAgents.Add(new SubAgent(agentId, null, "", SubAgents.Name(null), null, null, null, false, "", time, null,
            SessionState.Running, null));
    }

    /// <summary>
    /// 35.4: fills sub-agent <paramref name="agentId"/> in place from <c>subagent.started</c>, falling back to the
    /// arguments of its starting call.
    /// </summary>
    private void StartSubAgent(string agentId, JsonElement data, DateTimeOffset? time)
    {
        var toolId = CopilotJson.String(data, "toolCallId");
        var arguments = toolId is not null ? _arguments.GetValueOrDefault(toolId) : null;
        var parentId = toolId is not null && _toolIndex.TryGetValue(toolId, out var item) ? _items[item].AgentId : null;
        var description = NonEmpty(CopilotJson.String(data, "agentDescription")) ?? arguments?.Description;
        var mode = NonEmpty(CopilotJson.String(data, "executionMode")) ?? arguments?.Mode;

        var index = _subAgentIndex[agentId];
        var sub = _subAgents[index];
        _subAgents[index] = sub with
        {
            ParentId = parentId,
            ToolCallId = toolId ?? "",
            Name = SubAgents.Name(description),
            Description = description,
            AgentType = NonEmpty(CopilotJson.String(data, "agentType")) ?? arguments?.AgentType,
            Model = CopilotJson.String(data, "model"),
            Background = mode is not null && mode != "sync",
            Prompt = arguments?.Prompt ?? "",
            StartedAt = time ?? sub.StartedAt,
        };
    }

    /// <summary>Finishes the sub-agent at <paramref name="index"/> while it runs (35.5); true when it did.</summary>
    private bool FinishSubAgent(int index, SessionState state, DateTimeOffset? time)
    {
        if (index < 0 || _subAgents[index].State != SessionState.Running)
            return false;

        _subAgents[index] = _subAgents[index] with { State = state, FinishedAt = time };
        return true;
    }

    private int SubAgentIndex(string agentId) => _subAgentIndex.GetValueOrDefault(agentId, -1);

    /// <summary>The index of the sub-agent that tool call <paramref name="toolId"/> started, or -1.</summary>
    private int StartedBy(string? toolId) =>
        string.IsNullOrEmpty(toolId) ? -1 : _subAgents.FindIndex(sub => sub.ToolCallId == toolId);

    private static string? NonEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;

    /// <summary>The checkpoint table of spec 4.3; missing parts give null or empty arrays.</summary>
    private static ContextCheckpoint ReadCheckpoint(JsonElement data)
    {
        var model = CheckpointModel(data);

        var toolNames = ImmutableArray.CreateBuilder<string>();
        if (CopilotJson.Property(model, "tools") is { ValueKind: JsonValueKind.Array } tools)
        {
            foreach (var tool in tools.EnumerateArray())
            {
                if (CopilotJson.String(tool, "name") is { } name)
                    toolNames.Add(name);
            }
        }

        // A segment without a name or a token count is left out: TokenPart needs both.
        var segments = ImmutableArray.CreateBuilder<TokenPart>();
        if (CopilotJson.Property(model, "system_segments") is { ValueKind: JsonValueKind.Array } systemSegments)
        {
            foreach (var segment in systemSegments.EnumerateArray())
            {
                if (CopilotJson.String(segment, "segment") is { } name && CopilotJson.Int64(segment, "tokens") is { } tokens)
                    segments.Add(new TokenPart(name, tokens));
            }
        }

        return new ContextCheckpoint(
            CopilotJson.Int64(model, "prompt_tokens"),
            CopilotJson.Int64(model, "tool_tokens"),
            toolNames.ToImmutable(),
            segments.ToImmutable(),
            CopilotJson.Int64(data, "totalNanoAiu"),
            CopilotJson.Double(data, "totalPremiumRequests"));
    }

    /// <summary>
    /// In <c>promptCacheBreakState[]</c> the entry whose conversation is <c>main</c>, else the first; in it
    /// <c>models.&lt;lastActiveModel&gt;</c>, else the first model. A default element when there is none.
    /// </summary>
    private static JsonElement CheckpointModel(JsonElement data)
    {
        if (CopilotJson.Property(data, "promptCacheBreakState") is not { ValueKind: JsonValueKind.Array } states)
            return default;

        JsonElement entry = default;
        foreach (var state in states.EnumerateArray())
        {
            if (state.ValueKind != JsonValueKind.Object)
                continue;
            if (CopilotJson.String(state, "conversation") == "main")
            {
                entry = state;
                break;
            }
            if (entry.ValueKind == JsonValueKind.Undefined)
                entry = state;
        }

        if (CopilotJson.Property(entry, "models") is not { ValueKind: JsonValueKind.Object } models)
            return default;
        if (CopilotJson.String(entry, "lastActiveModel") is { } active &&
            CopilotJson.Property(models, active) is { ValueKind: JsonValueKind.Object } activeModel)
            return activeModel;

        foreach (var model in models.EnumerateObject())
        {
            if (model.Value.ValueKind == JsonValueKind.Object)
                return model.Value;
        }

        return default;
    }

    private void SetResult(JsonElement root)
    {
        _sessionId = CopilotJson.String(root, "sessionId") ?? _sessionId;

        var exitCode = CopilotJson.Int32(root, "exitCode");
        var subtype = exitCode switch
        {
            0 => "success",
            { } code => "exit " + code.ToString(CultureInfo.InvariantCulture),
            null => "error",
        };
        var structuredJson = StructuredJson(_finalAnswer);
        var (worker, review) = StructuredOutput.Parse(structuredJson);
        var usage = CopilotJson.Property(root, "usage");
        var codeChanges = CopilotJson.Property(usage, "codeChanges");

        _result = new SessionResult(
            IsError: exitCode != 0,
            Subtype: subtype,
            Text: _finalAnswer,
            StructuredJson: structuredJson,
            Worker: worker,
            Review: review,
            CostUsd: null,
            Turns: _calls.Count(call => call.AgentId is null),   // 35.7: the agent's own calls
            Duration: CopilotJson.Milliseconds(usage, "sessionDurationMs"),
            ApiDuration: CopilotJson.Milliseconds(usage, "totalApiDurationMs"),
            Usage: null,
            ContextWindow: null,
            PremiumRequests: CopilotJson.Double(usage, "premiumRequests"),
            LinesAdded: CopilotJson.Int32(codeChanges, "linesAdded"),
            LinesRemoved: CopilotJson.Int32(codeChanges, "linesRemoved"));
    }

    /// <summary>The text without one surrounding code fence, as indented JSON when it is a JSON object.</summary>
    private static string? StructuredJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            using var document = JsonDocument.Parse(RemoveFence(text.Trim()));
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? CopilotJson.Write(document.RootElement, indented: true)
                : null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Drops the first and last line when the first is three backticks with an optional language name
    /// and the last is three backticks; otherwise returns the text as it is.
    /// </summary>
    private static string RemoveFence(string text)
    {
        var firstBreak = text.IndexOf('\n');
        if (firstBreak < 0)
            return text;

        var lastBreak = text.LastIndexOf('\n');
        var opening = text[..firstBreak].TrimEnd('\r');
        var closing = text[(lastBreak + 1)..].Trim();
        if (!IsFenceOpening(opening) || closing != Fence)
            return text;

        return firstBreak == lastBreak ? "" : text[(firstBreak + 1)..lastBreak];
    }

    private static bool IsFenceOpening(string line)
    {
        if (!line.StartsWith(Fence, StringComparison.Ordinal))
            return false;
        foreach (var c in line.AsSpan(Fence.Length).Trim())
        {
            if (!char.IsLetterOrDigit(c) && c is not ('-' or '+' or '_' or '.' or '#'))
                return false;
        }
        return true;
    }

    /// <summary>The arguments of a <c>task</c> call that describe the sub-agent it starts (35.4).</summary>
    private sealed record StartArguments(string? Description, string? Prompt, string? AgentType, string? Mode)
    {
        private static readonly StartArguments None = new(null, null, null, null);

        /// <summary>The four arguments, or null when the call has none of them.</summary>
        public static StartArguments? Read(JsonElement arguments)
        {
            var read = new StartArguments(
                NonEmpty(CopilotJson.String(arguments, "description")),
                CopilotJson.String(arguments, "prompt"),
                NonEmpty(CopilotJson.String(arguments, "agent_type")),
                NonEmpty(CopilotJson.String(arguments, "mode")));
            return read == None ? null : read;
        }
    }
}
