using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using OrchDash.Core.Model;

namespace OrchDash.Core.Claude;

/// <summary>
/// Turns the lines of a Claude <c>events.jsonl</c> (stream-json output of headless Claude Code)
/// into <see cref="SessionContent"/> by the Claude event table of spec section 4.3.
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

    private string? _sessionId;
    private string? _model;
    private SessionInit? _init;
    private SessionResult? _result;
    private DateTimeOffset? _firstEventAt;
    private DateTimeOffset? _lastEventAt;
    private int _unparsedLines;
    private int? _thinkingTokens;
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
        var subtype = ClaudeJson.GetString(root, "subtype");
        if (!IsKnown(type, subtype))
            return;

        var time = ReadTimestamp(root);
        ApplyCommon(root, time);
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
        _firstEventAt, _lastEventAt, _unparsedLines);

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
        if (!ClaudeJson.TryGetObject(root, "message", out var message))
            return;

        var model = ClaudeJson.GetString(message, "model");
        SetModel(model);

        var callId = ClaudeJson.GetString(message, "id");
        if (callId is not null && _callIds.Add(callId))
        {
            _calls.Add(new ModelCall(callId, model, time, ReadCallUsage(message)));
            Changed();
        }

        if (!ClaudeJson.TryGetArray(message, "content", out var blocks))
            return;

        foreach (var block in blocks.EnumerateArray())
        {
            switch (ClaudeJson.GetString(block, "type"))
            {
                case "text" when ClaudeJson.GetString(block, "text") is { } text:
                    AddItem(new AssistantText(callId, time, text));
                    break;
                case "thinking":
                    AddItem(new Thinking(callId, time, ClaudeJson.GetString(block, "thinking") ?? "", _thinkingTokens));
                    _thinkingTokens = null;
                    break;
                case "tool_use":
                    AddToolCall(block, callId, time);
                    break;
            }
        }
    }

    private static TokenUsage? ReadCallUsage(JsonElement message) =>
        ClaudeJson.TryGetObject(message, "usage", out var usage)
            ? new TokenUsage(
                ClaudeJson.GetInt64(usage, "input_tokens") ?? 0,
                ClaudeJson.GetInt64(usage, "cache_read_input_tokens") ?? 0,
                ClaudeJson.GetInt64(usage, "cache_creation_input_tokens") ?? 0,
                null)
            : null;

    private void AddToolCall(JsonElement block, string? callId, DateTimeOffset? time)
    {
        var toolId = ClaudeJson.GetString(block, "id") ?? "";
        var name = ClaudeJson.GetString(block, "name") ?? "";
        var hasInput = ClaudeJson.TryGetProperty(block, "input", out var input);
        var inputJson = hasInput ? ClaudeJson.Write(input, indented: false) : "{}";
        var summary = ClaudeToolSummary.Create(name, input, _init?.Cwd ?? _workDir);

        if (toolId.Length > 0)
            _toolCallIndex[toolId] = _items.Count;
        AddItem(new ToolCall(callId, time, toolId, name, inputJson, summary, null));
    }

    private void AddUser(JsonElement root, DateTimeOffset? time)
    {
        if (!ClaudeJson.TryGetObject(root, "message", out var message) ||
            !ClaudeJson.TryGetProperty(message, "content", out var content))
            return;

        var isSynthetic = ClaudeJson.GetBoolean(root, "isSynthetic") ?? false;
        if (ClaudeJson.AsString(content) is { } text)
        {
            AddItem(new UserText(null, time, text, isSynthetic));
            return;
        }

        if (content.ValueKind != JsonValueKind.Array)
            return;

        foreach (var block in content.EnumerateArray())
        {
            switch (ClaudeJson.GetString(block, "type"))
            {
                case "text" when ClaudeJson.GetString(block, "text") is { } blockText:
                    AddItem(new UserText(null, time, blockText, isSynthetic));
                    break;
                case "tool_result":
                    AddToolResult(root, block, time);
                    break;
            }
        }
    }

    private void AddToolResult(JsonElement root, JsonElement block, DateTimeOffset? time)
    {
        if (ClaudeJson.GetString(block, "tool_use_id") is not { } toolId ||
            !_toolCallIndex.TryGetValue(toolId, out var index))
        {
            CountUnparsed();
            return;
        }

        var result = new ToolResult(
            time,
            ClaudeJson.GetBoolean(block, "is_error") ?? false,
            ReadResultContent(block),
            ReadDiff(root),
            null);
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
