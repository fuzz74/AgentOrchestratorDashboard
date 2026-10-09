using System.Text.RegularExpressions;
using OrchDash.App;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Tests.Host;
using XenoAtom.Terminal;
using Xunit;
using static OrchDash.Tests.App.SubAgentRun;

namespace OrchDash.Tests.App;

// Spec 5, end to end: the temp run of SubAgentRun read by the real reader, parsers and provider stores (35-38), then
// shown in the app's own pages (39-44). The worker's foreground and background sub-agents, the planner's nested and
// background ones, their states, reports and store figures, and their child rows and paths on all six pages.
public sealed class SubAgentRunTests : IDisposable
{
    // The Usage page's group table shows its Sub column from about 172 columns on.
    private const int Width = 180;

    private readonly SubAgentRun _run = new();
    private readonly RunSnapshot _snapshot;

    public SubAgentRunTests() => _snapshot = _run.Poll();

    public void Dispose() => _run.Dispose();

    [Fact]
    public void The_run_reads_both_sessions_with_their_store_data_and_no_problems()
    {
        Assert.Equal(RunPhase.Finished, _snapshot.Run.Phase);
        Assert.Equal([PlannerKey, WorkerKey], _snapshot.Sessions.Select(s => s.Files.Key));
        Assert.All(_snapshot.Sessions, s => Assert.Empty(s.Unavailable));
        Assert.Empty(_snapshot.Problems);
    }

    [Fact]
    public void The_worker_ends_with_its_final_result_after_its_background_sub_agent_finished()
    {
        var worker = Worker();

        // 35.8: the early result was cleared by the sub-agent events after it; the final result set it again.
        Assert.Equal(Provider.Claude, worker.Provider);
        Assert.Equal(WorkerSessionId, worker.Content.SessionId);
        Assert.Equal(SessionState.Succeeded, worker.State);
        Assert.Equal(WorkerSummary, worker.Content.Result?.Worker?.Summary);
        Assert.Equal(6, worker.Content.Result?.Turns);
        Assert.Equal(WorkerModel, worker.Content.Model);
        Assert.Equal([Survey.ToolUseId, Flags.ToolUseId], worker.Content.SubAgents.Select(sub => sub.Id));

        // 35.1, 35.3: the background sub-agent finishes on its notification, at the latest event time before it.
        var flags = Sub(worker, Flags.ToolUseId);
        Assert.True(flags.Background);
        Assert.Equal(SessionState.Succeeded, flags.State);
        Assert.Equal(FlagsReport, flags.Report);
        Assert.Equal(At(70), flags.StartedAt);
        Assert.Equal(FlagsCalls[^1].Time, flags.FinishedAt);
        Assert.Equal(FlagsPrompt, flags.Prompt);
        Assert.Equal(WorkerSubModel, flags.Model);
    }

    [Fact]
    public void The_foreground_sub_agent_reports_its_summary_and_takes_its_figures_from_its_transcript()
    {
        var worker = Worker();
        var survey = Sub(worker, Survey.ToolUseId);

        // 35.3: the notification's summary, not the hand-back that follows it, is the report.
        Assert.Equal(SessionState.Succeeded, survey.State);
        Assert.Equal(SurveyReport, survey.Report);
        Assert.Equal(SurveyCalls[^1].Time, survey.FinishedAt);
        Assert.False(survey.Background);
        Assert.Null(survey.ParentId);
        Assert.Equal(Survey.ToolUseId, survey.ToolCallId);
        Assert.Equal((Survey.Description, Survey.AgentType, WorkerSubModel, SurveyPrompt),
            (survey.Name, survey.AgentType, survey.Model, survey.Prompt));
        var agentCall = Assert.Single(SubAgents.Items(worker.Content, null).OfType<ToolCall>(), call => call.ToolId == Survey.ToolUseId);
        Assert.Same(survey, SubAgents.StartedBy(worker.Content, agentCall));
        Assert.StartsWith(HandBack, agentCall.Result?.Content, StringComparison.Ordinal);

        // 35.2: the sub-agent's items carry its id; its prompt echo is left out.
        var items = SubAgents.Items(worker.Content, Survey.ToolUseId);
        Assert.Equal(["Glob", "Read"], items.OfType<ToolCall>().Select(call => call.Name));
        Assert.All(items.OfType<ToolCall>(), call => Assert.NotNull(call.Result));
        Assert.DoesNotContain(items, item => item is UserText);

        // 36.1, 36.3: the sub-agent transcript's entry, and the figures of its calls (the events give no output).
        var stored = Assert.Single(worker.Stores.SubAgents);
        Assert.Equal(Survey.ToolUseId, stored.Key);
        Assert.Equal(["You are a file search specialist."], stored.Value.SystemPrompt);
        Assert.Equal(["Glob", "Read"], stored.Value.Tools.Select(tool => tool.Name));
        Assert.Equal(SurveyCalls.Select(call => call.MessageId), stored.Value.Calls.Select(figures => figures.CallId));
        Assert.All(stored.Value.Calls, figures => Assert.Equal(Survey.ToolUseId, figures.AgentId));
        AssertFigures(SurveyCalls, SubAgents.Calls(worker.Content, Survey.ToolUseId));
        AssertFigures(WorkerCalls, SubAgents.Calls(worker.Content, null));
        Assert.Equal(WorkerCalls.Select(call => call.MessageId), worker.Stores.Calls.Select(figures => figures.CallId));

        // The background sub-agent has no transcript: its calls keep the figures of the events.
        Assert.All(SubAgents.Calls(worker.Content, Flags.ToolUseId), call => Assert.Null(call.Usage?.Output));
    }

    [Fact]
    public void The_planner_keeps_its_own_final_answer_and_nests_its_sub_agents()
    {
        var planner = Planner();

        // 35.6, 35.7: the background sub-agent's later final answer is its report, not the planner's result.
        Assert.Equal(Provider.Copilot, planner.Provider);
        Assert.Equal(PlannerSessionId, planner.Content.SessionId);
        Assert.Equal(SessionState.Succeeded, planner.State);
        Assert.Equal(PlannerAnswer, planner.Content.Result?.Text);
        Assert.Equal(3, planner.Content.Result?.Turns);
        Assert.Equal(PlannerModel, planner.Content.Model);
        Assert.Equal(PlannerPrompt, planner.Content.SentPrompt);

        // 35.4, 35.5: agent-p1's own task call started agent-p2.
        var subs = planner.Content.SubAgents;
        Assert.Equal([MapRepo, ReadSpec, SurveyTests], subs.Select(sub => sub.Id));
        Assert.Equal([null, MapRepo, null], subs.Select(sub => sub.ParentId));
        Assert.Equal([MapRepoCall, ReadSpecCall, SurveyTestsCall], subs.Select(sub => sub.ToolCallId));
        Assert.Equal(["Map the repo", "Read the spec", "Survey the tests"], subs.Select(sub => sub.Name));
        Assert.Equal([false, false, true], subs.Select(sub => sub.Background));
        Assert.All(subs, sub => Assert.Equal(SessionState.Succeeded, sub.State));
        Assert.All(subs, sub => Assert.Equal(PlannerSubModel, sub.Model));
        Assert.Equal([At(12.5), At(9.5), At(23)], subs.Select(sub => sub.FinishedAt));
        Assert.Equal([MapRepoReport, ReadSpecReport, SurveyTestsReport], subs.Select(sub => sub.Report));
        Assert.Equal([ReadSpecCall], SubAgents.Items(planner.Content, MapRepo).OfType<ToolCall>()
            .Where(call => SubAgents.StartedBy(planner.Content, call) is not null).Select(call => call.ToolId));
    }

    [Fact]
    public void The_planner_calls_take_the_usage_rows_of_their_own_agent_and_its_own_system_prompt()
    {
        var planner = Planner();

        // 36.2, 36.4: per agent, the rows by position among that agent's calls.
        foreach (var agent in new[] { null, MapRepo, ReadSpec, SurveyTests })
        {
            Assert.Equal(
                PlannerRows.Where(row => row.AgentId == agent).Select(row => ((TokenUsage?)row.Usage, (long?)row.NanoAiu)),
                SubAgents.Calls(planner.Content, agent).Select(call => (call.Usage, call.NanoAiu)));
        }

        // 36.4, 36.5: every kept row in database order; the row of the unknown agent is left out.
        Assert.Equal(
            PlannerRows.Where(row => row.AgentId != UnknownAgent).Select(row => (row.AgentId, row.ParentToolCallId, row.Usage)),
            planner.Stores.Calls.Select(figures => (figures.AgentId, figures.ParentToolCallId, figures.Usage)));

        // 36.6: the sub-agent's system.message, later in the session folder, is not the system prompt.
        Assert.Equal([PlannerSystemPrompt], planner.Stores.SystemPrompt);
        Assert.Equal(TestedVersions.CopilotCli, planner.Stores.CliVersion);
    }

    [Fact]
    public void The_progress_entry_of_a_planner_sub_agent_names_it()
    {
        // 37.1: the tag is dropped and the rest is an Activity line again.
        var tagged = Assert.Single(_snapshot.Progress, entry => entry.SubAgent is not null);
        Assert.Equal(("planner", "Map the repo", "tool: glob", ProgressKind.Activity),
            (tagged.Source, tagged.SubAgent, tagged.Message, tagged.Kind));

        // 37.2: a tag after the start of the message stays in it.
        var activity = Assert.Single(_snapshot.Progress, entry => entry.Message.StartsWith("worker: ", StringComparison.Ordinal));
        Assert.Equal($"worker: 3 tool calls, last: ↳ [{Survey.Description}] Read src/Alpha/Parser.cs", activity.Message);
    }

    [Fact]
    public void The_overview_shows_the_worker_sub_agents_under_its_task_and_the_planner_sub_agent_in_the_log()
    {
        using var host = UiTestHost.Start(AppRunner.CreatePages(), _snapshot, Width);
        var overview = host.Frame();

        // 39.1: the child rows start one column right of the task id, with the state icon, role, tool calls and span.
        Assert.Contains("│→ ✔  alpha  W1 ", overview, StringComparison.Ordinal);
        Assert.Contains("│      ├✔ Survey the parser module · worker #1 · 2 tool calls · 7s ", overview, StringComparison.Ordinal);
        Assert.Contains("│      └✔ Survey CLI flags · worker #1 · 1 tool call · 25s ", overview, StringComparison.Ordinal);
        // 39.3: the log line's path.
        Assert.Contains($"{Clock(6)} [planner › Map the repo] tool: glob ", overview, StringComparison.Ordinal);
        host.SaveSvg("app-subagents-overview");

        // 39.1: Enter on a child row opens that sub-agent on the Conversation page.
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Enter);
        var conversation = host.Frame();
        Assert.Contains("│→ sub-agent alpha worker #1 › Survey the parser module ", conversation, StringComparison.Ordinal);
        Assert.Contains($"result succeeded {SurveyReport} ", conversation, StringComparison.Ordinal);
    }

    [Fact]
    public void The_graph_shows_the_worker_sub_agents_under_the_card_and_a_click_opens_one()
    {
        using var host = UiTestHost.Start(AppRunner.CreatePages(), _snapshot, Width);
        host.Type('5');
        var graph = host.Frame();

        // 40.1, 40.2: the child lines right below the card; a name over 20 characters is cut to 19 and "…".
        Assert.Matches(@"│●✔ alpha +│\n│  ├✔ Survey the parser m… +│\n│  └✔ Survey CLI flags +│\n", graph);
        // 40.4
        Assert.Contains("│sessions: 1 · sub-agents: 2 (0 running) ", graph, StringComparison.Ordinal);
        host.SaveSvg("app-subagents-graph");

        // 40.3
        host.ClickText("├✔ Survey the parser m…");
        Assert.Contains("│→ sub-agent alpha worker #1 › Survey the parser module ", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_conversation_shows_the_child_rows_the_start_entries_and_a_sub_agent_alone()
    {
        using var host = UiTestHost.Start(AppRunner.CreatePages(), _snapshot, Width);
        host.Type('2');
        var planner = host.Frame();

        // 41.1: child rows under each session; the list wraps rows wider than the pane, so only their starts are read.
        Assert.Contains("│→ ✔ planner planner #1 gpt-6-sol 26s 2 tool calls ", planner, StringComparison.Ordinal);
        Assert.Contains("│    ├✔ Map the repo · explore · gpt-5.6-luna · 9s · 2 tool", planner, StringComparison.Ordinal);
        Assert.Contains("│    │ └✔ Read the spec · explore · gpt-5.6-luna · 2s · 0 ", planner, StringComparison.Ordinal);
        Assert.Contains("│    └✔ Survey the tests · explore · gpt-5.6-luna · 7s · 1 ", planner, StringComparison.Ordinal);
        Assert.Contains("│  ✔ alpha worker #1 claude-opus-5-5 50s 2 tool calls ", planner, StringComparison.Ordinal);
        Assert.Contains("│    ├✔ Survey the parser module · Explore ·", planner, StringComparison.Ordinal);
        Assert.Contains("│    └✔ Survey CLI flags · Explore · claude-haiku-4-5 · 25s", planner, StringComparison.Ordinal);
        // 41.2, 41.3: the planner's own entries, with a start entry and the report's first line for each sub-agent.
        AssertLines(planner, "✔ sub-agent Map the repo · explore · succeeded", MapRepoReport);
        AssertLines(planner, "✔ sub-agent Survey the tests · explore · succeeded", SurveyTestsReport);
        Assert.Contains($"│  call 3 · {Clock(19)} · context 12.9k ", planner, StringComparison.Ordinal);
        Assert.Contains($"│  result success {PlannerAnswer} ", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("glob **/*", planner, StringComparison.Ordinal);

        // 41.4: the nested sub-agent alone, with its path.
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Down);
        var readSpec = host.Frame();
        Assert.Contains("│→ sub-agent planner #1 › Map the repo › Read the spec ", readSpec, StringComparison.Ordinal);
        Assert.Contains($"│  explore · gpt-5.6-luna · ✔ succeeded · started {Clock(7)} · 2s · 0 tool calls ", readSpec, StringComparison.Ordinal);
        Assert.Contains("│  prompt 19 chars Summarize the spec. ", readSpec, StringComparison.Ordinal);
        Assert.Contains($"│  result succeeded {ReadSpecReport} ", readSpec, StringComparison.Ordinal);

        // 41.3: the worker's start entries show the notifications' summaries, not the hand-backs.
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Down);
        var worker = host.Frame();
        AssertLines(worker, "✔ sub-agent Survey the parser module · Explore · succeeded", SurveyReport);
        AssertLines(worker, "✔ sub-agent Survey CLI flags · Explore · succeeded", FlagsReport);
        Assert.DoesNotContain("[Subagent hand-back]", worker, StringComparison.Ordinal);
        // 35.8: the final result, after the background sub-agent's notification.
        Assert.Contains($"│  result success done {WorkerSummary} ", worker, StringComparison.Ordinal);

        // 41.4: the foreground sub-agent alone: header, prompt, its tool calls and its result.
        host.Press(TerminalKey.Down);
        var survey = host.Frame();
        Assert.Contains("│→ sub-agent alpha worker #1 › Survey the parser module ", survey, StringComparison.Ordinal);
        Assert.Contains($"│  Explore · claude-haiku-4-5 · ✔ succeeded · started {Clock(60)} · 7s · 2 tool calls ", survey, StringComparison.Ordinal);
        Assert.Contains($"│  prompt 28 chars {SurveyPrompt} ", survey, StringComparison.Ordinal);
        Assert.Contains($"│  call 1 · {Clock(62)} · context 4.0k ", survey, StringComparison.Ordinal);
        Assert.Contains("│  Glob src/Alpha/** ok 1s ", survey, StringComparison.Ordinal);
        Assert.Contains("│  Read src/Alpha/Parser.cs ok 1s ", survey, StringComparison.Ordinal);
        Assert.Contains($"│  result succeeded {SurveyReport} ", survey, StringComparison.Ordinal);
        host.SaveSvg("app-subagents-conversation");
    }

    [Fact]
    public void The_context_page_shows_the_child_rows_and_a_sub_agent_alone()
    {
        using var host = UiTestHost.Start(AppRunner.CreatePages(), _snapshot, Width);
        host.Type('3');
        var planner = host.Frame();

        // 42.1: the child rows with their latest context.
        Assert.Matches(@"│    ├✔ Map the repo +7\.0k +│", planner);
        Assert.Matches(@"│    │ └✔ Read the spec +5\.5k +│", planner);
        Assert.Matches(@"│    └✔ Survey the tests +6\.2k +│", planner);
        Assert.Matches(@"│    ├✔ Survey the parser module +5\.1k +│", planner);
        Assert.Matches(@"│    └✔ Survey CLI flags +4\.6k +│", planner);
        // 42.2: the planner's own calls only.
        Assert.Contains("┊Copilot · planner · gpt-6-sol · 3 calls · peak 12.9k · context 12.9k\n", planner, StringComparison.Ordinal);

        // 42.3: the foreground sub-agent's own calls, with its make-up from its transcript.
        for (var i = 0; i < 5; i++)
            host.Press(TerminalKey.Down);
        var survey = host.Frame();
        Assert.Contains("┊Claude · sub-agent · alpha worker #1 › Survey the parser module · claude-haiku-4-5 · 3 calls · peak 5.1k · context 5.1k\n",
            survey, StringComparison.Ordinal);
        Assert.Matches($@"│  call 1  {Clock(62)} +4\.0k ", survey);
        Assert.Matches($@"│→ call 3  {Clock(67)} +5\.1k ", survey);
        host.SaveSvg("app-subagents-context");

        host.Type('s');
        Assert.Contains("┌ System prompt ", host.Frame(), StringComparison.Ordinal);
        Assert.Contains("You are a file search specialist.", host.Frame(), StringComparison.Ordinal);
        host.Press(TerminalKey.Escape);
        host.Type('t');
        Assert.Contains("┌ Tool definitions ", host.Frame(), StringComparison.Ordinal);
        Assert.Contains("Glob", host.Frame(), StringComparison.Ordinal);
        host.Press(TerminalKey.Escape);
    }

    [Fact]
    public void The_usage_page_shows_the_child_rows_with_their_figures_and_the_sub_agent_shares()
    {
        using var host = UiTestHost.Start(AppRunner.CreatePages(), _snapshot, Width);
        host.Type('4');
        var planner = host.Frame();

        // 43.2, 43.4: the run panel's count, the groups' Sub column and the chart's sub-agent bars.
        Assert.Contains("│Cache read: 138.0k      Cost: 0.42 USD          sub-agents 5 ", planner, StringComparison.Ordinal);
        AssertRow(planner, "│→ planner", "1", "8", "4.5k", "34.4k", "30.1k", "845", "-", "1", "4.20 AIU", "+0 -0", "3 · 46 %");
        AssertRow(planner, "│  alpha", "1", "9", "30", "103.6k", "15.2k", "900", "0.42 USD", "-", "-", "-", "2 · 19 %");
        Assert.Equal(2, Regex.Count(planner, "│└ sub-agents ██"));
        // 43.1, 43.2: the planner's row has its own peak; each child row the database figures of its calls.
        AssertRow(planner, "│→ ✔  planner", "#1", "gpt-6-sol", "8", "12.9k", "4.5k", "34.4k", "30.1k", "845", "-", "4.20 AIU", "+0 -0");
        AssertRow(planner, "│     ├✔ Map the repo", "gpt-5.6-luna", "2", "7.0k", "1.1k", "6.0k", "6.7k", "170", "-", "0.60 AIU", "-");
        AssertRow(planner, "│     │ └✔ Read the spec", "gpt-5.6-luna", "1", "5.5k", "500", "0", "5.0k", "60", "-", "0.30 AIU", "-");
        AssertRow(planner, "│     └✔ Survey the tests", "gpt-5.6-luna", "2", "6.2k", "950", "5.5k", "5.9k", "155", "-", "0.60 AIU", "-");
        host.SaveSvg("app-subagents-usage");

        // The worker's group: the foreground sub-agent with its transcript's output, the background one without.
        host.Press(TerminalKey.Down);
        var worker = host.Frame();
        AssertRow(worker, "✔  worker", "#1", "claude-opus-5-5", "9", "25.5k", "30", "103.6k", "15.2k");
        AssertRow(worker, "│     ├✔ Survey the parser module", "claude-haiku-4-5", "3", "5.1k", "9", "8.3k", "5.1k", "190", "-", "-", "-");
        AssertRow(worker, "│     └✔ Survey CLI flags", "claude-haiku-4-5", "2", "4.6k", "6", "4.1k", "4.6k", "-", "-", "-", "-");

        // 43.3, 43.5: Enter moves to the session table; Enter on a child row opens its pop-up.
        host.Press(TerminalKey.Enter);
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Enter);
        var popup = host.Frame();
        Assert.Contains("┌ Usage: alpha worker #1 › Survey the parser module ", popup, StringComparison.Ordinal);
        Assert.Contains("│calls: 3 ", popup, StringComparison.Ordinal);
        Assert.Contains("│output: 190 ", popup, StringComparison.Ordinal);
        Assert.Contains($"│call 1 · {Clock(62)} · claude-haiku-4-5 · input 3 · cache read 0 · cache write 4.0k · output 40 ", popup, StringComparison.Ordinal);
        host.Press(TerminalKey.Escape);
    }

    [Fact]
    public void The_timeline_shows_the_sub_agent_paths_their_starts_and_their_results()
    {
        using var host = UiTestHost.Start(AppRunner.CreatePages(), _snapshot, Width);
        host.Type('8');
        var latest = host.Frame();

        // 44.4
        Assert.Contains("[e] prompt/result  [s] sub-agents  task: all", latest, StringComparison.Ordinal);
        // 44.1, 44.2: the worker's sub-agents by path; calls numbered within their agent; one result per agent.
        AssertRow(latest, $"│  {Clock(60)}", "alpha worker #1 › Survey the parser module", "prompt", "sub-agent started · Explore · 28 chars");
        AssertRow(latest, $"│  {Clock(67)}", "alpha worker #1 › Survey the parser module", "call", "call 3 · claude-haiku-4-5 · context 5.1k");
        AssertRow(latest, $"│  {Clock(67)}", "alpha worker #1 › Survey the parser module", "result", $"result succeeded · {SurveyReport}");
        AssertRow(latest, $"│  {Clock(95)}", "alpha worker #1 › Survey CLI flags", "result", $"result succeeded · {FlagsReport}");
        AssertRow(latest, $"│  {Clock(105)}", "alpha worker #1", "call", "call 4 · claude-opus-5-5 · context 25.5k");
        AssertRow(latest, $"│  {Clock(105)}", "alpha worker #1", "result", "result success · done");
        Assert.Single(Regex.Matches(latest, @"alpha worker #1 +result "));
        host.SaveSvg("app-subagents-timeline");

        // The planner's nested sub-agent.
        host.Press(TerminalKey.Home);
        var first = host.Frame();
        AssertRow(first, $"│  {Clock(7)}", "planner #1 › Map the repo › Read the spec", "prompt", "sub-agent started · explore · 19 chars");
        AssertRow(first, $"│  {Clock(9.5)}", "planner #1 › Map the repo › Read the spec", "result", $"result succeeded · {ReadSpecReport}");
        AssertRow(first, $"│  {Clock(11)}", "planner #1 › Map the repo", "call", "call 2 · gpt-5.6-luna · context 7.0k");

        // 44.4: s hides every sub-agent event.
        host.Type('s');
        var hidden = host.Frame();
        Assert.DoesNotContain(AgentPath.Separator, hidden, StringComparison.Ordinal);
        AssertRow(hidden, $"│→ {Clock(0)}", "orchestrator", "orch", "Planning from .orchestrator/spec.md");
    }

    private Session Worker() => Assert.Single(_snapshot.Sessions, s => s.Files.Key == WorkerKey);

    private Session Planner() => Assert.Single(_snapshot.Sessions, s => s.Files.Key == PlannerKey);

    private static SubAgent Sub(Session session, string id) => Assert.Single(session.Content.SubAgents, sub => sub.Id == id);

    /// <summary>The calls have the usage and stop reason of their transcript lines, the output included.</summary>
    private static void AssertFigures(ClaudeCall[] expected, IEnumerable<ModelCall> calls) =>
        Assert.Equal(expected.Select(call => (call.MessageId, (TokenUsage?)call.Usage, (string?)call.StopReason)),
            calls.Select(call => (call.Id, call.Usage, call.StopReason)));

    /// <summary>The local "HH:mm:ss" of the time <paramref name="seconds"/> after the run's start.</summary>
    private static string Clock(double seconds) => Look.Clock(At(seconds));

    /// <summary>A screen row with the cells in this order, each after the one before it and at least one space.</summary>
    private static void AssertRow(string frame, params string[] cells) =>
        Assert.Matches(string.Join(" +", cells.Select(Regex.Escape)) + " ", frame);

    /// <summary>Two entry lines, the second right below the first and starting in the same column.</summary>
    private static void AssertLines(string frame, string first, string second)
    {
        var lines = frame.Split('\n');
        var at = Array.FindIndex(lines, line => line.Contains("│  " + first + " ", StringComparison.Ordinal));
        Assert.True(at >= 0 && at + 1 < lines.Length, $"no entry line '{first}' with a line below it");
        Assert.Equal(lines[at].IndexOf("│  " + first, StringComparison.Ordinal),
            lines[at + 1].IndexOf("│  " + second + " ", StringComparison.Ordinal));
    }
}
