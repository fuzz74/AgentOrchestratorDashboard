namespace OrchDash.Core.Tests.Transcript;

/// <summary>A TimeProvider whose time only moves when a test moves it.</summary>
public sealed class ManualTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public void Advance(TimeSpan by) => Now += by;
}
