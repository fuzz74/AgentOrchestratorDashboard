using OrchDash.Core.Model;

namespace OrchDash.Shell;

/// <summary>One row of the <c>Runs</c> dialog.</summary>
/// <param name="Markup">The row's markup.</param>
/// <param name="Entry">The run of the row; null for the header, loading and problem rows.</param>
/// <param name="Shown">True for the row of the run that the dashboard shows.</param>
public sealed record RunsRow(string Markup, RunEntry? Entry, bool Shown);
