using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Store;

// Returns whatever Next builds; tests build new arrays on every call to check content comparison.
public sealed class FakeRunFolderReader(Func<RunFolderData> next) : IRunFolderReader
{
    private readonly Lock _lock = new();
    private Func<RunFolderData> _next = next;
    private Exception? _throwOnce;
    private int _reads;

    public string? LastRunDir { get; private set; }
    public DateTimeOffset LastNow { get; private set; }

    public int Reads
    {
        get { lock (_lock) return _reads; }
    }

    public Func<RunFolderData> Next
    {
        get { lock (_lock) return _next; }
        set { lock (_lock) _next = value; }
    }

    public void ThrowOnce(Exception exception)
    {
        lock (_lock)
            _throwOnce = exception;
    }

    public RunFolderData Read(string runDir, DateTimeOffset now)
    {
        Func<RunFolderData> next;
        lock (_lock)
        {
            _reads++;
            LastRunDir = runDir;
            LastNow = now;
            if (_throwOnce is { } exception)
            {
                _throwOnce = null;
                throw exception;
            }
            next = _next;
        }
        return next();
    }
}
