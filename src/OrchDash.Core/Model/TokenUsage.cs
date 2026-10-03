namespace OrchDash.Core.Model;

public sealed record TokenUsage(long Input, long CacheRead, long CacheWrite, long? Output)
{
    public long Context => Input + CacheRead + CacheWrite;
}
