using OrchDash.Core.Model;
using OrchDash.Core.Replay;
using Xunit;
using static OrchDash.Core.Tests.Replay.ReplayData;

namespace OrchDash.Core.Tests.Replay;

// 30.3 and 30.6: the session cut table, one test per row.
public sealed class SessionCutTests
{
    // One live snapshot for the tests that compare instances; the replay never changes it.
    private static readonly RunSnapshot Live = Create();

    private static Session SessionAt(int index, DateTimeOffset at)
    {
        var key = Live.Sessions[index].Files.Key;
        return SnapshotReplay.At(Live, at).Sessions.Single(session => session.Files.Key == key);
    }

    [Fact]
    public void Sessions_started_after_the_time_are_left_out_in_snapshot_order()
    {
        var live = Create();

        var replay = SnapshotReplay.At(live, At(12, 10, 5));

        Assert.Equal(live.Sessions.Take(6).Select(session => session.Files.Key), replay.Sessions.Select(session => session.Files.Key));
    }

    [Fact]
    public void A_session_starting_at_the_time_is_kept()
    {
        var replay = SnapshotReplay.At(Create(), At(12, 0, 10));

        Assert.Equal(["planner.json", "alpha-20261003-120005/attempt-1-worker.json"], replay.Sessions.Select(session => session.Files.Key));
    }

    [Fact]
    public void A_session_without_a_start_is_left_out()
    {
        var unstarted = Create().Sessions[AlphaWorker] with { StartedAt = null };
        var live = Snapshot([], sessions: [unstarted]);

        Assert.Empty(SnapshotReplay.At(live, End.AddDays(1)).Sessions);
    }

    [Fact]
    public void Items_are_kept_up_to_the_time_and_a_later_tool_result_is_cut()
    {
        var live = Live.Sessions[AlphaWorker];

        var items = SessionAt(AlphaWorker, At(12, 5, 0)).Content.Items;

        Assert.Equal(4, items.Length);
        Assert.Same(live.Content.Items[0], items[0]);
        Assert.Same(live.Content.Items[1], items[1]);
        Assert.Same(live.Content.Items[2], items[2]);
        Assert.Equal((ToolCall)live.Content.Items[3] with { Result = null }, items[3]);
    }

    [Fact]
    public void A_tool_result_is_kept_from_its_time()
    {
        var live = Live.Sessions[AlphaWorker];

        var items = SessionAt(AlphaWorker, At(12, 5, 30)).Content.Items;

        Assert.Same(live.Content.Items[3], items[3]);
    }

    [Fact]
    public void An_untimed_item_has_the_time_of_the_item_before_it()
    {
        var before = SessionAt(AlphaWorker, At(12, 0, 14)).Content.Items;
        var at = SessionAt(AlphaWorker, At(12, 0, 15)).Content.Items;

        Assert.Equal(["Reading the parser"], before.OfType<AssistantText>().Select(text => text.Text));
        Assert.Equal(["Reading the parser", "Edited"], at.OfType<AssistantText>().Select(text => text.Text));
        Assert.Null(at.OfType<ToolCall>().Single().Result);
    }

    [Fact]
    public void A_tool_result_without_a_time_counts_at_the_call_time()
    {
        var call = SessionAt(GammaWorker1, At(12, 1, 0)).Content.Items.OfType<ToolCall>().Single();

        Assert.NotNull(call.Result);
    }

    [Fact]
    public void Calls_are_kept_up_to_their_start()
    {
        var calls = SessionAt(AlphaWorker, At(12, 5, 0)).Content.Calls;

        Assert.Equal(["a1", "a2"], calls.Select(call => call.Id));
    }

    [Fact]
    public void A_call_without_a_start_counts_at_the_session_start()
    {
        var calls = SessionAt(GammaWorker1, At(12, 0, 30)).Content.Calls;

        Assert.Equal(["g1"], calls.Select(call => call.Id));
    }

    [Fact]
    public void Result_checkpoint_and_result_file_are_cut_before_the_last_event()
    {
        var session = SessionAt(AlphaWorker, At(12, 5, 49));

        Assert.Null(session.Content.Result);
        Assert.Null(session.Content.Checkpoint);
        Assert.False(session.Files.HasResultFile);
    }

    [Fact]
    public void Result_checkpoint_and_result_file_are_kept_from_the_last_event()
    {
        var live = Live.Sessions[AlphaWorker];

        var session = SessionAt(AlphaWorker, At(12, 5, 50));

        Assert.Same(live.Content.Result, session.Content.Result);
        Assert.Same(live.Content.Checkpoint, session.Content.Checkpoint);
        Assert.True(session.Files.HasResultFile);
    }

    [Fact]
    public void A_session_without_a_last_event_keeps_its_result()
    {
        var live = WithResult(Session(Files("alpha", AgentRole.Worker), At(12, 0, 0), SessionState.Succeeded, "s1"), Result(0.1));

        var session = Assert.Single(SnapshotReplay.At(Snapshot([], sessions: [live]), At(12, 0, 0)).Sessions);

        Assert.Same(live, session);
    }

    [Fact]
    public void The_last_event_is_the_live_one_from_its_time()
    {
        Assert.Equal(At(12, 5, 50), SessionAt(AlphaWorker, At(12, 5, 50)).Content.LastEventAt);
    }

    [Fact]
    public void Before_the_last_event_it_is_the_latest_kept_call_time()
    {
        Assert.Equal(At(12, 3, 30), SessionAt(AlphaWorker, At(12, 5, 0)).Content.LastEventAt);
    }

    [Fact]
    public void Before_the_last_event_it_is_the_latest_kept_tool_result_time()
    {
        Assert.Equal(At(12, 0, 16), SessionAt(AlphaWorker, At(12, 0, 16)).Content.LastEventAt);
    }

    [Fact]
    public void Before_the_last_event_it_is_the_latest_kept_item_time()
    {
        Assert.Equal(At(12, 10, 30), SessionAt(BetaWorker1, At(12, 11, 0)).Content.LastEventAt);
    }

    [Fact]
    public void Without_kept_items_and_calls_there_is_no_last_event()
    {
        var live = Session(Files("alpha", AgentRole.Worker), At(12, 0, 0), SessionState.Running, "s1",
            [Text(At(12, 2, 0), "later")], [], At(12, 2, 0));

        var session = Assert.Single(SnapshotReplay.At(Snapshot([], sessions: [live]), At(12, 1, 0)).Sessions);

        Assert.Empty(session.Content.Items);
        Assert.Null(session.Content.LastEventAt);
    }

    [Fact]
    public void A_rate_limit_is_cut_before_it_was_seen()
    {
        Assert.NotNull(Live.Sessions[AlphaWorker].Content.RateLimit);
        Assert.Null(SessionAt(AlphaWorker, At(12, 5, 44)).Content.RateLimit);
    }

    [Fact]
    public void A_rate_limit_is_kept_from_when_it_was_seen()
    {
        var live = Create();

        var replay = SnapshotReplay.At(live, At(12, 5, 45));

        Assert.Equal(live.Sessions[AlphaWorker].Content.RateLimit, replay.Sessions[AlphaWorker].Content.RateLimit);
    }

    [Fact]
    public void A_rate_limit_without_a_time_is_kept()
    {
        Assert.NotNull(SessionAt(BetaWorker2, At(12, 13, 0)).Content.RateLimit);
    }

    [Fact]
    public void Store_calls_and_injected_items_are_kept_up_to_their_time_or_without_one()
    {
        var live = Live.Sessions[AlphaWorker].Stores;

        var stores = SessionAt(AlphaWorker, At(12, 5, 0)).Stores;

        Assert.Equal([live.Calls[0], live.Calls[1], live.Calls[3]], stores.Calls);
        Assert.Equal([live.Injected[0], live.Injected[2]], stores.Injected);
        Assert.Equal(live with { Calls = stores.Calls, Injected = stores.Injected }, stores);
    }

    [Fact]
    public void Everything_else_is_unchanged()
    {
        var live = Live.Sessions[AlphaWorker];

        var session = SessionAt(AlphaWorker, At(12, 5, 0));

        Assert.Equal(live.Files with { HasResultFile = false }, session.Files);
        Assert.Equal((live.Provider, live.Prompt, live.StartedAt), (session.Provider, session.Prompt, session.StartedAt));
        Assert.Equal(live.Unavailable, session.Unavailable);
        Assert.Equal(
            live.Content with
            {
                Items = session.Content.Items, Calls = session.Content.Calls, Result = null, Checkpoint = null,
                LastEventAt = session.Content.LastEventAt, RateLimit = null,
            },
            session.Content);
    }

    [Fact]
    public void A_shown_result_that_is_no_error_gives_Succeeded()
    {
        Assert.Equal(SessionState.Succeeded, SessionAt(AlphaWorker, At(12, 5, 50)).State);
    }

    [Fact]
    public void A_shown_error_result_gives_Failed()
    {
        Assert.Equal(SessionState.Failed, SessionAt(GammaWorker1, At(12, 5, 0)).State);
    }

    [Fact]
    public void A_result_file_without_a_result_gives_Failed_from_the_last_event()
    {
        Assert.Equal(SessionState.Failed, SessionAt(GammaWorker3, At(12, 19, 40)).State);
        Assert.Equal(SessionState.Running, SessionAt(GammaWorker3, At(12, 19, 39)).State);
    }

    [Fact]
    public void A_session_without_a_result_runs_while_the_run_runs()
    {
        Assert.Equal(SessionState.Running, SessionAt(AlphaWorker, At(12, 5, 0)).State);
    }

    [Fact]
    public void A_session_without_a_result_runs_while_the_run_stops()
    {
        var replay = SnapshotReplay.At(Create(), At(12, 25, 0));

        Assert.Equal(RunPhase.Stopping, replay.Run.Phase);
        Assert.Equal(SessionState.Running, replay.Sessions[BetaWorker2].State);
    }

    [Fact]
    public void The_planner_runs_while_the_run_is_planned()
    {
        var replay = SnapshotReplay.At(Create(), At(11, 59, 30));

        Assert.Equal(RunPhase.Planning, replay.Run.Phase);
        Assert.Equal(SessionState.Running, Assert.Single(replay.Sessions).State);
    }

    [Fact]
    public void A_later_kept_session_of_the_same_task_gives_Aborted()
    {
        Assert.Equal(SessionState.Running, SessionAt(BetaWorker1, At(12, 12, 4)).State);
        Assert.Equal(SessionState.Aborted, SessionAt(BetaWorker1, At(12, 12, 5)).State);
    }

    [Fact]
    public void A_later_session_of_any_task_aborts_the_planner()
    {
        var progress = Entry(At(12, 0, 0), null, "Planning from .orchestrator/spec.md with opus");
        var planner = Session(Files(null, AgentRole.Planner), At(12, 0, 0), SessionState.Aborted, "p", lastEventAt: At(12, 3, 0));
        var worker = Session(Files("alpha", AgentRole.Worker), At(12, 2, 0), SessionState.Running, "w", lastEventAt: At(12, 3, 0));
        var live = Snapshot([progress], sessions: [planner, worker]);

        Assert.Equal(SessionState.Running, SnapshotReplay.At(live, At(12, 1, 0)).Sessions[0].State);
        Assert.Equal(SessionState.Aborted, SnapshotReplay.At(live, At(12, 2, 0)).Sessions[0].State);
    }

    [Fact]
    public void A_session_without_a_result_is_Aborted_when_the_run_has_finished()
    {
        var progress = new[]
        {
            Entry(At(12, 0, 0), null, "Run started: 1 tasks, max 1 in parallel"),
            Entry(At(12, 3, 0), null, "Run finished: 0 done, 1 failed"),
        };
        var worker = Session(Files("alpha", AgentRole.Worker), At(12, 1, 0), SessionState.Aborted, "w",
            [Text(At(12, 4, 0), "late")], [], At(12, 4, 0));

        var replay = SnapshotReplay.At(Snapshot(progress, sessions: [worker]), At(12, 3, 30));

        Assert.Equal(RunPhase.Finished, replay.Run.Phase);
        Assert.Equal(SessionState.Aborted, Assert.Single(replay.Sessions).State);
    }

    [Fact]
    public void A_session_that_nothing_is_cut_from_is_the_live_instance()
    {
        var live = Create();

        var replay = SnapshotReplay.At(live, At(12, 10, 0));

        Assert.Same(live.Sessions[Planner], replay.Sessions[Planner]);
        Assert.Same(live.Sessions[AlphaWorker], replay.Sessions[AlphaWorker]);
        Assert.Same(live.Sessions[AlphaReview2], replay.Sessions[AlphaReview2]);
    }

    [Fact]
    public void A_session_whose_state_alone_changes_keeps_its_live_content()
    {
        var live = Create();

        var replay = SnapshotReplay.At(live, At(12, 11, 25));

        Assert.NotSame(live.Sessions[BetaWorker1], replay.Sessions[BetaWorker1]);
        Assert.Same(live.Sessions[BetaWorker1].Content, replay.Sessions[BetaWorker1].Content);
        Assert.Equal(live.Sessions[BetaWorker1] with { State = SessionState.Running }, replay.Sessions[BetaWorker1]);
    }
}
