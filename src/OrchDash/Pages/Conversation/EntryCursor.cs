namespace OrchDash.Pages.Conversation;

/// <summary>
/// The entry the user selected last: the key of the session or sub-agent it belongs to (an <c>AgentKey</c>, 41.1), its
/// position, and whether it was the last entry then, which makes the selection follow new entries while that session
/// or sub-agent is running (8.7, 41.4).
/// </summary>
internal sealed record EntryCursor(string? SessionKey, int Index, bool AtEnd)
{
    public static EntryCursor None { get; } = new(null, 0, false);
}
