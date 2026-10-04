using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Store;

// Answers through Next and records every call with a copy of knownIds, sorted ordinally.
public sealed class FakeSessionIdFinder(Func<string, string, DateTimeOffset, IReadOnlySet<string>, string?> next) : ISessionIdFinder
{
    private readonly Lock _lock = new();
    private readonly List<(string Name, string WorkDir, DateTimeOffset StartedAt, string[] KnownIds)> _calls = [];
    private Func<string, string, DateTimeOffset, IReadOnlySet<string>, string?> _next = next;

    public FakeSessionIdFinder() : this((_, _, _, _) => null)
    {
    }

    public IReadOnlyList<(string Name, string WorkDir, DateTimeOffset StartedAt, string[] KnownIds)> Calls
    {
        get { lock (_lock) return [.. _calls]; }
    }

    public Func<string, string, DateTimeOffset, IReadOnlySet<string>, string?> Next
    {
        get { lock (_lock) return _next; }
        set { lock (_lock) _next = value; }
    }

    public string? Find(string name, string workDir, DateTimeOffset startedAt, IReadOnlySet<string> knownIds)
    {
        Func<string, string, DateTimeOffset, IReadOnlySet<string>, string?> next;
        lock (_lock)
        {
            _calls.Add((name, workDir, startedAt, [.. knownIds.Order(StringComparer.Ordinal)]));
            next = _next;
        }
        return next(name, workDir, startedAt, knownIds);
    }
}
