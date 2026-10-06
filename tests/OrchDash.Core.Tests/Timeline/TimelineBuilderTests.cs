using System.Collections.Immutable;
using OrchDash.Core.Model;
using OrchDash.Core.Timeline;
using Xunit;
using static OrchDash.Core.Tests.Timeline.TimelineData;

namespace OrchDash.Core.Tests.Timeline;

public sealed class TimelineBuilderTests
{
    [Fact]
    public void An_empty_snapshot_has_no_events()
    {
        Assert.Empty(TimelineBuilder.Build(RunSnapshot.Empty(RepoPath)));
    }

    [Fact]
    public void Default_arrays_count_as_empty()
    {
        var started = SessionOf("alpha/worker-1.json", At(12, 0, 0));
        var session = started with { Content = started.Content with { Calls = default, Items = default } };
        var snapshot = RunSnapshot.Empty(RepoPath) with { Progress = default, Sessions = default, Tasks = default };

        Assert.Empty(TimelineBuilder.Build(snapshot));
        var events = TimelineBuilder.Build(snapshot with { Sessions = [session] });
        Assert.Equal(["alpha/worker-1.json:prompt"], Keys(events));
    }

    [Fact]
    public void Events_are_ordered_by_time_across_two_sessions()
    {
        var first = SessionOf("alpha/worker-1.json", At(12, 0, 0),
            items: [Tool(At(12, 0, 10)), Text(At(12, 0, 40))], result: Result(), lastEventAt: At(12, 1, 0));
        var second = SessionOf("beta/worker-1.json", At(12, 0, 5), taskId: "beta",
            items: [Text(At(12, 0, 20)), Tool(At(12, 0, 50))]);

        var events = TimelineBuilder.Build(Snapshot(sessions: [first, second]));

        Assert.Equal(
            ["alpha/worker-1.json:prompt", "beta/worker-1.json:prompt", "alpha/worker-1.json:item:0",
             "beta/worker-1.json:item:0", "alpha/worker-1.json:item:1", "beta/worker-1.json:item:1",
             "alpha/worker-1.json:result"],
            Keys(events));
        Assert.Equal(events.Select(e => e.Time).Order(), events.Select(e => e.Time));
    }

    [Fact]
    public void Equal_times_put_orchestrator_events_first_then_sessions_in_snapshot_order()
    {
        var first = SessionOf("beta/worker-1.json", At(12, 0, 5), taskId: "beta");
        var second = SessionOf("alpha/worker-1.json", At(12, 0, 5));
        var entry = Entry(At(12, 0, 5), "started (fresh) in C:\\X", "alpha");

        var events = TimelineBuilder.Build(Snapshot([entry], [first, second]));

        Assert.Equal(["progress:0", "beta/worker-1.json:prompt", "alpha/worker-1.json:prompt"], Keys(events));
    }

    [Fact]
    public void Within_a_session_equal_times_order_prompt_calls_items_and_result()
    {
        var t = At(12, 0, 0);
        var session = SessionOf("alpha/worker-1.json", t,
            calls: [Call("m1", t), Call("m2", t)], items: [Text(t), Tool(t)], result: Result(), lastEventAt: t);

        var events = TimelineBuilder.Build(Snapshot(sessions: [session]));

        Assert.Equal(
            ["alpha/worker-1.json:prompt", "alpha/worker-1.json:call:0", "alpha/worker-1.json:call:1",
             "alpha/worker-1.json:item:0", "alpha/worker-1.json:item:1", "alpha/worker-1.json:result"],
            Keys(events));
    }

    [Fact]
    public void A_call_comes_before_an_item_of_the_same_time_even_when_the_call_is_listed_later()
    {
        var session = SessionOf("alpha/worker-1.json", At(12, 0, 0),
            calls: [Call("m1", At(12, 0, 1)), Call("m2", At(12, 0, 30))], items: [Tool(At(12, 0, 30))]);

        var events = TimelineBuilder.Build(Snapshot(sessions: [session]));

        Assert.Equal(
            ["alpha/worker-1.json:prompt", "alpha/worker-1.json:call:0", "alpha/worker-1.json:call:1",
             "alpha/worker-1.json:item:0"],
            Keys(events));
    }

    [Fact]
    public void An_untimed_item_gets_the_earlier_items_time_and_keeps_item_order()
    {
        var session = SessionOf("alpha/worker-1.json", At(12, 0, 0),
            items: [Tool(At(12, 0, 20)), Text(null), Tool(At(12, 0, 30))],
            calls: [Call("m1", At(12, 0, 25))]);

        var events = TimelineBuilder.Build(Snapshot(sessions: [session]));

        Assert.Equal(
            ["alpha/worker-1.json:prompt", "alpha/worker-1.json:item:0", "alpha/worker-1.json:item:1",
             "alpha/worker-1.json:call:0", "alpha/worker-1.json:item:2"],
            Keys(events));
        Assert.Equal(At(12, 0, 20), events.Single(e => e.Key == "alpha/worker-1.json:item:1").Time);
    }

    [Fact]
    public void An_untimed_first_item_gets_the_start()
    {
        var session = SessionOf("alpha/worker-1.json", At(12, 0, 0), items: [Text(null)]);

        var events = TimelineBuilder.Build(Snapshot(sessions: [session]));

        Assert.Equal(At(12, 0, 0), events.Single(e => e.Kind == TimelineKind.Text).Time);
    }

    [Fact]
    public void Orchestrator_entries_with_equal_times_keep_file_order()
    {
        var progress = new[]
        {
            Entry(At(12, 0, 9), "later"),
            Entry(At(12, 0, 5), "b", "alpha"),
            Entry(At(12, 0, 5), "a", "beta"),
            Entry(At(12, 0, 5), "c"),
        };

        var events = TimelineBuilder.Build(Snapshot(progress));

        Assert.Equal(["progress:1", "progress:2", "progress:3", "progress:0"], Keys(events));
    }

    [Fact]
    public void Orchestrator_events_have_key_index_group_and_entry()
    {
        var progress = new[]
        {
            Entry(At(12, 0, 0), "Run started: 2 tasks"),
            Entry(At(12, 0, 1), "Planning from spec.md with opus", "planner"),
            Entry(At(12, 0, 2), "Creating the project skeleton", "bootstrap"),
            Entry(At(12, 0, 3), "DONE in 1m", "alpha", ProgressKind.Success),
        };

        var events = TimelineBuilder.Build(Snapshot(progress));

        Assert.Equal(["progress:0", "progress:1", "progress:2", "progress:3"], Keys(events));
        Assert.Equal(["run", "planner", "bootstrap", "alpha"], events.Select(e => e.Group));
        Assert.Equal([0, 1, 2, 3], events.Select(e => e.Index));
        Assert.All(events, e => Assert.Equal(TimelineKind.Orchestrator, e.Kind));
        for (var i = 0; i < progress.Length; i++)
        {
            Assert.Same(progress[i], events[i].Entry);
            Assert.Equal(progress[i].Time, events[i].Time);
            Assert.Null(events[i].Session);
            Assert.Null(events[i].Call);
            Assert.Null(events[i].Item);
            Assert.Null(events[i].Result);
        }
    }

    [Fact]
    public void Session_events_have_kind_key_index_and_the_matching_member()
    {
        var call = Call("m1", At(12, 0, 2));
        var thought = Thought(At(12, 0, 3));
        var tool = Tool(At(12, 0, 4));
        var text = Text(At(12, 0, 5));
        var result = Result();
        var session = SessionOf("alpha/worker-1.json", At(12, 0, 0), calls: [call],
            items: [thought, tool, text], result: result, lastEventAt: At(12, 0, 6));

        var events = TimelineBuilder.Build(Snapshot(sessions: [session]));

        Assert.Equal(
            ["alpha/worker-1.json:prompt", "alpha/worker-1.json:call:0", "alpha/worker-1.json:item:1",
             "alpha/worker-1.json:item:2", "alpha/worker-1.json:result"],
            Keys(events));
        Assert.Equal(
            [TimelineKind.Prompt, TimelineKind.Call, TimelineKind.Tool, TimelineKind.Text, TimelineKind.Result],
            events.Select(e => e.Kind));
        Assert.Equal([0, 0, 1, 2, 0], events.Select(e => e.Index));
        Assert.Equal([At(12, 0, 0), At(12, 0, 2), At(12, 0, 4), At(12, 0, 5), At(12, 0, 6)], events.Select(e => e.Time));
        Assert.All(events, e =>
        {
            Assert.Same(session, e.Session);
            Assert.Equal("alpha", e.Group);
            Assert.Null(e.Entry);
        });
        Assert.Same(call, events[1].Call);
        Assert.Same(tool, events[2].Item);
        Assert.Same(text, events[3].Item);
        Assert.Same(result, events[4].Result);
        Assert.Null(events[0].Call);
        Assert.Null(events[0].Item);
        Assert.Null(events[0].Result);
        Assert.Null(events[1].Item);
        Assert.Null(events[2].Call);
        Assert.Null(events[2].Result);
        Assert.Null(events[4].Call);
        Assert.Null(events[4].Item);
    }

    [Fact]
    public void Sessions_without_a_task_id_are_grouped_by_role()
    {
        var sessions = new[]
        {
            SessionOf("bootstrap.json", At(12, 0, 0), taskId: null, role: AgentRole.Bootstrap),
            SessionOf("planner.json", At(12, 0, 1), taskId: null, role: AgentRole.Planner),
            SessionOf("odd-worker.json", At(12, 0, 2), taskId: null, role: AgentRole.Worker),
            SessionOf("odd-reviewer.json", At(12, 0, 3), taskId: null, role: AgentRole.Reviewer),
            SessionOf("odd-resolver.json", At(12, 0, 4), taskId: null, role: AgentRole.Resolver),
            SessionOf("gamma/reviewer-1.json", At(12, 0, 5), taskId: "gamma", role: AgentRole.Reviewer),
        };

        var events = TimelineBuilder.Build(Snapshot(sessions: sessions));

        Assert.Equal(["bootstrap", "planner", "worker", "reviewer", "resolver", "gamma"], events.Select(e => e.Group));
    }

    [Fact]
    public void Skipped_kinds_make_no_event()
    {
        var progress = new[]
        {
            Entry(At(12, 0, 0), "Run started"),
            Entry(At(12, 0, 1), "alpha: Bash dotnet build", "alpha", ProgressKind.Activity),
            Entry(At(12, 0, 2), "review passed", "alpha", ProgressKind.Success),
        };
        var kept = SessionOf("alpha/worker-1.json", At(12, 0, 0),
            items: [Thought(At(12, 0, 1)), User(At(12, 0, 2)), Note(At(12, 0, 3)), Text(At(12, 0, 4))]);
        var unstarted = SessionOf("beta/worker-1.json", null, taskId: "beta",
            calls: [Call("m1", At(12, 0, 1))], items: [Text(At(12, 0, 2))], result: Result(), lastEventAt: At(12, 0, 3));

        var events = TimelineBuilder.Build(Snapshot(progress, [kept, unstarted]));

        Assert.Equal(
            ["progress:0", "alpha/worker-1.json:prompt", "progress:2", "alpha/worker-1.json:item:3"],
            Keys(events));
        Assert.DoesNotContain(events, e => ReferenceEquals(e.Session, unstarted));
    }

    [Fact]
    public void A_result_without_last_event_time_and_a_call_without_start_get_the_session_start()
    {
        var start = At(12, 0, 0);
        var session = SessionOf("alpha/worker-1.json", start, calls: [Call("m1", null)],
            items: [Tool(At(12, 0, 10))], result: Result(isError: true), lastEventAt: null);

        var events = TimelineBuilder.Build(Snapshot(sessions: [session]));

        Assert.Equal(start, events.Single(e => e.Kind == TimelineKind.Call).Time);
        var result = events.Single(e => e.Kind == TimelineKind.Result);
        Assert.Equal(start, result.Time);
        Assert.Equal(
            ["alpha/worker-1.json:prompt", "alpha/worker-1.json:call:0", "alpha/worker-1.json:result",
             "alpha/worker-1.json:item:0"],
            Keys(events));
    }

    [Fact]
    public void Keys_are_unique_over_a_whole_snapshot()
    {
        var t = At(12, 0, 0);
        var progress = Enumerable.Range(0, 5).Select(i => Entry(t.AddSeconds(i), "entry " + i, i % 2 == 0 ? null : "alpha"));
        Session Full(string key, string? task) => SessionOf(key, t, taskId: task, calls: [Call("m1", t), Call("m2", null)],
            items: [Tool(t), Text(null), Thought(t), Tool(null)], result: Result(), lastEventAt: t.AddMinutes(1));
        var sessions = new[] { Full("alpha/worker-1.json", "alpha"), Full("beta/worker-1.json", "beta"), Full("planner.json", null) };

        var events = TimelineBuilder.Build(Snapshot(progress, sessions));

        Assert.Equal(5 + 3 * (1 + 2 + 3 + 1), events.Length);
        Assert.Equal(events.Length, events.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Two_sessions_sharing_a_key_still_get_unique_keys()
    {
        var t = At(12, 0, 0);
        var first = SessionOf("alpha/worker-1.json", t, items: [Text(t)], result: Result(), lastEventAt: t);
        var second = SessionOf("alpha/worker-1.json", t, items: [Text(t)], result: Result(), lastEventAt: t);
        var third = SessionOf("alpha/worker-1.json#2", t);

        var events = TimelineBuilder.Build(Snapshot(sessions: [first, second, third]));

        Assert.Equal(7, events.Length);
        Assert.Equal(events.Length, events.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(["alpha/worker-1.json:prompt", "alpha/worker-1.json:item:0", "alpha/worker-1.json:result"],
            Keys([.. events.Where(e => ReferenceEquals(e.Session, first))]));
    }

    [Fact]
    public void Before_and_after_step_through_the_event_times()
    {
        var progress = new[] { Entry(At(12, 0, 10), "a"), Entry(At(12, 0, 20), "b"), Entry(At(12, 0, 30), "c") };
        // Built unsorted on purpose: Before and After do not assume a sorted array.
        ImmutableArray<TimelineEvent> events = [.. TimelineBuilder.Build(Snapshot(progress)).OrderByDescending(e => e.Time)];

        Assert.Equal(At(12, 0, 10), TimelineBuilder.Before(events, At(12, 0, 20)));
        Assert.Equal(At(12, 0, 30), TimelineBuilder.After(events, At(12, 0, 20)));
        Assert.Equal(At(12, 0, 20), TimelineBuilder.Before(events, At(12, 0, 25)));
        Assert.Equal(At(12, 0, 30), TimelineBuilder.After(events, At(12, 0, 25)));
        Assert.Null(TimelineBuilder.Before(events, At(12, 0, 10)));
        Assert.Null(TimelineBuilder.Before(events, At(12, 0, 0)));
        Assert.Equal(At(12, 0, 10), TimelineBuilder.After(events, At(12, 0, 0)));
        Assert.Null(TimelineBuilder.After(events, At(12, 0, 30)));
        Assert.Null(TimelineBuilder.After(events, At(12, 1, 0)));
        Assert.Equal(At(12, 0, 30), TimelineBuilder.Before(events, At(12, 1, 0)));
    }

    [Fact]
    public void Before_and_after_on_an_empty_or_default_array_are_null()
    {
        Assert.Null(TimelineBuilder.Before([], At(12, 0, 0)));
        Assert.Null(TimelineBuilder.After([], At(12, 0, 0)));
        Assert.Null(TimelineBuilder.Before(default, At(12, 0, 0)));
        Assert.Null(TimelineBuilder.After(default, At(12, 0, 0)));
    }
}
