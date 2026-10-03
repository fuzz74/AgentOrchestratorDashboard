namespace OrchDash.Pages.Conversation;

/// <summary>
/// The entry the user selected last: the session it belongs to, its position, and whether it was the last entry then,
/// which makes the selection follow new entries while that session is running (8.7).
/// </summary>
internal sealed record EntryCursor(string? SessionKey, int Index, bool AtEnd)
{
    public static EntryCursor None { get; } = new(null, 0, false);
}
