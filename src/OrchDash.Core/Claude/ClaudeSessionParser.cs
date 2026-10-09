using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using OrchDash.Core.Model;

namespace OrchDash.Core.Claude;

/// <summary>
/// Turns the lines of a Claude <c>events.jsonl</c> (stream-json output of headless Claude Code)
/// into <see cref="SessionContent"/> by the Claude event table of spec section 4.3, with the sub-agent rules of
/// 35.1-35.3, 35.7 and 35.8.
/// </summary>
public sealed class ClaudeSessionParser : ISessionParser
{
    private const string PermissionDeniedKind = "permission denied";
    private const double MaxDurationMs = 1e14;

    private readonly string? _workDir;
    private readonly ImmutableArray<ModelCall>.Builder _calls = ImmutableArray.CreateBuilder<ModelCall>();
    private readonly HashSet<string> _callIds = new(StringComparer.Ordinal);
    private readonly ImmutableArray<ConversationItem>.Builder _items = ImmutableArray.CreateBuilder<ConversationItem>();
    private readonly Dictionary<string, int> _toolCallIndex = new(StringComparer.Ordinal);
    private readonly List<SubAgent> _subAgents = [];
    private readonly Dictionary<string, int> _subAgentIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _taskToolUseIds = new(StringComparer.Ordinal);

    private string? _sessionId;
    private string? _model;
    private SessionInit? _init;
    private SessionResult? _result;
    private DateTimeOffset? _firstEventAt;
    private DateTimeOffset? _lastEventAt;
    private int _unparsedLines;
    private int? _thinkingTokens;
    private RateLimit? _rateLimit;
    private SessionContent? _content = SessionContent.Empty;

    /// <param name="workDir">Folder that tool paths are shown relative to until the init line gives a <c>cwd</c>.</param>
    public ClaudeSessionParser(string? workDir) => _workDir = workDir;

    public void AddLine(string line)
    {
        using var document = ClaudeJson.TryParse(line);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            CountUnparsed();
            return;
        }

        var root = document.RootElement;
        var type = ClaudeJson.GetString(root, "type");
        if (type == "rate_limit_event")
        {
            // 10.1: the line has no timestamp and sets nothing but the rate limit.
            _rateLimit = ReadRateLimit(root);
            Changed();
            return;
        }

        var subtype = ClaudeJson.GetString(root, "subtype");
        if (type == "system" && subtype is "task_started" or "task_notification")
        {
            // 35.3: they only start and finish sub-agents; a task of another kind (local_bash) names none.
            AddTask(root, subtype);
            return;
        }

        if (!IsKnown(type, subtype))
            return;

        var time = ReadTimestamp(root);
        ApplyCommon(root, time);
        if (_result is not null && (type is "assistant" or "user" || (type == "system" && subtype == "init")))
        {
            // 35.8: another turn has started; the next result event sets the result again.
            _result = null;
            Changed();
        }

        switch (type)
        {
            case "system":
                AddSystem(root, subtype, time);
                break;
            case "assistant":
                AddAssistant(root, time);
                break;
            case "user":
                AddUser(root, time);
                break;
            case "result":
                AddResult(root);
                break;
        }
    }

    public SessionContent Build() => _content ??= new SessionContent(
        _sessionId, _model, _init,
        _calls.ToImmutable(), _items.ToImmutable(), _result,
        _firstEventAt, _lastEventAt, _unparsedLines)
    {
        RateLimit = _rateLimit,
        SubAgents = [.. _subAgents],
    };

    private static bool IsKnown(string? type, string? subtype) => type switch
    {
        "system" => subtype is "init" or "thinking_tokens" or "permission_denied",
        "assistant" or "user" or "result" => true,
        _ => false,
    };

    private static DateTimeOffset? ReadTimestamp(JsonElement root) =>
        ClaudeJson.GetString(root, "timestamp") is { } text &&
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : null;

    private void ApplyCommon(JsonElement root, DateTimeOffset? time)
    {
        if (ClaudeJson.GetString(root, "session_id") is { } sessionId && sessionId != _sessionId)
        {
            _sessionId = sessionId;
            Changed();
        }

        if (time is { } t)
        {
            if (_firstEventAt is null || t < _firstEventAt)
                _firstEventAt = t;
            if (_lastEventAt is null || t > _lastEventAt)
                _lastEventAt = t;
            Changed();
        }
    }

    private void AddSystem(JsonElement root, string? subtype, DateTimeOffset? time)
    {
        switch (subtype)
        {
            case "init":
                SetModel(ClaudeJson.GetString(root, "model"));
                _init = ReadInit(root);
                Changed();
                break;
            case "thinking_tokens":
                if (ClaudeJson.GetInt32(root, "estimated_tokens") is { } tokens)
                    _thinkingTokens = tokens;
                break;
            case "permission_denied":
                var tool = ClaudeJson.GetString(root, "tool_name");
                var message = ClaudeJson.GetString(root, "message") ?? "";
                AddItem(new Notice(null, time, PermissionDeniedKind, tool is null ? message : $"{tool}: {message}"));
                break;
        }
    }

    /// <summary>
    /// 35.3: task_started remembers which tool use a task id belongs to; task_notification finishes the
    /// sub-agent of its tool_use_id, else of its task_id through that task_started. Neither has a timestamp
    /// as a rule, so a notification finishes at the latest event time seen so far.
    /// </summary>
    private void AddTask(JsonElement root, string? subtype)
    {
        var taskId = ClaudeJson.GetString(root, "task_id");
        var toolUseId = ClaudeJson.GetString(root, "tool_use_id");
        if (subtype == "task_started")
        {
            if (!string.IsNullOrEmpty(taskId) && !string.IsNullOrEmpty(toolUseId))
                _taskToolUseIds[taskId] = toolUseId;
            return;
        }

        if (string.IsNullOrEmpty(toolUseId) && taskId is not null)
            _taskToolUseIds.TryGetValue(taskId, out toolUseId);
        if (toolUseId is null || !_subAgentIndex.TryGetValue(toolUseId, out var index))
            return;

        var state = ClaudeJson.GetString(root, "status") == "failed" ? SessionState.Failed : SessionState.Succeeded;
        FinishSubAgent(index, state, ClaudeJson.GetString(root, "summary"), ReadTimestamp(root) ?? _lastEventAt);
    }

    /// <summary>The rate limit table of spec 4.3; seen at the latest event time so far.</summary>
    private RateLimit ReadRateLimit(JsonElement root)
    {
        var info = ClaudeJson.GetObject(root, "rate_limit_info");
        var windows = ClaudeJson.GetObject(info, "unifiedWindows");
        var fiveHour = ClaudeJson.GetObject(windows, "five_hour");
        var sevenDay = ClaudeJson.GetObject(windows, "seven_day");

        return new RateLimit(
            ClaudeJson.GetString(info, "status"),
            ClaudeJson.GetString(info, "rateLimitType"),
            ClaudeJson.GetDouble(fiveHour, "utilization"),
            ClaudeJson.GetUnixSeconds(fiveHour, "resetsAt"),
            ClaudeJson.GetDouble(sevenDay, "utilization"),
            ClaudeJson.GetUnixSeconds(sevenDay, "resetsAt"),
            _lastEventAt);
    }

    private static SessionInit ReadInit(JsonElement root)
    {
        var tools = ImmutableArray.CreateBuilder<string>();
        if (ClaudeJson.TryGetArray(root, "tools", out var toolArray))
        {
            foreach (var tool in toolArray.EnumerateArray())
            {
                if (ClaudeJson.AsString(tool) is { } name)
                    tools.Add(name);
            }
        }

        var servers = ImmutableArray.CreateBuilder<string>();
        if (ClaudeJson.TryGetArray(root, "mcp_servers", out var serverArray))
        {
            foreach (var server in serverArray.EnumerateArray())
            {
                if (ClaudeJson.GetString(server, "name") is not { } name)
                    continue;
                var status = ClaudeJson.GetString(server, "status");
                servers.Add(status is null ? name : $"{name} ({status})");
            }
        }

        return new SessionInit(
            ClaudeJson.GetString(root, "cwd"),
            ClaudeJson.GetString(root, "permissionMode"),
            ClaudeJson.GetString(root, "claude_code_version"),
            tools.ToImmutable(),
            servers.ToImmutable());
    }

    private void AddAssistant(JsonElement root, DateTimeOffset? time)
    {
        var agentId = AgentOf(root, time);
        if (!ClaudeJson.TryGetObject(root, "message", out var message))
            return;

        // 35.7: the session's model comes from the agent's own events only.
        var model = ClaudeJson.GetString(message, "model");
        if (agentId is null)
            SetModel(model);

        var callId = ClaudeJson.GetString(message, "id");
        if (callId is not null && _callIds.Add(callId))
        {
            _calls.Add(new ModelCall(callId, model, time, ReadCallUsage(message)) { AgentId = agentId });
            if (agentId is not null)
                SetSubAgentModel(agentId, model);
            Changed();
        }

        if (!ClaudeJson.TryGetArray(message, "content", out var blocks))
            return;

        foreach (var block in blocks.EnumerateArray())
        {
            switch (ClaudeJson.GetString(block, "type"))
            {
                case "text" when ClaudeJson.GetString(block, "text") is { } text:
                    AddItem(new AssistantText(callId, time, text) { AgentId = agentId });
                    break;
                case "thinking":
                    var thinking = ClaudeJson.GetString(block, "thinking") ?? "";
                    AddItem(new Thinking(callId, time, thinking, _thinkingTokens) { AgentId = agentId });
                    _thinkingTokens = null;
                    break;
                case "tool_use":
                    AddToolCall(block, callId, time, agentId);
                    break;
            }
        }
    }

    /// <summary>
    /// 35.2: the sub-agent an assistant or user event belongs to, by its parent_tool_use_id; null for the agent's
    /// own. A sub-agent seen here for the first time is added from the event's task_description and subagent_type.
    /// </summary>
    private string? AgentOf(JsonElement root, DateTimeOffset? time)
    {
        if (ClaudeJson.GetString(root, "parent_tool_use_id") is not { Length: > 0 } agentId)
            return null;

        if (!_subAgentIndex.ContainsKey(agentId))
        {
            var description = ClaudeJson.GetString(root, "task_description");
            AddSubAgent(new SubAgent(
                agentId, null, agentId, SubAgents.Name(description), description,
                ClaudeJson.GetString(root, "subagent_type"), null, false, "",
                time, null, SessionState.Running, null));
        }

        return agentId;
    }

    private static TokenUsage? ReadCallUsage(JsonElement message) =>
        ClaudeJson.TryGetObject(message, "usage", out var usage)
            ? new TokenUsage(
                ClaudeJson.GetInt64(usage, "input_tokens") ?? 0,
                ClaudeJson.GetInt64(usage, "cache_read_input_tokens") ?? 0,
                ClaudeJson.GetInt64(usage, "cache_creation_input_tokens") ?? 0,
                null)
            : null;

    private void AddToolCall(JsonElement block, string? callId, DateTimeOffset? time, string? agentId)
    {
        var toolId = ClaudeJson.GetString(block, "id") ?? "";
        var name = ClaudeJson.GetString(block, "name") ?? "";
        var hasInput = ClaudeJson.TryGetProperty(block, "input", out var input);
        var inputJson = hasInput ? ClaudeJson.Write(input, indented: false) : "{}";
        var summary = ClaudeToolSummary.Create(name, input, _init?.Cwd ?? _workDir);

        if (toolId.Length > 0)
            _toolCallIndex[toolId] = _items.Count;
        AddItem(new ToolCall(callId, time, toolId, name, inputJson, summary, null) { AgentId = agentId });

        // 35.1: an Agent (formerly Task) tool use starts a sub-agent; its first appearance wins.
        if (name is "Agent" or "Task" && toolId.Length > 0 && !_subAgentIndex.ContainsKey(toolId))
        {
            var description = ClaudeJson.GetString(input, "description");
            AddSubAgent(new SubAgent(
                toolId, agentId, toolId, SubAgents.Name(description), description,
                ClaudeJson.GetString(input, "subagent_type"), null,
                ClaudeJson.GetBoolean(input, "run_in_background") == true,
                ClaudeJson.GetString(input, "prompt") ?? "",
                time, null, SessionState.Running, null));
        }
    }

    private void AddUser(JsonElement root, DateTimeOffset? time)
    {
        var agentId = AgentOf(root, time);
        if (!ClaudeJson.TryGetObject(root, "message", out var message) ||
            !ClaudeJson.TryGetProperty(message, "content", out var content))
            return;

        var isSynthetic = ClaudeJson.GetBoolean(root, "isSynthetic") ?? false;
        if (ClaudeJson.AsString(content) is { } text)
        {
            AddUserText(text, time, isSynthetic, agentId);
            return;
        }

        if (content.ValueKind != JsonValueKind.Array)
            return;

        foreach (var block in content.EnumerateArray())
        {
            switch (ClaudeJson.GetString(block, "type"))
            {
                case "text" when ClaudeJson.GetString(block, "text") is { } blockText:
                    AddUserText(blockText, time, isSynthetic, agentId);
                    break;
                case "tool_result":
                    AddToolResult(root, block, time);
                    break;
            }
        }
    }

    private void AddUserText(string text, DateTimeOffset? time, bool isSynthetic, string? agentId)
    {
        // 35.2: a sub-agent's user text that repeats its prompt is not added; the SubAgent holds the prompt.
        if (agentId is not null && _subAgentIndex.TryGetValue(agentId, out var index) && text == _subAgents[index].Prompt)
            return;

        AddItem(new UserText(null, time, text, isSynthetic) { AgentId = agentId });
    }

    private void AddToolResult(JsonElement root, JsonElement block, DateTimeOffset? time)
    {
        var toolId = ClaudeJson.GetString(block, "tool_use_id");
        var isError = ClaudeJson.GetBoolean(block, "is_error") ?? false;
        var content = ReadResultContent(block);

        // 35.3 fallback: the hand-back of a foreground sub-agent that no notification finished. A sub-agent's
        // ToolCallId equals its Id here.
        var namesSubAgent = false;
        if (toolId is not null && _subAgentIndex.TryGetValue(toolId, out var subIndex))
        {
            namesSubAgent = true;
            if (!_subAgents[subIndex].Background)
                FinishSubAgent(subIndex, isError ? SessionState.Failed : SessionState.Succeeded, content, time ?? _lastEventAt);
        }

        if (toolId is null || !_toolCallIndex.TryGetValue(toolId, out var index))
        {
            if (!namesSubAgent)
                CountUnparsed();
            return;
        }

        var result = new ToolResult(time, isError, content, ReadDiff(root), null);
        _items[index] = (ToolCall)_items[index] with { Result = result };
        Changed();
    }

    private static string ReadResultContent(JsonElement block)
    {
        if (!ClaudeJson.TryGetProperty(block, "content", out var content))
            return "";
        if (ClaudeJson.AsString(content) is { } text)
            return text;
        if (content.ValueKind != JsonValueKind.Array)
            return "";

        var texts = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            if (ClaudeJson.GetString(part, "type") == "text" && ClaudeJson.GetString(part, "text") is { } partText)
                texts.Add(partText);
        }

        return string.Join('\n', texts);
    }

    /// <summary>Diff text from the top-level <c>tool_use_result.structuredPatch</c>; null when it has no hunks.</summary>
    private static string? ReadDiff(JsonElement root)
    {
        if (!ClaudeJson.TryGetObject(root, "tool_use_result", out var toolUseResult) ||
            !ClaudeJson.TryGetArray(toolUseResult, "structuredPatch", out var hunks) ||
            hunks.GetArrayLength() == 0)
            return null;

        var diff = new StringBuilder();
        foreach (var hunk in hunks.EnumerateArray())
        {
            if (hunk.ValueKind != JsonValueKind.Object)
                continue;

            if (diff.Length > 0)
                diff.Append('\n');
            var oldStart = ClaudeJson.GetInt64(hunk, "oldStart") ?? 0;
            var oldLines = ClaudeJson.GetInt64(hunk, "oldLines") ?? 0;
            var newStart = ClaudeJson.GetInt64(hunk, "newStart") ?? 0;
            var newLines = ClaudeJson.GetInt64(hunk, "newLines") ?? 0;
            diff.Append(CultureInfo.InvariantCulture, $"@@ -{oldStart},{oldLines} +{newStart},{newLines} @@");

            if (!ClaudeJson.TryGetArray(hunk, "lines", out var lines))
                continue;
            foreach (var line in lines.EnumerateArray())
            {
                if (ClaudeJson.AsString(line) is { } text)
                    diff.Append('\n').Append(text);
            }
        }

        return diff.Length == 0 ? null : diff.ToString();
    }

    private void AddResult(JsonElement root)
    {
        var text = ClaudeJson.GetString(root, "result");
        var structuredJson = ClaudeJson.TryGetObject(root, "structured_output", out var structured)
            ? ClaudeJson.Write(structured, indented: true)
            : IndentedObject(text);
        var (worker, review) = StructuredOutput.Parse(structuredJson);

        _result = new SessionResult(
            ClaudeJson.GetBoolean(root, "is_error") ?? false,
            ClaudeJson.GetString(root, "subtype") ?? "",
            text,
            structuredJson,
            worker,
            review,
            ClaudeJson.GetDouble(root, "total_cost_usd"),
            ClaudeJson.GetInt32(root, "num_turns"),
            ReadMilliseconds(root, "duration_ms"),
            ReadMilliseconds(root, "duration_api_ms"),
            ReadResultUsage(root),
            ReadContextWindow(root),
            null,
            null,
            null);
        Changed();
    }

    /// <summary>The text as indented JSON when it parses as a JSON object; otherwise null.</summary>
    private static string? IndentedObject(string? text)
    {
        if (text is null)
            return null;

        using var document = ClaudeJson.TryParse(text);
        return document is { RootElement.ValueKind: JsonValueKind.Object }
            ? ClaudeJson.Write(document.RootElement, indented: true)
            : null;
    }

    private static TimeSpan? ReadMilliseconds(JsonElement root, string name) =>
        ClaudeJson.GetDouble(root, name) is { } ms && Math.Abs(ms) <= MaxDurationMs
            ? TimeSpan.FromMilliseconds(ms)
            : null;

    private static TokenUsage? ReadResultUsage(JsonElement root) =>
        ClaudeJson.TryGetObject(root, "usage", out var usage)
            ? new TokenUsage(
                ClaudeJson.GetInt64(usage, "input_tokens") ?? 0,
                ClaudeJson.GetInt64(usage, "cache_read_input_tokens") ?? 0,
                ClaudeJson.GetInt64(usage, "cache_creation_input_tokens") ?? 0,
                ClaudeJson.GetInt64(usage, "output_tokens"))
            : null;

    private static long? ReadContextWindow(JsonElement root)
    {
        if (!ClaudeJson.TryGetObject(root, "modelUsage", out var modelUsage))
            return null;

        long? largest = null;
        foreach (var model in modelUsage.EnumerateObject())
        {
            if (ClaudeJson.GetInt64(model.Value, "contextWindow") is { } window && (largest is null || window > largest))
                largest = window;
        }

        return largest;
    }

    private void SetModel(string? model)
    {
        if (model is null || model == _model)
            return;

        _model = model;
        Changed();
    }

    private void AddSubAgent(SubAgent subAgent)
    {
        _subAgentIndex[subAgent.Id] = _subAgents.Count;
        _subAgents.Add(subAgent);
        Changed();
    }

    /// <summary>35.7: a sub-agent's model is the model of its first call (the first that names one).</summary>
    private void SetSubAgentModel(string agentId, string? model)
    {
        if (model is null || !_subAgentIndex.TryGetValue(agentId, out var index) || _subAgents[index].Model is not null)
            return;

        _subAgents[index] = _subAgents[index] with { Model = model };
        Changed();
    }

    /// <summary>35.3: finishes a running sub-agent; a finished one stays as it is (4.5).</summary>
    private void FinishSubAgent(int index, SessionState state, string? report, DateTimeOffset? finishedAt)
    {
        if (_subAgents[index].State != SessionState.Running)
            return;

        _subAgents[index] = _subAgents[index] with { State = state, FinishedAt = finishedAt, Report = report };
        Changed();
    }

    private void AddItem(ConversationItem item)
    {
        _items.Add(item);
        Changed();
    }

    private void CountUnparsed()
    {
        _unparsedLines++;
        Changed();
    }

    private void Changed() => _content = null;
}
