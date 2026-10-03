using System.Text;
using System.Text.Json;
using OrchDash.Core.Model;

namespace OrchDash.Core.Store;

// Follows one session between polls: tails its events.jsonl, chooses the provider, feeds the parser
// and keeps the prompt text (spec 5.2-5.5).
internal sealed class SessionTracker(SessionParserFactory parsers, string workDir)
{
    private const FileShare ShareAll = FileShare.ReadWrite | FileShare.Delete;

    private readonly List<string> _buffered = [];   // lines read while the provider is unknown
    private long _offset;                            // byte offset after the last complete line
    private ISessionParser? _parser;
    private bool _contentStale;
    private long _promptLength = -1;
    private DateTime _promptWriteTime;

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    public Provider Provider { get; private set; }
    public SessionContent Content { get; private set; } = SessionContent.Empty;
    public string Prompt { get; private set; } = "";

    public void Refresh(SessionFiles files)
    {
        ReadEvents(files.EventsPath);
        if (_contentStale && _parser is not null)
        {
            Content = _parser.Build();
            _contentStale = false;
        }
        ReadPrompt(files.PromptPath);
    }

    // Forgets everything read from the events file, so that the next Refresh starts from byte 0.
    public void Reset()
    {
        _offset = 0;
        _buffered.Clear();
        _parser = null;
        _contentStale = false;
        Provider = Provider.Unknown;
        Content = SessionContent.Empty;
    }

    private void ReadEvents(string path)
    {
        byte[] bytes;
        int count = 0;
        long start;
        try
        {
            if (!File.Exists(path))
                return;

            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, ShareAll);
            long length = RandomAccess.GetLength(handle);
            if (length < _offset)
                Reset();
            if (length == _offset)
                return;

            start = _offset;
            bytes = new byte[(int)Math.Min(length - start, Array.MaxLength)];
            int read;
            while (count < bytes.Length && (read = RandomAccess.Read(handle, bytes.AsSpan(count), start + count)) > 0)
                count += read;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;   // missing or in use: keep what was read so far
        }

        AddLines(bytes.AsSpan(0, count), start);
    }

    // Passes each line ended by '\n' on; the bytes after the last '\n' are read again by a later poll.
    private void AddLines(ReadOnlySpan<byte> bytes, long start)
    {
        int consumed = 0;
        int newline;
        while ((newline = bytes[consumed..].IndexOf((byte)'\n')) >= 0)
        {
            var line = bytes.Slice(consumed, newline);
            if (start + consumed == 0 && line.StartsWith(Utf8Bom))
                line = line[Utf8Bom.Length..];
            if (line.EndsWith((byte)'\r'))
                line = line[..^1];

            AddLine(Encoding.UTF8.GetString(line));
            consumed += newline + 1;
            _offset = start + consumed;
        }
    }

    private void AddLine(string line)
    {
        if (_parser is not null)
        {
            _parser.AddLine(line);
            _contentStale = true;
            return;
        }

        _buffered.Add(line);
        var provider = DetectProvider(line);
        if (provider == Provider.Unknown)
            return;

        var parser = parsers(provider, workDir);
        foreach (var buffered in _buffered)
            parser.AddLine(buffered);
        _buffered.Clear();
        _parser = parser;
        _contentStale = true;
        Provider = provider;
    }

    // Spec 5.4: a JSON object with a top-level session_id is Claude; with data or parentId it is Copilot.
    private static Provider DetectProvider(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Provider.Unknown;
            if (root.TryGetProperty("session_id", out _))
                return Provider.Claude;
            if (root.TryGetProperty("data", out _) || root.TryGetProperty("parentId", out _))
                return Provider.Copilot;
        }
        catch (JsonException)
        {
        }
        return Provider.Unknown;
    }

    // Reads the prompt again only when its length or last write time changed.
    private void ReadPrompt(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            Prompt = "";
            _promptLength = -1;
            return;
        }
        if (info.Length == _promptLength && info.LastWriteTimeUtc == _promptWriteTime)
            return;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, ShareAll);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            Prompt = reader.ReadToEnd();
            _promptLength = info.Length;
            _promptWriteTime = info.LastWriteTimeUtc;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // in use: keep the previous text and try again next poll
        }
    }
}
