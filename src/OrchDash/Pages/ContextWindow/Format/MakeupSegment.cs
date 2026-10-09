using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Pages.ContextWindow.Format;

// One agent's stretch of the make-up's calls: a session of the chain with its own calls and items (42.2), or the
// selected sub-agent with its calls and items (42.3). Its prompt part, labelled PromptLabel with the text Prompt and
// the pop-up source PromptSource (see PartKind.Prompt), is born at its first call.
internal sealed record MakeupSegment(Session Session, string PromptLabel, string Prompt, object PromptSource,
    ImmutableArray<ModelCall> Calls, ImmutableArray<ConversationItem> Items);
