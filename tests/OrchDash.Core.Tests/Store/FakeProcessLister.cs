using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Store;

// Returns whatever Next builds and records the time of the last call; not disposable, like the real lister.
public sealed class FakeProcessLister(Func<ProcessInfo> next) : IProcessLister
{
    private readonly Lock _lock = new();
    private Func<ProcessInfo> _next = next;
    private Exception? _throwOnce;
    private int _lists;

    public DateTimeOffset LastNow { get; private set; }

    public int Lists
    {
        get { lock (_lock) return _lists; }
    }

    public Func<ProcessInfo> Next
    {
        get { lock (_lock) return _next; }
        set { lock (_lock) _next = value; }
    }

    public void ThrowOnce(Exception exception)
    {
        lock (_lock)
            _throwOnce = exception;
    }

    public ProcessInfo List(DateTimeOffset now)
    {
        Func<ProcessInfo> next;
        lock (_lock)
        {
            _lists++;
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
