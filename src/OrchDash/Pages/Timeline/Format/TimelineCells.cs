namespace OrchDash.Pages.Timeline.Format;

/// <summary>The four plain-text columns of one timeline row (33.1), before padding.</summary>
public sealed record TimelineCells(string Time, string Source, string Kind, string Text);
