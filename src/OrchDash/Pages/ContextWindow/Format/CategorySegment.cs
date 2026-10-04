namespace OrchDash.Pages.ContextWindow.Format;

// One segment of the stacked category bar (15.8): the category, its name, its markup colour and its tokens.
public sealed record CategorySegment(PartCategory Category, string Name, string Color, long Tokens);
