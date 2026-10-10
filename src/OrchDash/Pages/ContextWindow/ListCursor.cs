namespace OrchDash.Pages.ContextWindow;

/// <summary>
/// The item the user selected last in the call list or the part list: the showing of the session or sub-agent it
/// belongs to (see <c>ContextView.Showing</c>), its position, and for a call whether it was the last call then, which
/// makes the selection follow new calls while that session or sub-agent is running (15.7, 42.3).
/// </summary>
internal sealed record ListCursor(int Showing, int Index, bool AtEnd)
{
    public static ListCursor None { get; } = new(0, 0, false);
}
