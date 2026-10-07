using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Pages.Timeline.Format;

/// <summary>One kind toggle of the Timeline page's filter row (33.2): its key, its label name and the kinds it covers.</summary>
public sealed record KindFilter(char Key, string Name, string CommandLabel, ImmutableArray<TimelineKind> Kinds);
