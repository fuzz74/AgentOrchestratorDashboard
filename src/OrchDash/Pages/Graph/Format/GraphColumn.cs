using System.Collections.Immutable;
using System.Globalization;

namespace OrchDash.Pages.Graph.Format;

/// <summary>One column of the graph: a wave (1-based, titled <c>W&lt;wave&gt;</c>) and its task ids in snapshot order.</summary>
public sealed record GraphColumn(int Wave, ImmutableArray<string> TaskIds)
{
    public string Title => "W" + Wave.ToString(CultureInfo.InvariantCulture);

    /// <summary>40.2: the number of child lines reserved below each card, by card index; a card past its end has none.</summary>
    public ImmutableArray<int> ChildLines { get; init; } = [];

    /// <summary>The number of lines below the titles: the cards and their child lines.</summary>
    public int Lines => TaskIds.Length + ChildLines.Sum();

    /// <summary>The number of child lines below card <paramref name="index"/>.</summary>
    public int ChildLinesOf(int index) => index < ChildLines.Length ? ChildLines[index] : 0;
}
