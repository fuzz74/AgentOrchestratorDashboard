using System.Collections.Immutable;
using OrchDash.Contracts;

namespace OrchDash.Pages.ContextWindow.Format;

// A pop-up of the Context page: its title and sections, both plain text.
public sealed record ContextPopup(string Title, ImmutableArray<PopupSection> Sections);
