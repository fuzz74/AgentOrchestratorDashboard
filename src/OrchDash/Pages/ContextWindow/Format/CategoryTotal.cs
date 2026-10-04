namespace OrchDash.Pages.ContextWindow.Format;

// A category's line at one call. Tokens and Share are null for a call without usage; HasEstimates only with tokens.
public sealed record CategoryTotal(PartCategory Category, long? Tokens, double? Share, long Characters, int Parts,
    bool HasEstimates);
