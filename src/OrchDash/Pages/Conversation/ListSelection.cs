namespace OrchDash.Pages.Conversation;

/// <summary>
/// The selected item of a <see cref="SelectableList"/>: its index (-1 when the list is empty) and the owner of the
/// items (for example a session key), so that a selection of the same index in other items counts as a change.
/// </summary>
internal readonly record struct ListSelection(object? Owner, int Index);
