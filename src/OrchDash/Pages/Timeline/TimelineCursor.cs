namespace OrchDash.Pages.Timeline;

/// <summary>
/// The event the user selected last: its key, and whether it was the last row then, which makes the selection follow
/// new rows while the run is active (33.7).
/// </summary>
internal sealed record TimelineCursor(string? Key, bool AtEnd)
{
    /// <summary>The start: no pick yet, which selects the last row and follows.</summary>
    public static TimelineCursor Start { get; } = new(null, true);
}
