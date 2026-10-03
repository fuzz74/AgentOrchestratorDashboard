namespace OrchDash.Core.Tests.Store;

// A TimeProvider whose local time zone is UTC and whose time only moves when a test sets it.
public sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
}
