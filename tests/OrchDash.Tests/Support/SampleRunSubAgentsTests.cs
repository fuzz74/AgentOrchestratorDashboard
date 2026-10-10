using System.Text.Json;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.Support;

// The sample data of spec 4.4, which the UI tests rely on.
public sealed class SampleRunSubAgentsTests
{
    private const SessionState Running = SessionState.Running;
    private const SessionState Succeeded = SessionState.Succeeded;
    private const SessionState Failed = SessionState.Failed;

    private readonly RunSnapshot _enriched = SampleRun.CreateEnriched();
    private readonly RunSnapshot _run = SampleRun.CreateSubAgents();

    private Session Planner => _run.Sessions[0];
    private Session AlphaWorker => _run.Sessions[1];
    private Session BetaWorker => _run.Sessions[4];

    private IEnumerable<Session> WithSubAgents => _run.Sessions.Where(s => !s.Content.SubAgents.IsEmpty);

    private Session Enriched(string key) => _enriched.Sessions.Single(s => s.Files.Key == key);

    [Fact]
    public void Every_call_returns_a_new_snapshot_with_equal_content()
    {
        var other = SampleRun.CreateSubAgents();

        Assert.NotSame(_run, other);
        Assert.NotSame(Planner, other.Sessions[0]);
        Assert.NotSame(AlphaWorker.Stores, other.Sessions[1].Stores);
        Assert.Equivalent(_run, other, strict: true);
    }

    [Fact]
    public void Sessions_are_the_planner_then_those_of_CreateEnriched_by_start()
    {
        Assert.Equal("planner-20261003-115900-1.json", SampleRun.PlannerKey);
        Assert.Equal(
        [
            (SampleRun.PlannerKey, Provider.Copilot, Succeeded),
            (SampleRun.AlphaWorkerKey, Provider.Claude, Succeeded),
            (SampleRun.GammaWorker1Key, Provider.Claude, Failed),
            (SampleRun.AlphaReviewKey, Provider.Copilot, Succeeded),
            (SampleRun.BetaWorkerKey, Provider.Claude, Running),
            (SampleRun.GammaWorker2Key, Provider.Claude, Failed),
        ],
        _run.Sessions.Select(s => (s.Files.Key, s.Provider, s.State)));

        var ordered = _run.Sessions
            .OrderBy(s => s.StartedAt.HasValue ? 0 : 1)
            .ThenBy(s => s.StartedAt)
            .ThenBy(s => s.Files.Key, StringComparer.Ordinal);
        Assert.Equal(ordered, _run.Sessions);
    }

    [Fact]
    public void Run_fields_are_those_of_CreateEnriched()
    {
        Assert.Equal(_enriched.Version, _run.Version);
        Assert.Equal(_enriched.ReadAt, _run.ReadAt);
        Assert.Equal(_enriched.RepoPath, _run.RepoPath);
        Assert.Equal(_enriched.Run, _run.Run);
        Assert.Equivalent(_enriched.Plan, _run.Plan, strict: true);
        Assert.Equivalent(_enriched.Tasks, _run.Tasks, strict: true);
        Assert.Equal(_enriched.Problems, _run.Problems);
    }

    [Fact]
    public void The_planner_is_a_succeeded_Copilot_session_without_a_task()
    {
        var files = Planner.Files;
        Assert.Null(files.TaskId);
        Assert.Equal(AgentRole.Planner, files.Role);
        Assert.Null(files.StartFolder);
        Assert.Equal(1, files.Attempt);
        Assert.Equal(0, files.ReviewTry);
        Assert.False(files.IsNudge);
        var resultPath = SampleRun.LogsDir + SampleRun.PlannerKey;
        Assert.Equal(resultPath, files.ResultPath);
        Assert.Equal(resultPath + ".prompt.md", files.PromptPath);
        Assert.Equal(resultPath + ".events.jsonl", files.EventsPath);
        Assert.Equal(resultPath + ".stderr", files.StderrPath);
        Assert.True(files.HasResultFile);
        Assert.True(files.HasEventsFile);
        Assert.True(files.PromptWrittenAt < Planner.StartedAt);

        Assert.Equal(SampleRun.At(11, 59, 0), Planner.StartedAt);
        Assert.NotEmpty(Planner.Prompt);
        Assert.Empty(Planner.Unavailable);

        var content = Planner.Content;
        Assert.False(string.IsNullOrEmpty(content.SessionId));
        Assert.Equal("gpt-5.6-luna", content.Model);
        Assert.Equal(Planner.StartedAt, content.FirstEventAt);
        var own = SubAgents.Calls(content, null);
        Assert.Equal(2, own.Length);
        Assert.All(own, c =>
        {
            Assert.NotNull(c.Usage);
            Assert.NotNull(c.NanoAiu);
            Assert.NotNull(c.Duration);
        });

        var result = content.Result;
        Assert.NotNull(result);
        Assert.False(result.IsError);
        Assert.Equal(own.Length, result.Turns);   // the agent's own calls only (35.7)
        Assert.Equal(((AssistantText)SubAgents.Items(content, null)[^1]).Text, result.Text);
    }

    [Fact]
    public void Every_sub_agent_has_its_id_parent_name_state_type_model_and_background()
    {
        Assert.Equal(
        [
            (SampleRun.PlannerKey, "agent-p1", null, "call-p1", "Map the repo", Succeeded, "explore", "gpt-5.6-luna", false),
            (SampleRun.PlannerKey, "agent-p2", "agent-p1", "call-p2", "Read the spec", Succeeded, "explore", "gpt-5.6-luna", false),
            (SampleRun.PlannerKey, "agent-p3", null, "call-p3", "Survey the tests", Succeeded, "explore", "gpt-5.6-luna", false),
            (SampleRun.AlphaWorkerKey, "toolu_alpha_sub1", null, "toolu_alpha_sub1", "Survey the parser module", Succeeded,
                "Explore", "claude-haiku-4-5", false),
            (SampleRun.AlphaWorkerKey, "toolu_alpha_sub2", null, "toolu_alpha_sub2", "Check the public API surface of…", Failed,
                "general-purpose", "claude-sonnet-4-5", false),
            (SampleRun.BetaWorkerKey, "toolu_beta_sub1", null, "toolu_beta_sub1", "Survey CLI flags", Running,
                "Explore", "claude-haiku-4-5", true),
        ],
        _run.Sessions.SelectMany(s => s.Content.SubAgents.Select(sub =>
            (s.Files.Key, sub.Id, sub.ParentId, sub.ToolCallId, sub.Name, sub.State, sub.AgentType, sub.Model, sub.Background))));

        Assert.Equal(
            [SampleRun.PlannerSub1Id, SampleRun.PlannerSub2Id, SampleRun.PlannerSub3Id, SampleRun.AlphaSub1Id, SampleRun.AlphaSub2Id, SampleRun.BetaSub1Id],
            WithSubAgents.SelectMany(s => s.Content.SubAgents.Select(sub => sub.Id)));
    }

    [Fact]
    public void Every_name_is_the_cut_description()
    {
        Assert.All(WithSubAgents.SelectMany(s => s.Content.SubAgents), sub => Assert.Equal(SubAgents.Name(sub.Description), sub.Name));
        Assert.Equal("Check the public API surface of the alpha parser",
            SubAgents.Find(AlphaWorker.Content, SampleRun.AlphaSub2Id)!.Description);
    }

    [Fact]
    public void Every_sub_agent_has_a_prompt_and_a_report_once_finished()
    {
        Assert.All(WithSubAgents, session => Assert.All(session.Content.SubAgents, sub =>
        {
            Assert.NotEmpty(sub.Prompt);
            Assert.Equal(sub.State, SubAgents.StateOf(session, sub));
            if (sub.State == Running)
            {
                Assert.Null(sub.FinishedAt);
                Assert.Null(sub.Report);
            }
            else
            {
                Assert.NotNull(sub.FinishedAt);
                Assert.False(string.IsNullOrEmpty(sub.Report));
            }
        }));
        Assert.Equal("The parser module has 3 files.", SubAgents.Find(AlphaWorker.Content, SampleRun.AlphaSub1Id)!.Report);
    }

    [Fact]
    public void Tree_rows_of_each_session_give_the_prefixes()
    {
        Assert.Equal(["├", "└"], AgentTree.Rows([AlphaWorker]).Select(r => r.Prefix));
        Assert.Equal(["└"], AgentTree.Rows([BetaWorker]).Select(r => r.Prefix));
        Assert.Equal(["├", "│ └", "└"], AgentTree.Rows([Planner]).Select(r => r.Prefix));
        Assert.Equal(
            [SampleRun.PlannerSub1Id, SampleRun.PlannerSub2Id, SampleRun.PlannerSub3Id],
            AgentTree.Rows([Planner]).Select(r => r.SubAgent.Id));
        Assert.All(_run.Sessions.Except([Planner, AlphaWorker, BetaWorker]), s => Assert.Empty(AgentTree.Rows([s])));
    }

    [Fact]
    public void Items_and_calls_split_by_agent()
    {
        Assert.Equal(
        [
            (SampleRun.PlannerKey, null, 4, 2, 2),
            (SampleRun.PlannerKey, "agent-p1", 3, 2, 1),
            (SampleRun.PlannerKey, "agent-p2", 2, 1, 1),
            (SampleRun.PlannerKey, "agent-p3", 2, 1, 1),
            (SampleRun.AlphaWorkerKey, null, 8, 4, 2),
            (SampleRun.AlphaWorkerKey, "toolu_alpha_sub1", 3, 2, 2),
            (SampleRun.AlphaWorkerKey, "toolu_alpha_sub2", 1, 1, 1),
            (SampleRun.BetaWorkerKey, null, 7, 4, 2),
            (SampleRun.BetaWorkerKey, "toolu_beta_sub1", 1, 1, 1),
        ],
        WithSubAgents.SelectMany(s => new string?[] { null }.Concat(s.Content.SubAgents.Select(sub => sub.Id)).Select(id =>
        {
            var items = SubAgents.Items(s.Content, id);
            return (s.Files.Key, id, items.Length, items.OfType<ToolCall>().Count(), SubAgents.Calls(s.Content, id).Length);
        })));
    }

    [Fact]
    public void Every_entry_belongs_to_the_agent_or_a_sub_agent_of_its_session()
    {
        Assert.All(WithSubAgents, session =>
        {
            var content = session.Content;
            var ids = content.SubAgents.Select(sub => sub.Id).Prepend(null).ToList();
            Assert.All(content.Items, i => Assert.Contains(i.AgentId, ids));
            Assert.All(content.Calls, c => Assert.Contains(c.AgentId, ids));
            Assert.Equal(content.Items.Length, ids.Sum(id => SubAgents.Items(content, id).Length));
            Assert.Equal(content.Calls.Length, ids.Sum(id => SubAgents.Calls(content, id).Length));
        });
    }

    [Fact]
    public void Every_item_names_a_call_of_its_own_agent()
    {
        // Copilot sub-agents number their turns from "0" again (35.4), so a CallId is only unique within its agent.
        Assert.Equal(["0", "0", "0", "0", "1"], Planner.Content.Calls.Select(c => c.Id));
        Assert.All(WithSubAgents, session =>
        {
            var content = session.Content;
            Assert.All(content.Items, i => Assert.Contains(SubAgents.Calls(content, i.AgentId), c => c.Id == i.CallId));
        });
    }

    [Fact]
    public void Every_sub_agent_call_has_usage_and_the_sub_agents_model()
    {
        Assert.All(WithSubAgents, session => Assert.All(session.Content.SubAgents, sub =>
            Assert.All(SubAgents.Calls(session.Content, sub.Id), c =>
            {
                Assert.NotNull(c.StartedAt);
                Assert.NotNull(c.Usage);
                Assert.Equal(sub.Model, c.Model);
            })));

        Assert.All(Planner.Content.SubAgents, sub =>
        {
            var call = Assert.Single(SubAgents.Calls(Planner.Content, sub.Id));
            Assert.NotNull(call.Usage!.Output);
            Assert.NotNull(call.NanoAiu);
            Assert.NotNull(call.Duration);
        });
        Assert.All(SubAgents.Calls(AlphaWorker.Content, SampleRun.AlphaSub1Id), c => Assert.NotNull(c.Usage!.Output));
    }

    [Fact]
    public void Every_sub_agent_tool_call_has_a_result_but_the_running_ones()
    {
        Assert.All(WithSubAgents, session => Assert.All(session.Content.SubAgents.Where(sub => sub.State != Running), sub =>
            Assert.All(SubAgents.Items(session.Content, sub.Id).OfType<ToolCall>(), t => Assert.NotNull(t.Result))));

        Assert.Equal(["Glob", "Read"], SubAgents.Items(AlphaWorker.Content, SampleRun.AlphaSub1Id).OfType<ToolCall>().Select(t => t.Name));
        var read = Assert.IsType<ToolCall>(Assert.Single(SubAgents.Items(BetaWorker.Content, SampleRun.BetaSub1Id)));
        Assert.Equal("Read", read.Name);
        Assert.Null(read.Result);
    }

    [Fact]
    public void Each_sub_agent_is_started_by_one_tool_call_of_its_parent()
    {
        Assert.All(WithSubAgents, session =>
        {
            var content = session.Content;
            Assert.All(content.SubAgents, sub =>
            {
                var start = Assert.Single(content.Items.OfType<ToolCall>(), t => SubAgents.StartedBy(content, t) == sub);
                Assert.Equal(sub.ToolCallId, start.ToolId);
                Assert.Equal(sub.ParentId, start.AgentId);
                Assert.Equal(session.Provider == Provider.Claude ? "Agent" : "task", start.Name);
                using var input = JsonDocument.Parse(start.InputJson);
                Assert.Equal(sub.Description, input.RootElement.GetProperty("description").GetString());
                Assert.Equal(sub.Prompt, input.RootElement.GetProperty("prompt").GetString());
                Assert.True(start.Time <= sub.StartedAt);
                if (sub.FinishedAt is { } finished)
                {
                    Assert.NotNull(start.Result);
                    Assert.Equal(sub.State == Failed, start.Result.IsError);
                    Assert.Equal(sub.Report, start.Result.Content);
                    Assert.True(start.Result.Time >= finished);
                }
                else
                {
                    Assert.Null(start.Result);
                }
            });
        });
        Assert.Equal("agent-p1", SubAgents.Items(Planner.Content, SampleRun.PlannerSub1Id).OfType<ToolCall>().First().AgentId);
    }

    [Fact]
    public void Sub_agent_times_lie_within_their_session_and_their_parent()
    {
        Assert.All(WithSubAgents, session =>
        {
            var content = session.Content;
            var end = content.LastEventAt!.Value;
            Assert.All(content.SubAgents, sub =>
            {
                var start = sub.StartedAt!.Value;
                var finish = sub.FinishedAt ?? end;
                Assert.InRange(start, session.StartedAt!.Value, finish);
                Assert.True(finish <= end);
                Assert.All(Times(content, sub.Id), t => Assert.InRange(t, start, finish));
                if (SubAgents.Find(content, sub.ParentId) is { } parent)
                {
                    Assert.True(parent.StartedAt < start);
                    Assert.True(finish < (parent.FinishedAt ?? end));
                }
            });
        });
    }

    [Fact]
    public void The_planner_stream_runs_p1_then_p2_then_p1_again_then_p3()
    {
        var content = Planner.Content;

        Assert.Equal(["agent-p1", "agent-p2", "agent-p1", "agent-p3"], Stretches(content.Items.Select(i => i.AgentId)));
        Assert.Equal(["agent-p1", "agent-p2", "agent-p3"], Stretches(content.Calls.Select(c => c.AgentId)));
        Assert.Equal(content.Items.OrderBy(i => i.Time), content.Items);
        Assert.Equal(content.Calls.OrderBy(c => c.StartedAt), content.Calls);
    }

    [Fact]
    public void The_planner_stores_hold_a_usage_row_per_call_with_its_agent()
    {
        var stores = Planner.Stores;
        var calls = Planner.Content.Calls;

        Assert.Equal(TestedVersions.CopilotCli, stores.CliVersion);
        Assert.Equal(
            calls.Select(c => (c.StartedAt, c.Usage!, c.ThinkingTokens, c.NanoAiu, c.Duration, c.StopReason)),
            stores.Calls.Select(f => (f.Time, f.Usage, f.ThinkingTokens, f.NanoAiu, f.Duration, f.StopReason)));
        Assert.Equal(
        [
            (null, null),
            ("agent-p1", "call-p1"),
            ("agent-p2", "call-p2"),
            ("agent-p3", "call-p3"),
            (null, null),
        ],
        stores.Calls.Select(f => (f.AgentId, f.ParentToolCallId)));
        Assert.Same(StoreData.NoSubAgents, stores.SubAgents);
    }

    [Fact]
    public void The_alpha_stores_hold_the_first_sub_agents_transcript()
    {
        var stores = AlphaWorker.Stores;
        var (id, sub) = Assert.Single(stores.SubAgents);

        Assert.Equal(SampleRun.AlphaSub1Id, id);
        Assert.Same(StringComparer.Ordinal, stores.SubAgents.KeyComparer);
        Assert.Equal(
            SubAgents.Calls(AlphaWorker.Content, SampleRun.AlphaSub1Id)
                .Select(c => new CallFigures(c.Id, c.StartedAt, c.Usage!, c.ThinkingTokens, null, null, c.StopReason)),
            sub.Calls);
        Assert.Equal(2, sub.Calls.Length);
        Assert.All(sub.Calls, f => Assert.NotNull(f.Usage.Output));
        Assert.InRange(sub.SystemPrompt.Length, 1, 2);
        Assert.All(sub.SystemPrompt, b => Assert.NotEmpty(b));
        Assert.Equal(["Glob", "Read"], sub.Tools.Select(t => t.Name));
        Assert.All(sub.Tools, t =>
        {
            Assert.False(string.IsNullOrEmpty(t.Description));
            using var schema = JsonDocument.Parse(t.SchemaJson!);
            Assert.Equal(JsonValueKind.Object, schema.RootElement.ValueKind);
        });
        Assert.Same(StoreData.NoSubAgents, sub.SubAgents);

        Assert.Equivalent(Enriched(SampleRun.AlphaWorkerKey).Stores, stores with { SubAgents = StoreData.NoSubAgents }, strict: true);
    }

    [Fact]
    public void No_other_stores_have_sub_agents()
    {
        Assert.All(_run.Sessions.Where(s => s.Files.Key != SampleRun.AlphaWorkerKey),
            s => Assert.Same(StoreData.NoSubAgents, s.Stores.SubAgents));
    }

    [Fact]
    public void The_beta_sub_agent_runs_in_the_background()
    {
        var sub = Assert.Single(BetaWorker.Content.SubAgents);

        Assert.True(sub.Background);
        Assert.Equal(Running, SubAgents.StateOf(BetaWorker, sub));
        Assert.Null(sub.FinishedAt);
        Assert.Null(sub.Report);
        Assert.Equal(["no transcript"], BetaWorker.Unavailable);
        Assert.Same(StoreData.Empty, BetaWorker.Stores);
        Assert.Null(BetaWorker.Content.Result);
    }

    [Fact]
    public void The_alpha_and_beta_workers_are_those_of_CreateEnriched_with_the_sub_agents_added()
    {
        Assert.All(new[] { AlphaWorker, BetaWorker }, session =>
        {
            var enriched = Enriched(session.Files.Key);
            var content = session.Content;

            Assert.Equal(enriched.Files, session.Files);
            Assert.Equal(enriched.Provider, session.Provider);
            Assert.Equal(enriched.State, session.State);
            Assert.Equal(enriched.Prompt, session.Prompt);
            Assert.Equal(enriched.StartedAt, session.StartedAt);
            Assert.Equal(enriched.Unavailable, session.Unavailable);
            Assert.Equal(enriched.Content.Calls, SubAgents.Calls(content, null));
            Assert.Equal(
                enriched.Content.Items,
                SubAgents.Items(content, null).Where(i => i is not ToolCall t || SubAgents.StartedBy(content, t) is null));
            Assert.Equivalent(
                enriched.Content with { Calls = [], Items = [] },
                content with { Calls = [], Items = [], SubAgents = [] },
                strict: true);
        });
    }

    [Fact]
    public void The_other_sessions_are_those_of_CreateEnriched()
    {
        Assert.All(new[] { SampleRun.GammaWorker1Key, SampleRun.AlphaReviewKey, SampleRun.GammaWorker2Key }, key =>
            Assert.Equivalent(Enriched(key), _run.Sessions.Single(s => s.Files.Key == key), strict: true));
    }

    [Fact]
    public void The_first_progress_entry_is_a_planner_sub_agent_activity()
    {
        var first = _run.Progress[0];

        Assert.Equal(
            new ProgressEntry(SampleRun.At(11, 59, 30), "planner", "Glob **/*", ProgressKind.Activity) { SubAgent = "Map the repo" },
            first);
        Assert.Equal(SubAgents.Find(Planner.Content, SampleRun.PlannerSub1Id)!.Name, first.SubAgent);
        Assert.Equal(_run.Progress.Min(p => p.Time), first.Time);
        Assert.Equal(_enriched.Progress, _run.Progress.Skip(1));
    }

    [Fact]
    public void CreateEnriched_keeps_its_five_sessions_without_sub_agents()
    {
        var enriched = SampleRun.CreateEnriched();

        Assert.Equal(
            [SampleRun.AlphaWorkerKey, SampleRun.GammaWorker1Key, SampleRun.AlphaReviewKey, SampleRun.BetaWorkerKey, SampleRun.GammaWorker2Key],
            enriched.Sessions.Select(s => s.Files.Key));
        Assert.All(enriched.Sessions, s =>
        {
            Assert.Empty(s.Content.SubAgents);
            Assert.All(s.Content.Items, i => Assert.Null(i.AgentId));
            Assert.All(s.Content.Calls, c => Assert.Null(c.AgentId));
            Assert.Same(StoreData.NoSubAgents, s.Stores.SubAgents);
        });
        Assert.All(enriched.Progress, p => Assert.Null(p.SubAgent));
    }

    // The times of the agent's calls, items and tool results.
    private static IEnumerable<DateTimeOffset> Times(SessionContent content, string agentId)
    {
        var times = SubAgents.Calls(content, agentId).Select(c => c.StartedAt)
            .Concat(SubAgents.Items(content, agentId).Select(i => i.Time))
            .Concat(SubAgents.Items(content, agentId).OfType<ToolCall>().Select(t => t.Result?.Time ?? t.Time))
            .ToList();
        Assert.All(times, t => Assert.NotNull(t));
        return times.Select(t => t!.Value);
    }

    // The sub-agent ids in stream order, with the agent's own entries left out and repeats in a row merged.
    private static List<string> Stretches(IEnumerable<string?> agentIds)
    {
        var stretches = new List<string>();
        foreach (var id in agentIds)
        {
            if (id is not null && (stretches.Count == 0 || stretches[^1] != id))
                stretches.Add(id);
        }
        return stretches;
    }
}
