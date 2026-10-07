using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Core.Timeline;
using OrchDash.Pages.Conversation.Format;
using OrchDash.Pages.Overview.Format;
using OrchDash.Pages.Timeline.Format;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Pages.Timeline;

public sealed class TimelineTextTests
{
    private static readonly ImmutableArray<TimelineEvent> Events = TimelineBuilder.Build(SampleRun.CreateTimeline());

    private static TimelineEvent Event(string key) => Events.Single(e => e.Key == key);

    /// <summary>The first orchestrator event whose message starts with <paramref name="message"/>.</summary>
    private static TimelineEvent Progress(string message) =>
        Events.First(e => e.Kind == TimelineKind.Orchestrator && e.Entry!.Message.StartsWith(message, StringComparison.Ordinal));

    private static KindFilter Filter(char key) => TimelineText.Filters.Single(f => f.Key == key);

    private const string AlphaWorker = SampleRun.AlphaWorkerKey;
    private const string AlphaReview = SampleRun.AlphaReviewKey;
    private const string BetaWorker = SampleRun.BetaWorkerKey;
    private const string GammaWorker1 = SampleRun.GammaWorker1Key;

    [Fact]
    public void The_sample_timeline_gives_sixty_rows_with_padded_source_and_kind_columns()
    {
        Assert.Equal(
        [
            @"12:00:00  orchestrator         orch    Run started: 5 tasks, max 2 in parallel",
            @"12:00:00  orchestrator         orch    Claude: C:\Users\sample\.local\bin\claude.exe",
            @"12:00:05  orchestrator         orch    [alpha] started (fresh) in C:\Work\SampleRepo.worktrees\alpha",
            @"12:00:05  orchestrator         orch    [gamma] started (fresh) in C:\Work\SampleRepo.worktrees\gamma",
            @"12:00:08  orchestrator         orch    [alpha] setup: dotnet restore Sample.slnx",
            @"12:00:09  orchestrator         orch    [alpha] attempt 1/3: worker started (sonnet)",
            @"12:00:10  orchestrator         orch    [gamma] setup: dotnet restore Sample.slnx",
            @"12:00:10  alpha worker #1      prompt  prompt sent · 223 chars",
            @"12:00:10  alpha worker #1      call    call 1 · claude-sonnet-4-5 · context 19.2k",
            @"12:00:12  alpha worker #1      text    I will replace the Parser stub in src/Alpha/Parser.cs.",
            @"12:00:15  alpha worker #1      tool    Edit src/Alpha/Parser.cs · ok · 1s",
            @"12:00:28  orchestrator         orch    [gamma] attempt 1/3: worker started (opus)",
            @"12:00:30  gamma worker #1      prompt  prompt sent · 118 chars",
            @"12:00:30  gamma worker #1      call    call 1 · claude-opus-4-5 · context 23.0k",
            @"12:00:32  gamma worker #1      text    I will add the Formatter class in src/Gamma.",
            @"12:00:35  gamma worker #1      tool    Write src/Gamma/Formatter.cs · ok · 1s",
            @"12:02:40  gamma worker #1      call    call 2 · claude-opus-4-5 · context 26.5k",
            @"12:02:45  gamma worker #1      tool    Bash dotnet test tests/Gamma · error · 1m05s",
            @"12:03:00  alpha worker #1      call    call 2 · claude-sonnet-4-5 · context 43.3k",
            @"12:03:05  alpha worker #1      tool    Bash dotnet test tests/Alpha · error · 35s",
            @"12:04:20  gamma worker #1      text    Two gamma tests still fail on empty input.",
            @"12:04:25  orchestrator         orch    [gamma] acceptance: dotnet test tests/Gamma",
            @"12:04:30  gamma worker #1      result  result error_max_turns",
            @"12:04:31  orchestrator         orch    [gamma] acceptance failed (exit 1)",
            @"12:05:50  alpha worker #1      text    The parser is in place and all alpha tests pass.",
            @"12:05:50  alpha worker #1      result  result success · done",
            @"12:06:00  orchestrator         orch    [alpha] acceptance: dotnet test tests/Alpha",
            @"12:06:25  orchestrator         orch    [alpha] review started (sonnet)",
            @"12:06:30  alpha reviewer #1.1  prompt  prompt sent · 102 chars",
            @"12:06:30  alpha reviewer #1.1  call    call 1 · gpt-5.1 · context 14.2k",
            @"12:06:36  alpha reviewer #1.1  tool    view src/Alpha/Parser.cs · ok · 1s",
            @"12:07:40  alpha reviewer #1.1  call    call 2 · gpt-5.1 · context 17.0k",
            @"12:08:50  alpha reviewer #1.1  text    ```json...",
            @"12:08:55  alpha reviewer #1.1  result  result success · spec pass, quality fail, 2 issues",
            @"12:09:00  orchestrator         orch    [alpha] review passed",
            @"12:09:30  orchestrator         orch    [alpha] DONE in 9m25s, 0.25 USD",
            @"12:10:00  orchestrator         orch    [beta] started (fresh) in C:\Work\SampleRepo.worktrees\beta",
            @"12:10:03  orchestrator         orch    [beta] attempt 1/3: worker started (sonnet)",
            @"12:10:05  beta worker #1       prompt  prompt sent · 125 chars",
            @"12:10:05  beta worker #1       call    call 1 · claude-sonnet-4-5 · context 18.8k",
            @"12:10:08  beta worker #1       text    Let me read the alpha parser first.",
            @"12:10:10  beta worker #1       tool    Read src/Alpha/Parser.cs · ok · 1s",
            @"12:10:55  orchestrator         orch    [gamma] attempt 2/3: worker started (opus)",
            @"12:11:00  gamma worker #2      prompt  prompt sent · 128 chars",
            @"12:11:00  gamma worker #2      call    call 1 · claude-opus-4-5 · context 30.7k",
            @"12:11:05  gamma worker #2      tool    Edit src/Gamma/Formatter.cs · ok · 1s",
            @"12:12:00  beta worker #1       tool    Grep Parse\( in src · ok · 1s",
            @"12:14:30  gamma worker #2      call    call 2 · claude-opus-4-5 · context 33.5k",
            @"12:14:35  gamma worker #2      tool    Bash dotnet test tests/Gamma · error · 1m05s",
            @"12:15:35  orchestrator         orch    [gamma] acceptance: dotnet test tests/Gamma",
            @"12:15:41  orchestrator         orch    [gamma] acceptance failed (exit 1)",
            @"12:16:10  gamma worker #2      text    The two empty-input tests still fail.",
            @"12:16:20  gamma worker #2      result  result error_max_turns",
            @"12:17:00  orchestrator         orch    [gamma] attempt 3/3: worker started (opus)",
            @"12:19:45  orchestrator         orch    [gamma] acceptance: dotnet test tests/Gamma",
            @"12:19:51  orchestrator         orch    [gamma] acceptance failed (exit 1)",
            @"12:20:00  orchestrator         orch    [gamma] FAILED after 3 attempts: acceptance failed",
            @"12:20:00  beta worker #1       call    call 2 · claude-sonnet-4-5 · context 34.5k",
            @"12:20:00  beta worker #1       text    Now I will write the checker and build it.",
            @"12:29:30  beta worker #1       tool    Bash dotnet build src/Beta · running",
        ], TimelineText.PlainRows(Events));
        Assert.Equal(19, TimelineText.SourceWidth(Events));
    }

    [Fact]
    public void The_cells_of_the_first_rows_and_of_each_session_kind_follow_the_row_text_table()
    {
        var alphaPrompt = SampleRun.CreateTimeline().Sessions.Single(s => s.Files.Key == AlphaWorker).Prompt;

        Assert.Equal(new TimelineCells("12:00:00", "orchestrator", "orch", "Run started: 5 tasks, max 2 in parallel"), TimelineText.Cells(Events[0]));
        Assert.Equal(new TimelineCells("12:00:00", "orchestrator", "orch", @"Claude: C:\Users\sample\.local\bin\claude.exe"), TimelineText.Cells(Events[1]));
        Assert.Equal(new TimelineCells("12:00:05", "orchestrator", "orch", @"[alpha] started (fresh) in C:\Work\SampleRepo.worktrees\alpha"), TimelineText.Cells(Events[2]));
        Assert.Equal(new TimelineCells("12:00:10", "alpha worker #1", "prompt", $"prompt sent · {alphaPrompt.Length} chars"), TimelineText.Cells(Event(AlphaWorker + ":prompt")));
        Assert.Equal(new TimelineCells("12:00:10", "alpha worker #1", "call", "call 1 · claude-sonnet-4-5 · context 19.2k"), TimelineText.Cells(Event(AlphaWorker + ":call:0")));
        Assert.Equal(new TimelineCells("12:00:15", "alpha worker #1", "tool", "Edit src/Alpha/Parser.cs · ok · 1s"), TimelineText.Cells(Event(AlphaWorker + ":item:2")));
        Assert.Equal(new TimelineCells("12:05:50", "alpha worker #1", "result", "result success · done"), TimelineText.Cells(Event(AlphaWorker + ":result")));
        Assert.Equal(new TimelineCells("12:08:55", "alpha reviewer #1.1", "result", "result success · spec pass, quality fail, 2 issues"), TimelineText.Cells(Event(AlphaReview + ":result")));
    }

    [Fact]
    public void An_orchestrator_row_shows_the_first_line_of_a_message_of_several()
    {
        Assert.Equal("[alpha] DONE in 9m25s, 0.25 USD", TimelineText.Text(Progress("DONE")));
    }

    [Fact]
    public void Rows_colour_the_kind_and_text_columns_by_the_event_and_escape_every_text()
    {
        Assert.Equal(
            "12:00:00  orchestrator         orch    Run started: 5 tasks, max 2 in parallel",
            TimelineText.Rows(Events)[0]);
        Assert.Equal(
            "12:04:31  orchestrator         [error]orch  [/]  [error][[gamma]] acceptance failed (exit 1)[/]",
            TimelineText.Row(Progress("acceptance failed"), 19));
        Assert.Equal(
            "12:09:00  orchestrator         [success]orch  [/]  [success][[alpha]] review passed[/]",
            TimelineText.Row(Progress("review passed"), 19));
        Assert.Equal(
            "12:00:10  alpha worker #1      [accent]prompt[/]  [accent]prompt sent · 223 chars[/]",
            TimelineText.Row(Event(AlphaWorker + ":prompt"), 19));
        Assert.Equal(
            "12:00:10  alpha worker #1      [muted]call  [/]  [muted]call 1 · claude-sonnet-4-5 · context 19.2k[/]",
            TimelineText.Row(Event(AlphaWorker + ":call:0"), 19));
        Assert.Equal(
            "12:00:12  alpha worker #1      text    I will replace the Parser stub in src/Alpha/Parser.cs.",
            TimelineText.Row(Event(AlphaWorker + ":item:1"), 19));
        Assert.Equal(
            "12:00:15  alpha worker #1      [success]tool  [/]  [success]Edit src/Alpha/Parser.cs · ok · 1s[/]",
            TimelineText.Row(Event(AlphaWorker + ":item:2"), 19));
        Assert.Equal(
            "12:03:05  alpha worker #1      [error]tool  [/]  [error]Bash dotnet test tests/Alpha · error · 35s[/]",
            TimelineText.Row(Event(AlphaWorker + ":item:4"), 19));
        Assert.Equal(
            "12:29:30  beta worker #1       [primary]tool  [/]  [primary]Bash dotnet build src/Beta · running[/]",
            TimelineText.Row(Event(BetaWorker + ":item:5"), 19));
        Assert.Equal(
            "12:05:50  alpha worker #1      [success]result[/]  [success]result success · done[/]",
            TimelineText.Row(Event(AlphaWorker + ":result"), 19));
        Assert.Equal(
            "12:04:30  gamma worker #1      [error]result[/]  [error]result error_max_turns[/]",
            TimelineText.Row(Event(GammaWorker1 + ":result"), 19));
    }

    [Fact]
    public void A_session_without_a_task_id_a_tool_without_summary_and_a_blank_text_have_their_fallbacks()
    {
        var alpha = Event(AlphaWorker + ":prompt").Session!;
        var planner = alpha with { Files = alpha.Files with { TaskId = null, Role = AgentRole.Planner } };
        var start = SampleRun.At(11, 50, 0);
        var tool = new ToolCall(null, start, "toolu_1", "Glob", "{}", "", null);
        var text = new AssistantText(null, null, " \n\n");

        var toolEvent = new TimelineEvent("t", start, TimelineKind.Tool, "planner", 0, null, planner, null, tool, null);
        var textEvent = new TimelineEvent("x", start, TimelineKind.Text, "planner", 1, null, planner, null, text, null);
        var callEvent = new TimelineEvent("c", start, TimelineKind.Call, "planner", 2, null, planner,
            new ModelCall("m", null, null, null), null, null);

        Assert.Equal(new TimelineCells("11:50:00", "planner #1", "tool", "Glob · running"), TimelineText.Cells(toolEvent));
        Assert.Equal(new TimelineCells("11:50:00", "planner #1", "text", "text"), TimelineText.Cells(textEvent));
        Assert.Equal("call 3", TimelineText.Text(callEvent));
        Assert.Equal("", TimelineText.Color(textEvent));
    }

    [Fact]
    public void Filter_labels_are_accent_while_active_and_muted_once_toggled_off()
    {
        var shown = TimelineText.AllKinds;
        Assert.Equal(
            ["[accent][[o]] orchestrator[/]", "[accent][[a]] calls[/]", "[accent][[u]] tools[/]", "[accent][[x]] text[/]", "[accent][[e]] prompt/result[/]"],
            TimelineText.Filters.Select(f => TimelineText.FilterLabel(f, TimelineText.IsActive(f, shown))));

        var withoutTools = TimelineText.Toggle(Filter('u'), shown);

        Assert.False(TimelineText.IsActive(Filter('u'), withoutTools));
        Assert.Equal("[muted][[u]] tools[/]", TimelineText.FilterLabel(Filter('u'), false));
        Assert.Equal(50, TimelineText.Visible(Events, withoutTools, null).Length);
        Assert.DoesNotContain(TimelineText.Visible(Events, withoutTools, null), e => e.Kind == TimelineKind.Tool);
        Assert.Equal(60, TimelineText.Visible(Events, TimelineText.Toggle(Filter('u'), withoutTools), null).Length);
    }

    [Fact]
    public void E_toggles_prompts_and_results_together()
    {
        var shown = TimelineText.Toggle(Filter('e'), TimelineText.AllKinds);

        Assert.Equal([TimelineKind.Orchestrator, TimelineKind.Call, TimelineKind.Tool, TimelineKind.Text], shown.Order());
        Assert.Equal(60 - 5 - 4, TimelineText.Visible(Events, shown, null).Length);
    }

    [Fact]
    public void The_task_filter_keeps_the_group_and_the_run_events()
    {
        var gamma = TimelineText.Visible(Events, TimelineText.AllKinds, "gamma");

        Assert.All(gamma, e => Assert.Contains(e.Group, new[] { "gamma", "run" }));
        Assert.Equal(Events.Count(e => e.Group is "gamma" or "run"), gamma.Length);
        Assert.Equal(2, gamma.Count(e => e.Group == "run"));
        Assert.Equal("task: gamma", TimelineText.TaskLabel("gamma"));
        Assert.Equal("task: all", TimelineText.TaskLabel(null));
        Assert.Equal("▶ replay here", TimelineText.ReplayLabel());
    }

    [Fact]
    public void An_orchestrator_event_opens_its_log_entry()
    {
        var e = Events[0];

        Assert.Equal("Log entry", TimelineText.PopupTitle(e));
        Assert.Equal<PopupSection>(OverviewText.LogPopup(e.Entry!), TimelineText.Popup(e));
    }

    [Fact]
    public void A_call_opens_its_figures_with_a_dash_for_each_unknown_value()
    {
        var e = Event(AlphaWorker + ":call:0");

        Assert.Equal("Call 1", TimelineText.PopupTitle(e));
        var section = Assert.Single(TimelineText.Popup(e));
        Assert.Equal("Call", section.Heading);
        Assert.Equal(
            "number: 1\nmodel: claude-sonnet-4-5\nstarted: 12:00:10\ncontext: 19.2k\noutput: 640\nthinking: 210\nstop reason: tool_use\nduration: -",
            section.Text);

        Assert.Equal(
            ["number: 2", "model: -", "started: -", "context: -", "output: -", "thinking: -", "stop reason: -", "duration: -"],
            TimelineText.CallLines(new ModelCall("m", null, null, null), 1));
        Assert.Equal(
            ["number: 2", "model: gpt-5.1", "started: 12:07:40", "context: 17.0k", "output: 905", "thinking: 412", "stop reason: stop", "duration: 11s"],
            TimelineText.CallLines(Event(AlphaReview + ":call:1").Call!, 1));
    }

    [Fact]
    public void Prompt_tool_text_and_result_events_open_the_conversation_pop_ups()
    {
        var prompt = Event(AlphaWorker + ":prompt");
        var tool = Event(AlphaWorker + ":item:2");
        var text = Event(AlphaWorker + ":item:1");
        var result = Event(AlphaReview + ":result");

        AssertPopup(ConversationText.PromptEntry(prompt.Session!.Prompt), prompt);
        AssertPopup(ConversationText.ItemEntry(tool.Item!), tool);
        AssertPopup(ConversationText.ItemEntry(text.Item!), text);
        AssertPopup(ConversationText.ResultEntry(result.Result!), result);
        Assert.Equal("Edit src/Alpha/Parser.cs", TimelineText.PopupTitle(tool));
        Assert.Equal(["Input", "Result", "Diff"], TimelineText.Popup(tool).Select(s => s.Heading));
    }

    private static void AssertPopup(ConversationEntry expected, TimelineEvent e)
    {
        Assert.Equal(expected.PopupTitle, TimelineText.PopupTitle(e));
        Assert.Equal<PopupSection>(expected.Popup, TimelineText.Popup(e));
    }
}
