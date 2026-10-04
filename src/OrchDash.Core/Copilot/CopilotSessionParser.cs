using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using OrchDash.Core.Model;

namespace OrchDash.Core.Copilot;

/// <summary>Turns the lines of a Copilot CLI <c>events.jsonl</c> into <see cref="SessionContent"/> (spec 4.1-4.6).</summary>
public sealed class CopilotSessionParser : ISessionParser
{
    private const string Fence = "```";

    private readonly string? _workDir;
    private readonly List<ModelCall> _calls = [];
    private readonly Dictionary<string, int> _callIndex = new(StringComparer.Ordinal);
    private readonly List<ConversationItem> _items = [];
    private readonly Dictionary<string, int> _toolIndex = new(StringComparer.Ordinal);
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

        _built = null;
        var time = CopilotJson.Time(root, "timestamp");
        if (time is not null)
        {
            _firstEventAt ??= time;
            _lastEventAt = time;
        }

        var data = CopilotJson.Property(root, "data");
        switch (CopilotJson.String(root, "type"))
        {
            case "assistant.turn_start":
                StartTurn(data, time);
                break;
            case "assistant.message":
                AddMessage(data, time);
                break;
            case "tool.execution_start":
                StartTool(data, time);
                break;
            case "tool.execution_complete":
                CompleteTool(data, time);
                break;
            case "user.message":
                // 10.2: the first prompt as sent; an empty one does not count.
                if (_sentPrompt is null && CopilotJson.String(data, "transformedContent") is { Length: > 0 } prompt)
                    _sentPrompt = prompt;
                break;
            case "session.usage_checkpoint":
                _checkpoint = ReadCheckpoint(data);
                break;
            case "result":
                SetResult(root);
                break;
        }
    }

    private void CountUnparsed()
    {
        _unparsedLines++;
        _built = null;
    }

    private void StartTurn(JsonElement data, DateTimeOffset? time)
    {
        if (CopilotJson.String(data, "turnId") is not { } turnId)
            return;
        _callIndex[turnId] = _calls.Count;
        _calls.Add(new ModelCall(turnId, null, time, null));
    }

    private void AddMessage(JsonElement data, DateTimeOffset? time)
    {
        var turnId = CopilotJson.String(data, "turnId");
        if (CopilotJson.String(data, "model") is { } model)
        {
            _model = model;
            if (turnId is not null && _callIndex.TryGetValue(turnId, out var index))
                _calls[index] = _calls[index] with { Model = model };
        }

        if (CopilotJson.String(data, "reasoningText") is { Length: > 0 } reasoning)
            _items.Add(new Thinking(turnId, time, reasoning, null));

        var content = CopilotJson.String(data, "content");
        if (content is { Length: > 0 })
            _items.Add(new AssistantText(turnId, time, content));

        if (CopilotJson.Property(data, "toolRequests") is { ValueKind: JsonValueKind.Array } requests)
        {
            foreach (var request in requests.EnumerateArray())
            {
                if (request.ValueKind != JsonValueKind.Object)
                    continue;
                AddToolCall(turnId, time,
                    CopilotJson.String(request, "toolCallId"),
                    CopilotJson.String(request, "name"),
                    CopilotJson.Property(request, "arguments"),
                    CopilotJson.String(request, "intentionSummary"));
            }
        }

        if (CopilotJson.String(data, "phase") == "final_answer")
            _finalAnswer = content;
    }

    private void StartTool(JsonElement data, DateTimeOffset? time)
    {
        var toolId = CopilotJson.String(data, "toolCallId");
        if (toolId is not null && _toolIndex.TryGetValue(toolId, out var index))
        {
            var call = (ToolCall)_items[index];
            _items[index] = call with { Time = time ?? call.Time };
            return;
        }

        AddToolCall(null, time, toolId, CopilotJson.String(data, "toolName"), CopilotJson.Property(data, "arguments"), null);
    }

    private void AddToolCall(string? callId, DateTimeOffset? time, string? toolId, string? name, JsonElement arguments,
        string? intentionSummary)
    {
        name ??= "";
        var call = new ToolCall(callId, time, toolId ?? "", name, CopilotJson.Write(arguments),
            CopilotToolSummary.Create(name, arguments, intentionSummary, _workDir), null);
        if (toolId is not null)
            _toolIndex[toolId] = _items.Count;
        _items.Add(call);
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

        var call = (ToolCall)_items[index];
        _items[index] = call with
        {
            Result = new ToolResult(time, CopilotJson.Boolean(data, "success") != true, content, diff, exitCode),
        };
    }

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
            Turns: _calls.Count,
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
}
