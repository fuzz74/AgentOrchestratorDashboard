using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Store;

// Returns whatever Next builds and records the run folder of the last call; disposable, so the store's
// Dispose can be checked (spec 23.6).
public sealed class FakeCommandLogReader(Func<CommandLogData> next) : ICommandLogReader, IDisposable
{
    private readonly Lock _lock = new();
    private Func<CommandLogData> _next = next;
    private Exception? _throwOnce;
    private int _reads;
    private int _disposeCount;
    private int _readsAtDispose;

    public string? LastRunDir { get; private set; }

    public int Reads
    {
        get { lock (_lock) return _reads; }
    }

    public bool Disposed
    {
        get { lock (_lock) return _disposeCount > 0; }
    }

    public int DisposeCount
    {
        get { lock (_lock) return _disposeCount; }
    }

    // The number of reads when Dispose was first called.
    public int ReadsAtDispose
    {
        get { lock (_lock) return _readsAtDispose; }
    }

    public Func<CommandLogData> Next
    {
        get { lock (_lock) return _next; }
        set { lock (_lock) _next = value; }
    }

    public void ThrowOnce(Exception exception)
    {
        lock (_lock)
            _throwOnce = exception;
    }

    public CommandLogData Read(string runDir)
    {
        Func<CommandLogData> next;
        lock (_lock)
        {
            _reads++;
            LastRunDir = runDir;
            if (_throwOnce is { } exception)
            {
                _throwOnce = null;
                throw exception;
            }
            next = _next;
        }
        return next();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposeCount++ == 0)
                _readsAtDispose = _reads;
        }
    }
}
