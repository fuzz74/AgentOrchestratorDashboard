using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation.Format;

namespace OrchDash.Pages.ContextWindow.Format;

// The text of the Context page (15.1, 15.4-15.6, 15.8-15.12, 42.1-42.3): rows, header lines and pop-ups built on the
// make-up.
// Lines are markup; every piece of model text goes through Look.Tag. Pop-up titles and texts are plain text.
// Columns are separated by two spaces and padded so that the rows of one list line up.
public static class ContextText
{
    public const string Missing = "-";

    private const string Gap = "  ";
    private const string Dot = " · ";
    private const string ChildIndent = "  ";  // a child row starts below the session row's name (42.1)
    private const int IconWidth = 2;          // "✔ "
    private const int RoleWidth = 9;          // "bootstrap"
    private const int AttemptWidth = 4;       // "#1.1"
    private const int ClockWidth = 8;         // "12:00:10"
    private const int TokensWidth = 6;        // "999.9k"
    private const int StepWidth = 7;          // "+999.9k"
    private const int CategoryWidth = 16;     // "Tool definitions"
    private const int ShareWidth = 5;         // "100 %"
    private const int CharactersWidth = 12;   // "999999 chars"
    private const int PartsWidth = 8;         // "99 parts"

    // The task id, or the role for bootstrap and planner; the first column of a session row.
    public static string SessionName(Session session) => session.Files.TaskId ?? Words.Role(session.Files.Role);

    // 15.1: state icon, name (padded to nameWidth), role, #<attempt> and the context of the agent's own latest call
    // with usage (42.2).
    public static string SessionRow(Session session, int nameWidth = 0)
    {
        var files = session.Files;
        var latest = SubAgents.Calls(session.Content, null).LastOrDefault(c => c.Usage is not null);
        return string.Join(Gap,
            Look.Tag(Look.Color(session.State), Look.Icon(session.State)) + " " +
            Cell("", SessionName(session), nameWidth),
            Cell(Look.Color(files.Role), Words.Role(files.Role), RoleWidth),
            Cell("", Words.Attempt(files), AttemptWidth),
            Right(Tokens(latest?.Usage?.Context), TokensWidth));
    }

    // 42.1: "  <Prefix><icon> <Name>" below the session's row, then the context of the sub-agent's latest call with
    // usage, ending where the context of the session rows of that nameWidth ends. The name is padded, or cut so that
    // one space stays before the context.
    public static string ChildRow(AgentRow row, int nameWidth = 0)
    {
        var state = SubAgents.StateOf(row.Session, row.SubAgent);
        var latest = SubAgents.Calls(row.Session.Content, row.SubAgent.Id).LastOrDefault(c => c.Usage is not null);
        var context = Tokens(latest?.Usage?.Context);
        var lead = ChildIndent + row.Prefix + Look.Icon(state) + " ";
        var nameRoom = Math.Max(0, SessionRowWidth(nameWidth) - lead.Length - " ".Length - context.Length);
        return ChildIndent + row.Prefix + Look.Tag(Look.Color(state), Look.Icon(state)) + " " +
            Look.Tag("", Fit(row.SubAgent.Name, nameRoom)) + " " + context;
    }

    // 15.4: the first line, then the unavailable reasons and the unparsed lines while there are any. 42.3: with
    // agentId naming a sub-agent of the session, the first line is the sub-agent's; the other lines stay the
    // session's, whose files hold the sub-agent's events too.
    public static ImmutableArray<string> Header(RunSnapshot snapshot, Session session, ContextMakeup makeup,
        int selectedCall, string? agentId = null)
    {
        var lines = ImmutableArray.CreateBuilder<string>(3);
        lines.Add(SubAgents.Find(session.Content, agentId) is { } sub
            ? SubAgentFirstLine(snapshot, session, sub, makeup, selectedCall)
            : SessionFirstLine(snapshot, session, makeup, selectedCall));
        if (!session.Unavailable.IsDefaultOrEmpty)
            lines.Add(Look.Tag("warning", "unavailable: " + string.Join(", ", session.Unavailable)));
        if (session.Stores.UnparsedLines > 0)
            lines.Add(Look.Tag("warning", $"{Words.Number(session.Stores.UnparsedLines)} lines not understood"));
        return lines.ToImmutable();
    }

    // 15.4: provider, role, task id, model, then the figures with the session's limit.
    private static string SessionFirstLine(RunSnapshot snapshot, Session session, ContextMakeup makeup,
        int selectedCall)
    {
        var files = session.Files;
        var first = new List<string>
        {
            Look.Tag("", session.Provider.ToString()),
            Look.Tag(Look.Color(files.Role), Words.Role(files.Role)),
        };
        if (files.TaskId is { } taskId)
            first.Add(Look.Tag("", taskId));
        if (!string.IsNullOrEmpty(session.Content.Model))
            first.Add(Look.Tag("", session.Content.Model));
        first.AddRange(Figures(makeup, selectedCall, ContextLimit.For(snapshot, session)));
        return string.Join(Dot, first);
    }

    // 42.3: "<Provider> · sub-agent · <path> · <Model|-> · N calls · peak X · context X of Y", with the largest
    // context window known for the sub-agent's model.
    private static string SubAgentFirstLine(RunSnapshot snapshot, Session session, SubAgent sub, ContextMakeup makeup,
        int selectedCall)
    {
        var first = new List<string>
        {
            Look.Tag("", session.Provider.ToString()),
            "sub-agent",
            Look.Tag("", AgentPath.Of(session, sub.Id)),
            Look.Tag("", string.IsNullOrEmpty(sub.Model) ? Missing : sub.Model),
        };
        first.AddRange(Figures(makeup, selectedCall, ContextLimit.ForModel(snapshot, sub.Model)));
        return string.Join(Dot, first);
    }

    // "N calls", "peak X" and, for a selected call with usage, "context X[ of Y (P %)]".
    private static IEnumerable<string> Figures(ContextMakeup makeup, int selectedCall, long? limit)
    {
        yield return Words.Count(makeup.Calls.Length, "call");
        yield return "peak " + Tokens(makeup.Peak);
        if (makeup.ContextAt(selectedCall) is { } context)
            yield return "context " + Look.ContextSize(context, limit);
    }

    // 15.5: the context of each call with usage, in call order.
    public static ImmutableArray<double> ChartValues(ContextMakeup makeup) =>
        [.. makeup.Calls.Where(c => c.Call.Usage is not null).Select(c => (double)c.Call.Usage!.Context)];

    // 15.6: call <n>, #<attempt> while the chain has more than one session, start, context, step, output, thinking
    // and stop reason; "-" for an unknown figure.
    public static string CallRow(ContextMakeup makeup, int index)
    {
        var chainCall = makeup.Calls[index];
        var call = chainCall.Call;
        var cells = new List<string>
        {
            Cell("", "call " + Words.Number(index + 1), ("call " + Words.Number(makeup.Calls.Length)).Length),
        };
        if (makeup.Chain.Length > 1)
            cells.Add(Cell("", Words.Attempt(chainCall.Session.Files),
                makeup.Chain.Max(s => Words.Attempt(s.Files).Length)));
        cells.Add(Cell("", call.StartedAt is { } start ? Look.Clock(start) : Missing, ClockWidth));
        cells.Add(Right(Tokens(makeup.ContextAt(index)), TokensWidth));
        cells.Add(Right(Step(makeup.StepAt(index)), StepWidth));
        cells.Add(Cell("", "out " + Tokens(call.Usage?.Output), "out ".Length + TokensWidth));
        cells.Add(Cell("", "think " + Tokens(call.ThinkingTokens), "think ".Length + TokensWidth));
        cells.Add(Look.Tag("", call.StopReason ?? Missing));
        return string.Join(Gap, cells);
    }

    public static string CategoryName(PartCategory category) => category switch
    {
        PartCategory.SystemPrompt => "System prompt",
        PartCategory.ToolDefinitions => "Tool definitions",
        PartCategory.Injected => "Injected",
        PartCategory.Prompt => "Prompt",
        PartCategory.Conversation => "Conversation",
        PartCategory.Other => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    // The markup colour of a category, shared by its lines, its rows and its segment of the stacked bar.
    public static string CategoryColor(PartCategory category) => category switch
    {
        PartCategory.SystemPrompt => "blue",
        PartCategory.ToolDefinitions => "magenta",
        PartCategory.Injected => "yellow",
        PartCategory.Prompt => "green",
        PartCategory.Conversation => "cyan",
        PartCategory.Other => "gray",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    // 15.8: name, tokens, share, characters, parts and "est." while any of the tokens are estimates.
    public static string CategoryLine(CategoryTotal total)
    {
        var cells = new List<string>
        {
            Cell(CategoryColor(total.Category), CategoryName(total.Category), CategoryWidth),
            Right(Tokens(total.Tokens), TokensWidth),
            Right(total.Share is { } share ? Look.Percent(share) : Missing, ShareWidth),
            Right(Characters(total.Characters), CharactersWidth),
            Cell("", Words.Count(total.Parts, "part"), total.HasEstimates ? PartsWidth : 0),
        };
        if (total.HasEstimates)
            cells.Add("est.");
        return string.Join(Gap, cells);
    }

    // The tip of a segment of the stacked bar: the category in its colour, then its tokens and share on a second line.
    public static string CategoryTip(CategoryTotal total) =>
        Look.Tag(CategoryColor(total.Category), CategoryName(total.Category)) + "\n" +
        Tokens(total.Tokens) + " tokens" + Dot + (total.Share is { } share ? Look.Percent(share) : Missing);

    // 15.8: the stacked bar's segments, in category order, for the categories with tokens above 0.
    public static ImmutableArray<CategorySegment> CategorySegments(ImmutableArray<CategoryTotal> totals) =>
    [
        .. totals
            .Where(t => t.Tokens > 0)
            .Select(t => new CategorySegment(t.Category, CategoryName(t.Category), CategoryColor(t.Category),
                t.Tokens!.Value)),
    ];

    // 15.9: category, label (padded or cut to labelWidth when it is above 0), tokens, characters and call <birth + 1>.
    public static string PartRow(ContextPart part, int labelWidth = 0)
    {
        var tokens = part.Tokens is { } known ? (part.IsEstimate ? "~" : "") + Look.Tokens(known) : Missing;
        return string.Join(Gap,
            Cell(CategoryColor(part.Category), CategoryName(part.Category), CategoryWidth),
            Look.Tag("", labelWidth > 0 ? Fit(part.Label, labelWidth) : part.Label),
            Right(tokens, TokensWidth + 1),
            Right(Characters(part.Characters), CharactersWidth),
            "call " + Words.Number(part.Birth + 1));
    }

    // 15.10: the title "<category>: <label>" and the sections of the part pop-up table.
    public static ContextPopup PartPopup(ContextPart part)
    {
        ImmutableArray<PopupSection> sections = (part.Kind, part.Source) switch
        {
            (PartKind.SystemBlock, string block) => [new PopupSection("Text", block)],
            (PartKind.Injected, InjectedItem item) => [new PopupSection("Text", item.Text)],
            (PartKind.SystemSegment, TokenPart segment) =>
            [
                new PopupSection("Tokens", $"{Words.Number(segment.Tokens)} tokens, from Copilot's checkpoint. " +
                    "The text is part of the system prompt (key s)."),
            ],
            (PartKind.ToolDefinition, ToolDefinition tool) =>
            [
                new PopupSection("Description", tool.Description ?? ""),
                new PopupSection("Schema", tool.SchemaJson ?? "", TextKind.Json),
            ],
            (PartKind.ToolSummary, ContextCheckpoint checkpoint) =>
                [new PopupSection("Tools", string.Join('\n', MakeupParts.OrEmpty(checkpoint.ToolNames)))],
            (PartKind.Prompt, Session session) =>
                [new PopupSection("Prompt", session.Content.SentPrompt ?? session.Prompt)],
            (PartKind.Prompt, SubAgent sub) => [new PopupSection("Prompt", sub.Prompt)],
            (PartKind.ToolCall, ToolCall call) => ToolCallSections(call),
            (PartKind.Item, ConversationItem item) => [new PopupSection("Text", ItemText(item))],
            _ => throw new ArgumentOutOfRangeException(nameof(part), part.Kind, null),
        };
        return new ContextPopup(CategoryName(part.Category) + ": " + part.Label, sections);
    }

    // 15.11: one section per system prompt block, or the unavailable section. 42.3: with agentId naming a sub-agent of
    // the session, the blocks of its store data.
    public static ContextPopup SystemPromptPopup(Session session, string? agentId = null)
    {
        var blocks = MakeupParts.OrEmpty(MakeupParts.StoresOf(session, agentId).SystemPrompt);
        ImmutableArray<PopupSection> sections = blocks.IsEmpty
            ? [UnavailableSection(session)]
            : [.. blocks.Select((block, i) => new PopupSection(
                $"Block {Words.Number(i + 1)}, {Words.Number(block.Length)} characters", block))];
        return new ContextPopup("System prompt", sections);
    }

    // 15.12: one section per tool definition; else the tokens and names of the chain's last checkpoint; else the
    // unavailable section. 42.3: with agentId naming a sub-agent of the session, the definitions of its store data;
    // its make-up has no checkpoint.
    public static ContextPopup ToolsPopup(Session session, ContextMakeup makeup, string? agentId = null)
    {
        var tools = MakeupParts.OrEmpty(MakeupParts.StoresOf(session, agentId).Tools);
        ImmutableArray<string> names = makeup.LastCheckpoint is { } checkpoint
            ? MakeupParts.OrEmpty(checkpoint.ToolNames)
            : [];

        ImmutableArray<PopupSection> sections;
        if (!tools.IsEmpty)
        {
            sections = [.. tools.Select(t => new PopupSection(t.Name, $"{t.Description}\n\n{t.SchemaJson}"))];
        }
        else if (!names.IsEmpty)
        {
            var lines = makeup.LastCheckpoint!.ToolTokens is { } tokens
                ? names.Insert(0, $"{Words.Number(tokens)} tokens")
                : names;
            sections = [new PopupSection("Tools", string.Join('\n', lines))];
        }
        else
        {
            sections = [UnavailableSection(session)];
        }
        return new ContextPopup("Tool definitions", sections);
    }

    private static ImmutableArray<PopupSection> ToolCallSections(ToolCall call) =>
        call.Result is { } result
            ? [new PopupSection("Input", call.InputJson, TextKind.Json), new PopupSection("Result", result.Content)]
            : [new PopupSection("Input", call.InputJson, TextKind.Json)];

    private static string ItemText(ConversationItem item) => item switch
    {
        AssistantText text => text.Text,
        Thinking thinking => thinking.Text,
        UserText user => user.Text,
        Notice notice => notice.Text,
        _ => throw new ArgumentOutOfRangeException(nameof(item), item.GetType().Name, null),
    };

    // "unavailable: <reasons>", or "unavailable" alone when there is no reason.
    private static PopupSection UnavailableSection(Session session) =>
        new("Unavailable", session.Unavailable.IsDefaultOrEmpty
            ? "unavailable"
            : "unavailable: " + string.Join(", ", session.Unavailable));

    private static string Tokens(long? tokens) => tokens is { } known ? Look.Tokens(known) : Missing;

    // The width of a session row whose name fits nameWidth.
    private static int SessionRowWidth(int nameWidth) =>
        IconWidth + nameWidth + RoleWidth + AttemptWidth + TokensWidth + 3 * Gap.Length;

    // "+<tokens>" or "-<tokens>"; "-" when unknown.
    private static string Step(long? step) => step switch
    {
        null => Missing,
        < 0 => "-" + Look.Tokens(-step.Value),
        _ => "+" + Look.Tokens(step.Value),
    };

    private static string Characters(long characters) => Words.Number(characters) + " chars";

    // The text in its colour, followed by spaces up to the width.
    private static string Cell(string color, string text, int width) =>
        Look.Tag(color, text) + new string(' ', Math.Max(0, width - text.Length));

    // Spaces up to the width, followed by the plain text.
    private static string Right(string text, int width) => text.PadLeft(width);

    // Pads the text to its width, or cuts it so that it ends with "...".
    private static string Fit(string text, int width)
    {
        if (text.Length <= width)
            return text.PadRight(width);
        return width < 3 ? new string('.', width) : string.Concat(text.AsSpan(0, width - 3), "...");
    }
}
