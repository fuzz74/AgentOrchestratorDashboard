using OrchDash.Core.Model;

namespace OrchDash.Pages.ContextWindow.Format;

// A model call of the chain; Index counts from 0 over the whole chain and is shown as "call <Index + 1>".
public sealed record ChainCall(int Index, Session Session, ModelCall Call);
