namespace OrchDash.Pages.Usage.Format;

// The figures of the usage rules for one session, a group or the run; null is unknown.
// LinesAdded and LinesRemoved are known together or not at all.
public sealed record UsageFigures(int Sessions, int Calls,
    long? Input, long? CacheRead, long? CacheWrite, long? Output, long? Thinking, long? PeakContext,
    double? CostUsd, double? PremiumRequests, long? NanoAiu, int? LinesAdded, int? LinesRemoved)
{
    // Input + cache read + cache write + output, an unknown figure counting as 0.
    public long Tokens => (Input ?? 0) + (CacheRead ?? 0) + (CacheWrite ?? 0) + (Output ?? 0);
}
