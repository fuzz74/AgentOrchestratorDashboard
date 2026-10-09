using System.Collections.Immutable;
using OrchDash.Contracts;

namespace OrchDash.Pages.Conversation.Format;

// One entry of a session's conversation. Lines are 1 to 3 markup lines; PopupTitle is plain text.
// An empty Popup means that the entry opens no pop-up (CallSeparator, NoEventLog).
public sealed record ConversationEntry(
    EntryKind Kind,
    ImmutableArray<string> Lines,
    string PopupTitle,
    ImmutableArray<PopupSection> Popup)
{
    // The id of the sub-agent that this entry started (41.3): Enter or a click selects it instead of opening the pop-up.
    public string? SelectsAgentId { get; init; }
}
