using OrchDash.Core.Model;

namespace OrchDash.Pages.ContextWindow.Format;

// One part of the context. Tokens null = unknown. Birth is the index of the first call whose context holds the part.
// Session is the chain session the part comes from (S for system prompt, tools and injected items); Source is what
// the part's pop-up shows, by Kind (see PartKind).
public sealed record ContextPart(PartCategory Category, PartKind Kind, string Label, long Characters,
    long? Tokens, bool IsEstimate, int Birth, Session Session, object Source);
