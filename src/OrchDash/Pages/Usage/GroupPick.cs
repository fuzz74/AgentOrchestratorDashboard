namespace OrchDash.Pages.Usage;

/// <summary>
/// The group the user picked last on the Usage page and the <c>SelectedSessionKey</c> at that moment, so that a key
/// that differs from it was set elsewhere and selects its session's group instead (16.7). The key may name a sub-agent
/// (43.5), so a move between a session and its child rows changes it too.
/// </summary>
internal readonly record struct GroupPick(string Name, string? Key);
