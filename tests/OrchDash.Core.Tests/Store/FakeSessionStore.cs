using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Store;

// Answers through Next and records every call; tests can return equal content in new arrays on every call.
public sealed class FakeSessionStore(Func<string, string?, StoreData?> next) : ISessionStore
{
    private readonly Lock _lock = new();
    private readonly List<(string SessionId, string? WorkDir)> _calls = [];
    private Func<string, string?, StoreData?> _next = next;

    public FakeSessionStore() : this((_, _) => null)
    {
    }

    public IReadOnlyList<(string SessionId, string? WorkDir)> Calls
    {
        get { lock (_lock) return [.. _calls]; }
    }

    public Func<string, string?, StoreData?> Next
    {
        get { lock (_lock) return _next; }
        set { lock (_lock) _next = value; }
    }

    public StoreData? Read(string sessionId, string? workDir)
    {
        Func<string, string?, StoreData?> next;
        lock (_lock)
        {
            _calls.Add((sessionId, workDir));
            next = _next;
        }
        return next(sessionId, workDir);
    }
}
