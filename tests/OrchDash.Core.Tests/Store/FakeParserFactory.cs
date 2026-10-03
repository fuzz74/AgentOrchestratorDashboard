using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Store;

// Creates FakeSessionParser instances and records each call.
public sealed class FakeParserFactory
{
    private readonly Lock _lock = new();
    private readonly List<FakeSessionParser> _parsers = [];
    private string? _throwOnLine;

    public IReadOnlyList<FakeSessionParser> Parsers
    {
        get { lock (_lock) return [.. _parsers]; }
    }

    public IReadOnlyList<(Provider Provider, string? WorkDir)> Calls =>
        [.. Parsers.Select(p => (p.Provider, p.WorkDir))];

    // The next parser that receives this line throws once.
    public void ThrowOnceOn(string line)
    {
        lock (_lock)
            _throwOnLine = line;
    }

    public ISessionParser Create(Provider provider, string? workDir)
    {
        var parser = new FakeSessionParser(provider, workDir, this);
        lock (_lock)
            _parsers.Add(parser);
        return parser;
    }

    internal bool TakeThrow(string line)
    {
        lock (_lock)
        {
            if (_throwOnLine != line)
                return false;
            _throwOnLine = null;
            return true;
        }
    }
}
