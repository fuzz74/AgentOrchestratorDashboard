using OrchDash.Core.Model;
using OrchDash.Core.Timeline;
using Xunit;
using static OrchDash.Core.Tests.Timeline.TimelineData;

namespace OrchDash.Core.Tests.Timeline;

// 38.2: the sub-agent events, and the AgentId of sub-agent calls and items.
public sealed class TimelineSubAgentTests
{
    private const string Key = "alpha/worker-1.json";

    private static SubAgent Sub(string id, DateTimeOffset? startedAt, DateTimeOffset? finishedAt) =>
        new(id, null, id, "Sub " + id, "Sub " + id, "Explore", null, false, "Prompt " + id, startedAt, finishedAt,
            finishedAt is null ? SessionState.Running : SessionState.Succeeded, finishedAt is null ? null : "Report " + id);

    private static Session WithSubAgents(Session session, params SubAgent[] subAgents) =>
        session with { Content = session.Content with { SubAgents = [.. subAgents] } };

    // The worker starts "s1" at 12:00:03 (finished at 12:00:10) and "s2" without a start time (still running).
    private static Session Worker() =>
        WithSubAgents(
            SessionOf(Key, At(12, 0, 0),
                calls: [Call("m1", At(12, 0, 1)), Call("m2", At(12, 0, 5)) with { AgentId = "s1" }],
                items:
                [
                    Tool(At(12, 0, 2), "Agent"),
                    Tool(At(12, 0, 6), "Glob") with { AgentId = "s1" },
                    Text(At(12, 0, 8), "Three files") with { AgentId = "s1" },
                    Text(At(12, 0, 20)),
                ],
                result: Result(), lastEventAt: At(12, 0, 30)),
            Sub("s1", At(12, 0, 3), At(12, 0, 10)),
            Sub("s2", null, null));

    [Fact]
    public void Sub_agent_events_are_ordered_by_time_with_the_sessions_other_events()
    {
        var events = TimelineBuilder.Build(Snapshot(sessions: [Worker()]));

        Assert.Equal(
            [Key + ":prompt", Key + ":sub:s2:prompt", Key + ":call:0", Key + ":item:0", Key + ":sub:s1:prompt",
             Key + ":call:1", Key + ":item:1", Key + ":item:2", Key + ":sub:s1:result", Key + ":item:3", Key + ":result"],
            Keys(events));
        Assert.Equal(
            [At(12, 0, 0), At(12, 0, 0), At(12, 0, 1), At(12, 0, 2), At(12, 0, 3), At(12, 0, 5), At(12, 0, 6), At(12, 0, 8),
             At(12, 0, 10), At(12, 0, 20), At(12, 0, 30)],
            events.Select(e => e.Time));
    }

    [Fact]
    public void Sub_agent_events_have_kind_group_index_session_and_agent_id_only()
    {
        var session = Worker();

        var events = TimelineBuilder.Build(Snapshot(sessions: [session]))
            .Where(e => e.Key.Contains(":sub:", StringComparison.Ordinal)).ToArray();

        Assert.Equal([Key + ":sub:s2:prompt", Key + ":sub:s1:prompt", Key + ":sub:s1:result"], events.Select(e => e.Key));
        Assert.Equal([TimelineKind.Prompt, TimelineKind.Prompt, TimelineKind.Result], events.Select(e => e.Kind));
        Assert.Equal(["s2", "s1", "s1"], events.Select(e => e.AgentId));
        Assert.All(events, e =>
        {
            Assert.Equal("alpha", e.Group);
            Assert.Equal(0, e.Index);
            Assert.Same(session, e.Session);
            Assert.Null(e.Entry);
            Assert.Null(e.Call);
            Assert.Null(e.Item);
            Assert.Null(e.Result);
        });
    }

    [Fact]
    public void Calls_and_items_carry_their_agent_id_and_the_sessions_own_events_none()
    {
        var session = Worker();

        var events = TimelineBuilder.Build(Snapshot(sessions: [session])).ToDictionary(e => e.Key);

        Assert.Null(events[Key + ":call:0"].AgentId);
        Assert.Equal("s1", events[Key + ":call:1"].AgentId);
        Assert.Same(session.Content.Calls[1], events[Key + ":call:1"].Call);
        Assert.Null(events[Key + ":item:0"].AgentId);
        Assert.Equal("s1", events[Key + ":item:1"].AgentId);
        Assert.Equal("s1", events[Key + ":item:2"].AgentId);
        Assert.Same(session.Content.Items[2], events[Key + ":item:2"].Item);
        Assert.Null(events[Key + ":item:3"].AgentId);
        Assert.Null(events[Key + ":prompt"].AgentId);
        Assert.Null(events[Key + ":result"].AgentId);
    }

    [Fact]
    public void Within_a_session_equal_times_order_items_then_each_sub_agents_prompt_and_result_then_the_result()
    {
        var t = At(12, 0, 0);
        var session = WithSubAgents(
            SessionOf(Key, t, calls: [Call("m1", t), Call("m2", t) with { AgentId = "b" }],
                items: [Text(t) with { AgentId = "b" }, Tool(t)], result: Result(), lastEventAt: t),
            Sub("b", t, t),
            Sub("a", t, t));

        var events = TimelineBuilder.Build(Snapshot(sessions: [session]));

        Assert.Equal(
            [Key + ":prompt", Key + ":call:0", Key + ":call:1", Key + ":item:0", Key + ":item:1",
             Key + ":sub:b:prompt", Key + ":sub:b:result", Key + ":sub:a:prompt", Key + ":sub:a:result", Key + ":result"],
            Keys(events));
    }

    [Fact]
    public void A_sub_agent_finishing_with_the_next_session_start_comes_before_that_session()
    {
        var first = WithSubAgents(SessionOf(Key, At(12, 0, 0)), Sub("s1", At(12, 0, 1), At(12, 0, 5)));
        var second = SessionOf("beta/worker-1.json", At(12, 0, 5), taskId: "beta");

        var events = TimelineBuilder.Build(Snapshot(sessions: [first, second]));

        Assert.Equal([Key + ":prompt", Key + ":sub:s1:prompt", Key + ":sub:s1:result", "beta/worker-1.json:prompt"],
            Keys(events));
    }

    [Fact]
    public void A_running_sub_agent_has_no_result_event()
    {
        var session = WithSubAgents(SessionOf(Key, At(12, 0, 0)), Sub("s1", At(12, 0, 1), null));

        var events = TimelineBuilder.Build(Snapshot(sessions: [session]));

        Assert.Equal([Key + ":prompt", Key + ":sub:s1:prompt"], Keys(events));
    }

    [Fact]
    public void A_sub_agent_of_an_unstarted_session_has_no_events()
    {
        var session = WithSubAgents(SessionOf(Key, null), Sub("s1", At(12, 0, 1), At(12, 0, 2)));

        Assert.Empty(TimelineBuilder.Build(Snapshot(sessions: [session])));
    }

    [Fact]
    public void Sub_agent_events_take_the_group_of_their_session()
    {
        var planner = WithSubAgents(SessionOf("planner.json", At(12, 0, 0), taskId: null, role: AgentRole.Planner),
            Sub("agent-p1", At(12, 0, 1), At(12, 0, 2)));

        var events = TimelineBuilder.Build(Snapshot(sessions: [planner]));

        Assert.Equal(["planner.json:prompt", "planner.json:sub:agent-p1:prompt", "planner.json:sub:agent-p1:result"], Keys(events));
        Assert.All(events, e => Assert.Equal("planner", e.Group));
    }

    [Fact]
    public void Sub_agent_keys_stay_unique_for_sessions_sharing_a_key_and_a_repeated_sub_agent_id()
    {
        var t = At(12, 0, 0);
        var first = WithSubAgents(SessionOf(Key, t), Sub("s1", t, t), Sub("s1", t, null));
        var second = WithSubAgents(SessionOf(Key, t), Sub("s1", t, t));

        var events = TimelineBuilder.Build(Snapshot(sessions: [first, second]));

        Assert.Equal(
            [Key + ":prompt", Key + ":sub:s1:prompt", Key + ":sub:s1:result", Key + ":sub:s1#2:prompt",
             Key + "#2:prompt", Key + "#2:sub:s1:prompt", Key + "#2:sub:s1:result"],
            Keys(events));
        Assert.All(events.Where(e => e.Key.Contains(":sub:", StringComparison.Ordinal)), e => Assert.Equal("s1", e.AgentId));
    }

    [Fact]
    public void Keys_are_unique_over_a_snapshot_with_sub_agents()
    {
        var t = At(12, 0, 0);
        Session Full(string key, string? task) => WithSubAgents(
            SessionOf(key, t, taskId: task, calls: [Call("m1", t), Call("m2", null) with { AgentId = "s1" }],
                items: [Tool(t), Text(null) with { AgentId = "s1" }], result: Result(), lastEventAt: t.AddMinutes(1)),
            Sub("s1", t, t.AddSeconds(30)), Sub("s2", null, null));
        var sessions = new[] { Full(Key, "alpha"), Full("beta/worker-1.json", "beta"), Full("planner.json", null) };

        var events = TimelineBuilder.Build(Snapshot([Entry(t, "Run started")], sessions));

        Assert.Equal(1 + 3 * (1 + 2 + 2 + 3 + 1), events.Length);
        Assert.Equal(events.Length, events.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Before_and_after_step_through_the_sub_agent_event_times_like_any_other()
    {
        var events = TimelineBuilder.Build(Snapshot(sessions: [Worker()]));

        Assert.Equal(At(12, 0, 3), TimelineBuilder.After(events, At(12, 0, 2)));
        Assert.Equal(At(12, 0, 2), TimelineBuilder.Before(events, At(12, 0, 3)));
        Assert.Equal(At(12, 0, 10), TimelineBuilder.After(events, At(12, 0, 8)));
        Assert.Equal(At(12, 0, 10), TimelineBuilder.Before(events, At(12, 0, 20)));
        Assert.Null(TimelineBuilder.Before(events, At(12, 0, 0)));
        Assert.Null(TimelineBuilder.After(events, At(12, 0, 30)));
    }
}
