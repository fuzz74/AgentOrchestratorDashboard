using System.Collections.Immutable;
using System.Text.Json;
using OrchDash.Core.Model;

namespace OrchDash.Core.SessionFolder;

// Follows one session folder's events.jsonl and maps its lines by the session folder table (spec 4.3, 12.1, 12.2).
// Reads only the bytes added since the last read, as SessionTracker does for the run's logs.
internal sealed class EventsFile
{
    private const FileShare ShareAll = FileShare.ReadWrite | FileShare.Delete;

    private FileStamp _stamp;
    private long _offset;                // byte offset after the last complete line
    private string? _cliVersion;
    private ImmutableArray<string> _systemPrompt = [];
    private int _unparsed;
    private StoreData? _data;

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    // The data for the file with this stamp; the last result (null before the first) when it cannot be read.
    public StoreData? Read(string path, FileStamp stamp)
    {
        if (_data is not null && stamp == _stamp)
            return _data;

        byte[] bytes;
        int count = 0;
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, ShareAll);
            long length = RandomAccess.GetLength(handle);
            if (length < _offset)
                Reset();

            bytes = new byte[(int)Math.Min(length - _offset, Array.MaxLength)];
            int read;
            while (count < bytes.Length && (read = RandomAccess.Read(handle, bytes.AsSpan(count), _offset + count)) > 0)
                count += read;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return _data;
        }

        // The stamp is taken before the read, so bytes written in between are read again next time.
        _stamp = stamp;
        if (AddLines(bytes.AsMemory(0, count)) || _data is null)
            _data = StoreData.Empty with { CliVersion = _cliVersion, SystemPrompt = _systemPrompt, UnparsedLines = _unparsed };
        return _data;
    }

    // The file is shorter than what was read: it was replaced, so it is read again from byte 0.
    private void Reset()
    {
        _offset = 0;
        _cliVersion = null;
        _systemPrompt = [];
        _unparsed = 0;
    }

    // Maps each line ended by '\n'; the bytes after the last '\n' are not complete yet and are read again later.
    // True when at least one line was mapped.
    private bool AddLines(ReadOnlyMemory<byte> bytes)
    {
        bool added = false;
        int consumed = 0;
        int newline;
        while ((newline = bytes.Span[consumed..].IndexOf((byte)'\n')) >= 0)
        {
            var line = bytes.Slice(consumed, newline);
            if (_offset == 0 && consumed == 0 && line.Span.StartsWith(Utf8Bom))
                line = line[Utf8Bom.Length..];
            if (line.Span.EndsWith((byte)'\r'))
                line = line[..^1];

            AddLine(line);
            consumed += newline + 1;
            added = true;
        }
        _offset += consumed;
        return added;
    }

    private void AddLine(ReadOnlyMemory<byte> line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _unparsed++;
                return;
            }
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return;

            switch (String(root, "type"))
            {
                case "session.start":
                    _cliVersion = String(data, "copilotVersion") ?? _cliVersion;
                    break;
                // 36.6: a sub-agent's system message is not the session's system prompt.
                case "system.message" when string.IsNullOrEmpty(String(root, "agentId")):
                    _systemPrompt = SystemPrompt(data);
                    break;
            }
        }
        catch (JsonException)
        {
            _unparsed++;
        }
    }

    // The content of each entry of data.contentBlocks[], or [data.content] when there are no blocks.
    private static ImmutableArray<string> SystemPrompt(JsonElement data)
    {
        if (data.TryGetProperty("contentBlocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array
            && blocks.GetArrayLength() > 0)
        {
            var texts = ImmutableArray.CreateBuilder<string>();
            foreach (var block in blocks.EnumerateArray())
            {
                if (block.ValueKind == JsonValueKind.Object && String(block, "content") is { } text)
                    texts.Add(text);
            }
            return texts.ToImmutable();
        }

        return String(data, "content") is { } content ? [content] : [];
    }

    // Null when the property is missing, not a string, or cannot be decoded (an unpaired surrogate or bytes
    // that are not UTF-8 make GetString throw).
    private static string? String(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
