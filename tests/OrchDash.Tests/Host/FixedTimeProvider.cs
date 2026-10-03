namespace OrchDash.Tests.Host;

/// <summary>A clock that stands still until the test moves it; safe to read from the UI thread while the test sets it.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private long _utcTicks = now.UtcTicks;

    public void Set(DateTimeOffset now) => Volatile.Write(ref _utcTicks, now.UtcTicks);

    public override DateTimeOffset GetUtcNow() => new(Volatile.Read(ref _utcTicks), TimeSpan.Zero);
}
