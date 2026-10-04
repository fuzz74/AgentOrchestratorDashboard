using System.Text.Json.Nodes;
using OrchDash.Core.Model;
using Xunit;
using static OrchDash.Core.Tests.RunFolder.TempRunFolder;

namespace OrchDash.Core.Tests.RunFolder;

public sealed class RunFolderReaderTaskTests : IDisposable
{
    private readonly TempRunFolder _run = new();

    public void Dispose() => _run.Dispose();

    [Fact]
    public void Diamond_gives_waves_and_dependents()
    {
        _run.WriteTasks(Task("d", ["b", "c"]), Task("b", ["a"]), Task("c", ["a"]), Task("a"));

        var data = _run.Read();

        Assert.Equal(
            [("d", 3, 0), ("b", 2, 1), ("c", 2, 1), ("a", 1, 3)],
            data.Tasks.Select(t => (t.Id, t.Wave, t.Dependents)));
        Assert.Empty(data.Problems);
    }

    [Fact]
    public void Chain_gives_waves_and_dependents()
    {
        _run.WriteTasks(Task("a"), Task("b", ["a"]), Task("c", ["b"]), Task("d", ["c"]));

        var data = _run.Read();

        Assert.Equal(
            [("a", 1, 3), ("b", 2, 2), ("c", 3, 1), ("d", 4, 0)],
            data.Tasks.Select(t => (t.Id, t.Wave, t.Dependents)));
    }

    [Fact]
    public void A_dep_that_is_not_a_task_is_ignored()
    {
        _run.WriteTasks(Task("a", ["missing"]), Task("b", ["a", "missing"]));
        _run.WriteState(("a", "done"));

        var data = _run.Read();

        Assert.Equal([("a", 1, 1), ("b", 2, 0)], data.Tasks.Select(t => (t.Id, t.Wave, t.Dependents)));
        Assert.Equal(["a", "missing"], data.Tasks[1].Deps);
        Assert.Equal("ready", data.Tasks[1].Detail);
    }

    [Fact]
    public void A_dependency_cycle_neither_hangs_nor_throws()
    {
        _run.WriteTasks(Task("x", ["y"]), Task("y", ["x"]), Task("z", ["x"]), Task("self", ["self"]));

        var data = _run.Read();

        Assert.Equal(["x", "y", "z", "self"], data.Tasks.Select(t => t.Id));
        Assert.All(data.Tasks, t => Assert.InRange(t.Wave, 1, 4));
        Assert.Equal([2, 2, 0, 0], data.Tasks.Select(t => t.Dependents));
        Assert.Equal(1, data.Tasks[3].Wave);
        Assert.Empty(data.Problems);
    }

    [Fact]
    public void A_pending_task_with_a_failed_direct_or_indirect_dependency_is_blocked()
    {
        _run.WriteTasks(Task("a"), Task("b", ["a"]), Task("c", ["b"]), Task("d"), Task("e", ["d"]), Task("f", ["e"]), Task("g", ["b"]));
        _run.WriteState(("a", "failed"), ("d", "failed"), ("e", "done"), ("g", "running"));

        var data = _run.Read();

        Assert.Equal(
            [
                ("a", TaskState.Failed), ("b", TaskState.Blocked), ("c", TaskState.Blocked),
                ("d", TaskState.Failed), ("e", TaskState.Done), ("f", TaskState.Blocked), ("g", TaskState.Running),
            ],
            data.Tasks.Select(t => (t.Id, t.Status)));
        Assert.All(data.Tasks.Where(t => t.Status == TaskState.Blocked), t => Assert.Equal("a dependency failed", t.Detail));
    }

    [Fact]
    public void State_fills_each_task()
    {
        _run.WriteTasks(Task("core"));
        _run.WriteState(new JsonObject
        {
            ["core"] = JsonNode.Parse("""
                {
                  "status": "done", "mode": "sync", "attempts": 2, "syncRuns": 3, "specRejections": 1, "costUsd": 1.1573,
                  "sessionId": "abc", "summary": "Sum", "notes": "Notes", "error": "Err", "feedback": "Feed",
                  "startedAt": "2026-10-03T11:34:44.6566436+02:00", "finishedAt": "2026-10-03T11:35:58+02:00",
                  "mergedSha": "f597f7f"
                }
                """),
        });

        var task = Assert.Single(_run.Read().Tasks);

        Assert.Equal(TaskState.Done, task.Status);
        Assert.Equal(("sync", 2, 3, 1, 1.1573), (task.Mode, task.Attempts, task.SyncRuns, task.SpecRejections, task.CostUsd));
        Assert.Equal(("abc", "Sum", "Notes", "Err", "Feed", "f597f7f"),
            (task.SessionId, task.Summary, task.Notes, task.Error, task.Feedback, task.MergedSha));
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 11, 34, 44, TimeSpan.FromHours(2)).AddTicks(6566436), task.StartedAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 11, 35, 58, TimeSpan.FromHours(2)), task.FinishedAt);
    }

    [Theory]
    [InlineData("pending", TaskState.Pending)]
    [InlineData("running", TaskState.Running)]
    [InlineData("done", TaskState.Done)]
    [InlineData("failed", TaskState.Failed)]
    [InlineData("blocked", TaskState.Pending)]
    [InlineData("Done", TaskState.Pending)]
    [InlineData("", TaskState.Pending)]
    public void Status_is_read_from_state(string status, TaskState expected)
    {
        _run.WriteTasks(Task("a"));
        _run.WriteState(("a", status));

        Assert.Equal(expected, Assert.Single(_run.Read().Tasks).Status);
    }

    [Fact]
    public void Missing_fields_give_the_defaults()
    {
        _run.Write("tasks.json", """{ "tasks": [ { "id": "a" }, { "id": "b", "title": null, "deps": null, "owns": [1, "src/**"] }, { "id": "c" } ] }""");
        _run.Write("state.json", """
            { "tasks": { "b": { "status": 7, "mode": null, "attempts": "two", "costUsd": "1", "startedAt": "yesterday" }, "c": null } }
            """);

        var data = _run.Read();

        Assert.Equal(new PlanInfo(null, null, "orch/integration", data.Plan!.Settings), data.Plan);
        Assert.Empty(data.Plan.Settings);
        Assert.Equal(3, data.Tasks.Length);
        foreach (var task in data.Tasks)
        {
            Assert.Equal(("", "", null, null), (task.Title, task.Prompt, task.Acceptance, task.Model));
            Assert.Empty(task.Deps);
            Assert.Equal((TaskState.Pending, "fresh", 0, 0, 0, 0.0),
                (task.Status, task.Mode, task.Attempts, task.SyncRuns, task.SpecRejections, task.CostUsd));
            Assert.Equal<object?>([null, null, null, null, null, null, null, null],
                [task.SessionId, task.Summary, task.Notes, task.Error, task.Feedback, task.StartedAt, task.FinishedAt, task.MergedSha]);
            Assert.Equal((1, 0), (task.Wave, task.Dependents));
            Assert.Equal("ready", task.Detail);
        }
        Assert.Equal(["src/**"], data.Tasks[1].Owns);
        Assert.Empty(data.Problems);
    }

    [Fact]
    public void A_task_without_a_string_id_is_skipped_with_a_problem()
    {
        _run.Write("tasks.json", """{ "tasks": [ { "title": "no id" }, { "id": "a" }, { "id": 5 }, "text" ] }""");

        var data = _run.Read();

        Assert.Equal("a", Assert.Single(data.Tasks).Id);
        Assert.Equal(
            ["tasks.json: task 1 has no id and is skipped", "tasks.json: task 3 has no id and is skipped", "tasks.json: task 4 has no id and is skipped"],
            data.Problems);
    }

    [Fact]
    public void Plan_and_settings_as_compact_json()
    {
        _run.Write("tasks.json", """
            {
              "spec": ".orchestrator/spec.md",
              "baseBranch": "main",
              "integrationBranch": "orch/int",
              "settings": {
                "model": "sonnet",
                "maxAttempts": 3,
                "review": true,
                "shared": [ "a.cs", "README.md" ],
                "nested": { "k": null, "n": 1.5 },
                "setup": "dotnet build && dotnet test <x> 'y' æ",
                "none": null
              },
              "tasks": []
            }
            """);

        var plan = _run.Read().Plan!;

        Assert.Equal((".orchestrator/spec.md", "main", "orch/int"), (plan.Spec, plan.BaseBranch, plan.IntegrationBranch));
        Assert.Equal(
            [
                ("maxAttempts", "3"),
                ("model", "\"sonnet\""),
                ("nested", """{"k":null,"n":1.5}"""),
                ("none", "null"),
                ("review", "true"),
                ("setup", "\"dotnet build && dotnet test <x> 'y' æ\""),
                ("shared", """["a.cs","README.md"]"""),
            ],
            plan.Settings.Select(setting => (setting.Key, setting.Value)));
    }

    [Fact]
    public void Files_with_a_bom_are_read()
    {
        _run.Write("tasks.json", """{ "spec": "s", "tasks": [ { "id": "a" } ] }""", bom: true);
        _run.Write("state.json", """{ "tasks": { "a": { "status": "done", "attempts": 1 } } }""", bom: true);
        _run.Write("progress.md", Started + ClaudeLine + Finished, bom: true);

        var data = _run.Read();

        Assert.Equal("s", data.Plan!.Spec);
        Assert.Equal(TaskState.Done, Assert.Single(data.Tasks).Status);
        Assert.Equal(3, data.Progress.Length);
        Assert.Equal(RunPhase.Finished, data.Run.Phase);
        Assert.Equal(new DateTime(2026, 10, 3, 12, 0, 0), data.Run.StartedAt!.Value.DateTime);
        Assert.Empty(data.Problems);
    }
}
