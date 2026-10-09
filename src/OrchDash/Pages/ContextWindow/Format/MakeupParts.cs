using System.Collections.Immutable;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation.Format;

namespace OrchDash.Pages.ContextWindow.Format;

// The parts of the make-up rules, with exact tokens where the source has them and no estimates yet.
internal static class MakeupParts
{
    // The parts with a birth below the number of calls, by birth, then table row (= category), then source order.
    // System prompt, tools and injected items come from stores and belong to session; each segment adds its prompt
    // and its items, from its first call in calls on.
    public static ImmutableArray<ContextPart> Collect(Session session, StoreData stores,
        ImmutableArray<MakeupSegment> segments, ImmutableArray<ChainCall> calls, ContextCheckpoint? checkpoint)
    {
        var n = calls.Length;
        var parts = new List<ContextPart>();
        AddSystemPrompt(parts, session, stores, checkpoint);
        AddTools(parts, session, stores, checkpoint);
        AddInjected(parts, session, stores, calls);

        var firstCalls = new int[segments.Length];
        for (int i = 0, offset = 0; i < segments.Length; offset += segments[i].Calls.Length, i++)
            firstCalls[i] = segments[i].Calls.IsEmpty ? n : offset;

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            parts.Add(Part(PartCategory.Prompt, PartKind.Prompt, segment.PromptLabel, segment.Prompt.Length, null,
                firstCalls[i], segment.Session, segment.PromptSource));
        }
        for (var i = 0; i < segments.Length; i++)
            AddItems(parts, segments[i], firstCalls[i]);

        return [.. parts.Where(p => p.Birth < n).OrderBy(p => p.Birth).ThenBy(p => p.Category)];
    }

    public static ImmutableArray<T> OrEmpty<T>(ImmutableArray<T> array) => array.IsDefault ? [] : array;

    // The store data of the agent: the session's for null or an id the session has no sub-agent with; else the
    // sub-agent's own (36.1), or StoreData.Empty when the stores hold none for it (42.3).
    public static StoreData StoresOf(Session session, string? agentId) =>
        SubAgents.Find(session.Content, agentId) is not { } sub ? session.Stores
        : session.Stores.SubAgents.TryGetValue(sub.Id, out var stores) ? stores
        : StoreData.Empty;

    private static void AddSystemPrompt(List<ContextPart> parts, Session session, StoreData stores,
        ContextCheckpoint? checkpoint)
    {
        ImmutableArray<TokenPart> segments = checkpoint is null ? [] : OrEmpty(checkpoint.SystemSegments);
        if (!segments.IsEmpty)
        {
            foreach (var segment in segments)
                parts.Add(Part(PartCategory.SystemPrompt, PartKind.SystemSegment, segment.Name, 0, segment.Tokens, 0,
                    session, segment));
            return;
        }

        var blocks = OrEmpty(stores.SystemPrompt);
        for (var i = 0; i < blocks.Length; i++)
            parts.Add(Part(PartCategory.SystemPrompt, PartKind.SystemBlock, "block " + Words.Number(i + 1),
                blocks[i].Length, null, 0, session, blocks[i]));
    }

    private static void AddTools(List<ContextPart> parts, Session session, StoreData stores,
        ContextCheckpoint? checkpoint)
    {
        var tools = OrEmpty(stores.Tools);
        foreach (var tool in tools)
            parts.Add(Part(PartCategory.ToolDefinitions, PartKind.ToolDefinition, tool.Name,
                (tool.Description?.Length ?? 0) + (tool.SchemaJson?.Length ?? 0), null, 0, session, tool));

        if (tools.IsEmpty && checkpoint?.ToolTokens is { } toolTokens)
            parts.Add(Part(PartCategory.ToolDefinitions, PartKind.ToolSummary,
                Words.Number(OrEmpty(checkpoint.ToolNames).Length) + " tools", 0, toolTokens, 0, session, checkpoint));
    }

    // Born at the first call that starts after the item's time.
    private static void AddInjected(List<ContextPart> parts, Session session, StoreData stores,
        ImmutableArray<ChainCall> calls)
    {
        foreach (var item in OrEmpty(stores.Injected))
        {
            var birth = 0;
            if (item.Time is { } time)
            {
                birth = calls.Length;
                foreach (var call in calls)
                {
                    if (call.Call.StartedAt > time)
                    {
                        birth = call.Index;
                        break;
                    }
                }
            }
            parts.Add(Part(PartCategory.Injected, PartKind.Injected, item.Kind, item.Text.Length, null, birth,
                session, item));
        }
    }

    // An item is born at the call after the one that made it; an item without a known CallId follows the nearest
    // earlier item that has one, or is born at the segment's first call. The segment holds one agent's calls and
    // items, so a CallId is looked up among the calls of the item's own agent (Copilot turn ids repeat, 35.4).
    private static void AddItems(List<ContextPart> parts, MakeupSegment segment, int firstCall)
    {
        var callIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < segment.Calls.Length; i++)
            callIndex.TryAdd(segment.Calls[i].Id, i);

        int? lastCall = null;
        foreach (var item in segment.Items)
        {
            if (item.CallId is { } id && callIndex.TryGetValue(id, out var own))
                lastCall = own;
            var birth = lastCall is { } call ? firstCall + call + 1 : firstCall;

            if (Describe(item) is { } described)
                parts.Add(Part(PartCategory.Conversation, described.Kind, described.Label, described.Characters, null,
                    birth, segment.Session, item));
        }
    }

    private static (PartKind Kind, string Label, long Characters)? Describe(ConversationItem item) => item switch
    {
        ToolCall tool => (PartKind.ToolCall, tool.Summary, tool.InputJson.Length + (tool.Result?.Content.Length ?? 0)),
        AssistantText text => (PartKind.Item, "text", text.Text.Length),
        Thinking thinking => (PartKind.Item, "thinking", thinking.Text.Length),
        UserText user => (PartKind.Item, "user text", user.Text.Length),
        Notice notice => (PartKind.Item, "notice", notice.Text.Length),
        _ => null,
    };

    private static ContextPart Part(PartCategory category, PartKind kind, string label, long characters,
        long? exactTokens, int birth, Session session, object source) =>
        new(category, kind, label, characters, exactTokens, false, birth, session, source);
}
