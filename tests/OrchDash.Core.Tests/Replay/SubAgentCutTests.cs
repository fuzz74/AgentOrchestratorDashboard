using OrchDash.Core.Model;
using OrchDash.Core.Replay;
using Xunit;
using static OrchDash.Core.Tests.Replay.ReplayData;

namespace OrchDash.Core.Tests.Replay;

// 38.1: the replay cut of a session's sub-agents and of their store data.
public sealed class SubAgentCutTests
{
    private const string SurveyId = "toolu_survey";
    private const string CheckId = "toolu_check";

    private static readonly ProgressEntry RunStarted = Entry(At(11, 59, 0), null, "Run started: 1 tasks, max 1 in parallel");

    // "Survey" runs from 12:00:10 to 12:02:00 and reports; "Check" starts at 12:03:00 and still runs.
    private static SubAgent Survey { get; } = new(SurveyId, null, SurveyId, "Survey the parser", "Survey the parser",
        "Explore", "claude-haiku-4-5", false, "List the files.", At(12, 0, 10), At(12, 2, 0), SessionState.Succeeded,
        "Three files.");

    private static SubAgent Check { get; } = new(CheckId, null, CheckId, "Check the API", "Check the API",
        "general-purpose", "claude-haiku-4-5", true, "Check the API.", At(12, 3, 0), null, SessionState.Running, null);

    // The running alpha worker: its own calls and items, and the sub-agents' calls and items tagged with their ids.
    // Its store data holds figures and injected items for the worker and for each sub-agent, some without a time.
    private static Session Worker()
    {
        var session = Session(Files("alpha", AgentRole.Worker), At(12, 0, 0), SessionState.Running, "s-alpha-1",
            [
                Tool(At(12, 0, 10), "Agent", At(12, 2, 5)),
                Tool(At(12, 0, 30), "Glob", At(12, 0, 31)) with { AgentId = SurveyId },
                Text(At(12, 1, 50), "Three files") with { AgentId = SurveyId },
                Tool(At(12, 3, 0), "Agent", null, hasResult: false),
                Tool(At(12, 3, 20), "Read", At(12, 3, 21)) with { AgentId = CheckId },
            ],
            [
                Call("a1", At(12, 0, 5)),
                Call("s1", At(12, 0, 20)) with { AgentId = SurveyId },
                Call("a2", At(12, 2, 50)),
                Call("c1", At(12, 3, 10)) with { AgentId = CheckId },
            ],
            At(12, 3, 21));
        return session with
        {
            Content = session.Content with { SubAgents = [Survey, Check] },
            Stores = StoreData.Empty with
            {
                Calls = [Figures("a1", At(12, 0, 5)), Figures("a2", At(12, 2, 50))],
                Injected = [Injected(At(12, 0, 5))],
                CostUsd = 0.25,
                SubAgents = StoreData.NoSubAgents
                    .Add(SurveyId, StoreData.Empty with
                    {
                        SystemPrompt = ["You are a survey agent."],
                        Calls = [Figures("s1", At(12, 0, 20))],
                        Injected = [Injected(At(12, 0, 20)), Injected(At(12, 1, 30)), Injected(null)],
                    })
                    .Add(CheckId, StoreData.Empty with
                    {
                        Calls = [Figures("c1", At(12, 3, 10)), Figures("c2", null)],
                        Injected = [Injected(At(12, 3, 10))],
                    }),
            },
        };
    }

    private static Session Cut(Session live, DateTimeOffset at) =>
        Assert.Single(SnapshotReplay.At(Snapshot([RunStarted], sessions: [live]), at).Sessions);

    [Fact]
    public void Sub_agents_started_up_to_the_time_are_kept_in_order()
    {
        var live = Worker();

        Assert.Empty(Cut(live, At(12, 0, 9)).Content.SubAgents);
        Assert.Equal([SurveyId], Cut(live, At(12, 2, 30)).Content.SubAgents.Select(sub => sub.Id));
        Assert.Equal([SurveyId, CheckId], Cut(live, At(12, 3, 0)).Content.SubAgents.Select(sub => sub.Id));
    }

    [Fact]
    public void A_sub_agent_without_a_start_counts_at_the_session_start()
    {
        var unstarted = Check with { Id = "toolu_early", ToolCallId = "toolu_early", StartedAt = null };
        var worker = Worker();
        var live = worker with { Content = worker.Content with { SubAgents = [Survey, unstarted] } };

        var session = Cut(live, At(12, 0, 0));

        Assert.Same(unstarted, Assert.Single(session.Content.SubAgents));
    }

    [Fact]
    public void A_sub_agent_finishing_after_the_time_runs_again_without_finish_and_report()
    {
        var subAgent = Assert.Single(Cut(Worker(), At(12, 1, 0)).Content.SubAgents);

        Assert.Equal(Survey with { State = SessionState.Running, FinishedAt = null, Report = null }, subAgent);
    }

    [Fact]
    public void A_sub_agent_finished_by_the_time_is_the_live_instance()
    {
        var session = Cut(Worker(), At(12, 2, 0));

        Assert.Same(Survey, Assert.Single(session.Content.SubAgents));
    }

    [Fact]
    public void A_running_sub_agent_is_the_live_instance()
    {
        var subAgents = Cut(Worker(), At(12, 3, 10)).Content.SubAgents;

        Assert.Same(Survey, subAgents[0]);
        Assert.Same(Check, subAgents[1]);
    }

    [Fact]
    public void Sub_agent_items_and_calls_are_cut_by_time_like_the_agents_own()
    {
        var live = Worker();

        var content = Cut(live, At(12, 1, 0)).Content;

        Assert.Equal([null, SurveyId], content.Items.Select(item => item.AgentId));
        Assert.Null(((ToolCall)content.Items[0]).Result);
        Assert.Same(live.Content.Items[1], content.Items[1]);
        Assert.Equal(["a1", "s1"], content.Calls.Select(call => call.Id));
        Assert.Equal(At(12, 0, 31), content.LastEventAt);
    }

    [Fact]
    public void Store_values_of_sub_agents_are_cut_like_the_stores()
    {
        var worker = Worker();
        var live = worker.Stores;

        var stores = Cut(worker, At(12, 1, 0)).Stores;

        Assert.Equal([live.Calls[0]], stores.Calls);
        Assert.Equal([CheckId, SurveyId], stores.SubAgents.Keys.Order(StringComparer.Ordinal));

        var survey = live.SubAgents[SurveyId];
        var cutSurvey = stores.SubAgents[SurveyId];
        Assert.Equal([survey.Injected[0], survey.Injected[2]], cutSurvey.Injected);
        Assert.Equal(survey with { Injected = cutSurvey.Injected }, cutSurvey);

        var check = live.SubAgents[CheckId];
        var cutCheck = stores.SubAgents[CheckId];
        Assert.Equal([check.Calls[1]], cutCheck.Calls);
        Assert.Empty(cutCheck.Injected);
        Assert.Equal(check with { Calls = cutCheck.Calls, Injected = cutCheck.Injected }, cutCheck);
    }

    [Fact]
    public void A_store_value_that_nothing_is_cut_from_is_the_live_instance()
    {
        var live = Worker();

        var stores = Cut(live, At(12, 2, 30)).Stores;

        Assert.Same(live.Stores.SubAgents[SurveyId], stores.SubAgents[SurveyId]);
        Assert.NotSame(live.Stores.SubAgents[CheckId], stores.SubAgents[CheckId]);
        Assert.Equal([live.Stores.SubAgents[CheckId].Calls[1]], stores.SubAgents[CheckId].Calls);
    }

    [Fact]
    public void A_cut_in_a_sub_agent_store_value_alone_keeps_the_content_and_the_other_store_parts()
    {
        var worker = Worker();
        var check = worker.Stores.SubAgents[CheckId];
        var live = worker with
        {
            Stores = worker.Stores with
            {
                SubAgents = worker.Stores.SubAgents.SetItem(CheckId, check with { Injected = [.. check.Injected, Injected(At(12, 5, 0))] }),
            },
        };

        var session = Cut(live, At(12, 4, 0));

        Assert.Same(live.Content, session.Content);
        Assert.Same(live.Stores.SubAgents[SurveyId], session.Stores.SubAgents[SurveyId]);
        Assert.Equal(check.Injected, session.Stores.SubAgents[CheckId].Injected);
        // Calls, Injected and the other members are the live ones (record equality compares the arrays by instance).
        Assert.Equal(live.Stores with { SubAgents = session.Stores.SubAgents }, session.Stores);
    }

    [Fact]
    public void A_cut_in_the_content_alone_keeps_the_store_data()
    {
        var live = Worker();

        var session = Cut(live, At(12, 3, 15));

        Assert.NotSame(live.Content, session.Content);
        Assert.Same(live.Stores, session.Stores);
    }

    [Fact]
    public void A_cut_in_the_sub_agents_alone_keeps_the_other_content()
    {
        var worker = Worker();
        var live = worker with
        {
            Content = worker.Content with { SubAgents = [Survey, Check with { StartedAt = At(12, 5, 0) }] },
        };

        var session = Cut(live, At(12, 4, 0));

        Assert.Same(live.Stores, session.Stores);
        Assert.Equal(live.Content with { SubAgents = session.Content.SubAgents }, session.Content);
        Assert.Same(Survey, Assert.Single(session.Content.SubAgents));
    }

    [Fact]
    public void A_session_that_nothing_is_cut_from_is_the_live_instance()
    {
        var live = Snapshot([RunStarted], sessions: [Worker()]);

        var replay = SnapshotReplay.At(live, At(12, 4, 0));

        Assert.True(live.Sessions == replay.Sessions);
        Assert.Same(live.Sessions[0], replay.Sessions[0]);
    }

    [Fact]
    public void The_replay_snapshot_carries_the_cut_sub_agents_next_to_the_other_sessions()
    {
        var live = Create();
        live = live with { Sessions = live.Sessions.Add(Worker() with { Files = Files("delta", AgentRole.Worker) }) };

        var replay = SnapshotReplay.At(live, At(12, 1, 0));

        var session = replay.Sessions.Single(s => s.Files.TaskId == "delta");
        Assert.Equal([Survey with { State = SessionState.Running, FinishedAt = null, Report = null }], session.Content.SubAgents);
        Assert.Equal(SessionState.Running, SubAgents.StateOf(session, session.Content.SubAgents[0]));
        Assert.Same(live.Sessions[Planner], replay.Sessions[Planner]);
    }

    private static CallFigures Figures(string callId, DateTimeOffset? time) =>
        new(callId, time, new TokenUsage(10, 0, 0, 5), null, null, null, null);

    private static InjectedItem Injected(DateTimeOffset? time) => new("reminder", "user", time, "injected");
}
