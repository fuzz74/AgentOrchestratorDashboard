using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using OrchDash.Core.Model;

namespace OrchDash.Core.Transcript;

/// <summary>Maps the lines of a Claude Code transcript to one <see cref="StoreData"/> by the transcript table of spec 4.3.</summary>
internal sealed class TranscriptMapper
{
    private readonly List<CallFigures> _calls = [];
    private readonly Dictionary<string, int> _callIndex = new(StringComparer.Ordinal);
    private readonly ImmutableArray<InjectedItem>.Builder _injected = ImmutableArray.CreateBuilder<InjectedItem>();

    private string? _cliVersion;
    private ImmutableArray<string> _systemPrompt = [];
    private ImmutableArray<ToolDefinition> _tools = [];
    private double? _costUsd;
    private int? _linesAdded;
    private int? _linesRemoved;
    private int _unparsedLines;

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>Maps every line ended by '\n'; the bytes after the last '\n' are not read (spec 11.4).</summary>
    public static StoreData Map(ReadOnlySpan<byte> bytes)
    {
        var mapper = new TranscriptMapper();
        if (bytes.StartsWith(Utf8Bom))
            bytes = bytes[Utf8Bom.Length..];

        int newline;
        while ((newline = bytes.IndexOf((byte)'\n')) >= 0)
        {
            var line = bytes[..newline];
            if (line.EndsWith((byte)'\r'))
                line = line[..^1];

            mapper.AddLine(Encoding.UTF8.GetString(line));
            bytes = bytes[(newline + 1)..];
        }
        return mapper.Build();
    }

    private StoreData Build() => new(
        _cliVersion, _systemPrompt, _tools, _injected.ToImmutable(), [.. _calls],
        _costUsd, _linesAdded, _linesRemoved, _unparsedLines);

    private void AddLine(string line)
    {
        using var document = TranscriptJson.TryParse(line);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            _unparsedLines++;
            return;
        }

        var root = document.RootElement;
        if (TranscriptJson.IsTrue(root, "isSidechain"))
            return;

        if (TranscriptJson.GetString(root, "version") is { } version)
            _cliVersion = version;

        switch (TranscriptJson.GetString(root, "type"))
        {
            case "assistant":
                AddAssistant(root);
                break;
            case "attachment":
                AddAttachment(root);
                break;
            case "cost-state":
                _costUsd = TranscriptJson.GetDouble(root, "totalCostUSD");
                _linesAdded = TranscriptJson.GetInt32(root, "totalLinesAdded");
                _linesRemoved = TranscriptJson.GetInt32(root, "totalLinesRemoved");
                break;
        }
    }

    // The first record of a message id adds the call; every record of the id sets its figures again.
    private void AddAssistant(JsonElement root)
    {
        var message = TranscriptJson.GetObject(root, "message");
        if (TranscriptJson.GetString(message, "id") is not { } id)
            return;

        var usage = TranscriptJson.GetObject(message, "usage");
        var tokens = new TokenUsage(
            TranscriptJson.GetInt64(usage, "input_tokens") ?? 0,
            TranscriptJson.GetInt64(usage, "cache_read_input_tokens") ?? 0,
            TranscriptJson.GetInt64(usage, "cache_creation_input_tokens") ?? 0,
            TranscriptJson.GetInt64(usage, "output_tokens") ?? 0);
        var thinking = TranscriptJson.GetInt64(TranscriptJson.GetObject(usage, "output_tokens_details"), "thinking_tokens");
        var stopReason = TranscriptJson.GetString(message, "stop_reason");

        if (_callIndex.TryGetValue(id, out var index))
        {
            _calls[index] = _calls[index] with { Usage = tokens, ThinkingTokens = thinking, StopReason = stopReason };
            return;
        }

        _callIndex.Add(id, _calls.Count);
        _calls.Add(new CallFigures(id, TranscriptJson.GetTime(root, "timestamp"), tokens, thinking, null, null, stopReason));
    }

    private void AddAttachment(JsonElement root)
    {
        var attachment = TranscriptJson.GetObject(root, "attachment");
        var kind = TranscriptJson.GetString(attachment, "type");
        if (kind == "prompt_snapshot")
        {
            AddSnapshot(attachment);
            return;
        }

        if (!TranscriptJson.TryGetArray(root, "rendered", out var rendered) || rendered.GetArrayLength() == 0)
            return;

        var texts = rendered.EnumerateArray()
            .Select(entry => TranscriptJson.GetString(entry, "content"))
            .OfType<string>();
        _injected.Add(new InjectedItem(
            kind ?? "",
            TranscriptJson.GetString(root, "renderedRole") ?? "system",
            TranscriptJson.GetTime(root, "timestamp"),
            string.Join('\n', texts)));
    }

    // The latest snapshot sets the system prompt; the latest snapshot with tools sets the tools.
    private void AddSnapshot(JsonElement attachment)
    {
        _systemPrompt = TranscriptJson.TryGetArray(attachment, "systemPrompt", out var blocks)
            ? [.. blocks.EnumerateArray().Select(TranscriptJson.AsString).OfType<string>()]
            : [];

        if (TranscriptJson.TryGetArray(attachment, "tools", out var tools))
        {
            _tools = [.. tools.EnumerateArray()
                .Where(tool => tool.ValueKind == JsonValueKind.Object)
                .Select(ReadTool)];
        }
    }

    private static ToolDefinition ReadTool(JsonElement tool)
    {
        string? schemaJson = null;
        if (TranscriptJson.TryGetProperty(tool, "schema", out var schema) && schema.ValueKind != JsonValueKind.Null)
        {
            schemaJson = TranscriptJson.TryGetProperty(schema, "input_schema", out var input) && input.ValueKind != JsonValueKind.Null
                ? TranscriptJson.WriteIndented(input)
                : TranscriptJson.WriteIndented(schema);
        }

        return new ToolDefinition(
            TranscriptJson.GetString(tool, "name") ?? "",
            TranscriptJson.GetString(tool, "description"),
            schemaJson);
    }
}
