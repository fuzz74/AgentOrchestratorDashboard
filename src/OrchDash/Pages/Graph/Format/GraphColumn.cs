using System.Collections.Immutable;
using System.Globalization;

namespace OrchDash.Pages.Graph.Format;

/// <summary>One column of the graph: a wave (1-based, titled <c>W&lt;wave&gt;</c>) and its task ids in snapshot order.</summary>
public sealed record GraphColumn(int Wave, ImmutableArray<string> TaskIds)
{
    public string Title => "W" + Wave.ToString(CultureInfo.InvariantCulture);
}
