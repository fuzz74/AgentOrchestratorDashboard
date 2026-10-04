using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Store;

// Answers through Next and records every call with a copy of the ids in the order they were passed.
public sealed class FakeUsageReader(Func<IReadOnlyCollection<string>, UsageRows> next) : IUsageReader
{
    private readonly Lock _lock = new();
    private readonly List<string[]> _calls = [];
    private Func<IReadOnlyCollection<string>, UsageRows> _next = next;

    public FakeUsageReader() : this(_ => UsageRows.Empty)
    {
    }

    public IReadOnlyList<string[]> Calls
    {
        get { lock (_lock) return [.. _calls]; }
    }

    public Func<IReadOnlyCollection<string>, UsageRows> Next
    {
        get { lock (_lock) return _next; }
        set { lock (_lock) _next = value; }
    }

    public UsageRows Read(IReadOnlyCollection<string> sessionIds)
    {
        Func<IReadOnlyCollection<string>, UsageRows> next;
        lock (_lock)
        {
            _calls.Add([.. sessionIds]);
            next = _next;
        }
        return next(sessionIds);
    }
}
