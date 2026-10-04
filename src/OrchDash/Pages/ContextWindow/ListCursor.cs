namespace OrchDash.Pages.ContextWindow;

/// <summary>
/// The item the user selected last in the call list or the part list: the session it belongs to, its position, and
/// for a call whether it was the chain's last call then, which makes the selection follow new calls while that
/// session is running (15.7).
/// </summary>
internal sealed record ListCursor(string? SessionKey, int Index, bool AtEnd)
{
    public static ListCursor None { get; } = new(null, 0, false);
}
