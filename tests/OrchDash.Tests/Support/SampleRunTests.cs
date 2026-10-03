using OrchDash.Contracts;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.Support;

public sealed class SampleRunTests
{
    private readonly RunSnapshot _run = SampleRun.Create();

    [Fact]
    public void Every_call_returns_a_new_snapshot_with_the_same_content()
    {
        var other = SampleRun.Create();

        Assert.NotSame(_run, other);
        Assert.Equal(_run.ReadAt, other.ReadAt);
        Assert.Equal(_run.Tasks.Select(t => t with { Deps = [], Owns = [] }), other.Tasks.Select(t => t with { Deps = [], Owns = [] }));
        Assert.Equal(_run.Sessions.Select(s => s.Files), other.Sessions.Select(s => s.Files));
        Assert.Equal(_run.Progress, other.Progress);
        Assert.Equal(_run.Problems, other.Problems);
    }

    [Fact]
    public void Run_is_running()
    {
        Assert.Equal(1, _run.Version);
        Assert.Equal(@"C:\Work\SampleRepo", _run.RepoPath);
        Assert.Equal("12:30:00", Look.Clock(_run.ReadAt));
        Assert.Equal(new DateTime(2026, 10, 3, 12, 30, 0), _run.ReadAt.DateTime);
        Assert.Equal(RunPhase.Running, _run.Run.Phase);
        Assert.Equal("12:00:00", Look.Clock(_run.Run.StartedAt!.Value));
        Assert.Null(_run.Run.FinishedAt);
        Assert.Equal(2, _run.Run.MaxParallel);
        Assert.Equal(Provider.Claude, _run.Run.Provider);
        Assert.NotNull(_run.Run.AgentPath);
        Assert.False(_run.Run.StopRequested);
        Assert.NotNull(_run.Plan);
        Assert.True(_run.Plan.Settings.Count >= 2);
    }

    [Fact]
    public void Tasks_are_in_snapshot_order_with_their_status_and_detail()
    {
        Assert.Equal(
        [
            ("alpha", 1, TaskState.Done, "0.25 USD, 1 attempt(s)"),
            ("gamma", 1, TaskState.Failed, "acceptance failed: 2 tests failed"),
            ("beta", 2, TaskState.Running, "worker (attempt 1)"),
            ("delta", 2, TaskState.Blocked, "a dependency failed"),
            ("epsilon", 3, TaskState.Pending, "waiting for beta"),
        ],
        _run.Tasks.Select(t => (t.Id, t.Wave, t.Status, t.Detail)));
    }

    [Fact]
    public void Tasks_carry_the_fields_pages_show()
    {
        var alpha = _run.Tasks.Single(t => t.Id == "alpha");
        Assert.Equal(0.25, alpha.CostUsd);
        Assert.Equal(1, alpha.Attempts);
        Assert.NotNull(alpha.Summary);
        Assert.NotNull(alpha.Notes);
        Assert.NotNull(alpha.StartedAt);
        Assert.NotNull(alpha.FinishedAt);
        Assert.Equal(2, alpha.Dependents);

        var gamma = _run.Tasks.Single(t => t.Id == "gamma");
        Assert.Equal(3, gamma.Attempts);
        Assert.Equal(2, gamma.Error!.Split('\n').Length);
        Assert.NotNull(gamma.Feedback);

        var beta = _run.Tasks.Single(t => t.Id == "beta");
        Assert.Equal(["alpha"], beta.Deps);
        Assert.Equal("12:10:00", Look.Clock(beta.StartedAt!.Value));

        Assert.Equal(["gamma"], _run.Tasks.Single(t => t.Id == "delta").Deps);
        Assert.Equal(["beta"], _run.Tasks.Single(t => t.Id == "epsilon").Deps);
        Assert.All(_run.Tasks, t =>
        {
            Assert.NotEmpty(t.Owns);
            Assert.NotEmpty(t.Prompt);
        });
    }

    [Fact]
    public void Sessions_are_in_snapshot_order()
    {
        Assert.Equal(
        [
            (SampleRun.AlphaWorkerKey, AgentRole.Worker, Provider.Claude, SessionState.Succeeded),
            (SampleRun.AlphaReviewKey, AgentRole.Reviewer, Provider.Copilot, SessionState.Succeeded),
            (SampleRun.BetaWorkerKey, AgentRole.Worker, Provider.Claude, SessionState.Running),
        ],
        _run.Sessions.Select(s => (s.Files.Key, s.Files.Role, s.Provider, s.State)));

        var starts = _run.Sessions.Select(s => s.StartedAt!.Value).ToList();
        Assert.Equal(starts.Order(), starts);
    }

    [Fact]
    public void Session_files_follow_the_file_name_grammar()
    {
        Assert.Equal("alpha/20261003-120005/attempt-1-worker.json", SampleRun.AlphaWorkerKey);
        Assert.Equal("alpha/20261003-120005/attempt-1-review-1.json", SampleRun.AlphaReviewKey);
        Assert.Equal("beta/20261003-121000/attempt-1-worker.json", SampleRun.BetaWorkerKey);

        Assert.All(_run.Sessions, s =>
        {
            var files = s.Files;
            var parts = files.Key.Split('/');
            Assert.Equal(parts[0], files.TaskId);
            Assert.Equal(parts[1], files.StartFolder);
            Assert.Equal(1, files.Attempt);
            Assert.False(files.IsNudge);
            var resultPath = @"C:\Work\SampleRepo\.orchestrator\logs\" + files.Key.Replace('/', '\\');
            Assert.Equal(resultPath, files.ResultPath);
            Assert.Equal(resultPath + ".prompt.md", files.PromptPath);
            Assert.Equal(resultPath + ".events.jsonl", files.EventsPath);
            Assert.Equal(resultPath + ".stderr", files.StderrPath);
            Assert.True(files.HasEventsFile);
        });
        Assert.Equal([0, 1, 0], _run.Sessions.Select(s => s.Files.ReviewTry));
    }

    [Fact]
    public void Alpha_worker_has_every_item_kind()
    {
        var session = _run.Sessions[0];
        var content = session.Content;

        Assert.True(session.Prompt.Split('\n').Length >= 5);
        Assert.NotNull(content.Init);
        Assert.Equal(2, content.Calls.Length);
        Assert.All(content.Calls, c => Assert.NotNull(c.Usage));
        Assert.Equal(
            [typeof(Thinking), typeof(AssistantText), typeof(ToolCall), typeof(Thinking), typeof(ToolCall), typeof(AssistantText)],
            content.Items.Select(i => i.GetType()));

        var thinking = Assert.IsType<Thinking>(content.Items[0]);
        Assert.True(thinking.Text.Split('\n').Length > 3);

        var edit = Assert.IsType<ToolCall>(content.Items[2]);
        Assert.Equal("Edit", edit.Name);
        Assert.NotNull(edit.Result);
        Assert.False(edit.Result.IsError);
        var diff = edit.Result.Diff!.Split('\n');
        Assert.Contains(diff, l => l.StartsWith("@@", StringComparison.Ordinal));
        Assert.Contains(diff, l => l.StartsWith('+'));
        Assert.Contains(diff, l => l.StartsWith('-'));

        var empty = Assert.IsType<Thinking>(content.Items[3]);
        Assert.Equal("", empty.Text);
        Assert.Equal(1200, empty.EstimatedTokens);

        var bash = Assert.IsType<ToolCall>(content.Items[4]);
        Assert.Equal("Bash", bash.Name);
        Assert.True(bash.Result!.IsError);

        Assert.All(content.Items, i => Assert.Contains(content.Calls, c => c.Id == i.CallId));
    }

    [Fact]
    public void Alpha_worker_has_a_worker_report()
    {
        var result = _run.Sessions[0].Content.Result;

        Assert.NotNull(result);
        Assert.False(result.IsError);
        Assert.Equal("success", result.Subtype);
        Assert.Equal(0.25, result.CostUsd);
        Assert.NotNull(result.Turns);
        Assert.NotNull(result.Duration);
        Assert.NotNull(result.Usage);
        Assert.NotNull(result.Worker);
        Assert.True(result.Worker.Summary.Split('\n').Length > 3);
        Assert.Equal(result.Worker, StructuredOutput.Parse(result.StructuredJson).Worker);
    }

    [Fact]
    public void Alpha_review_has_a_failing_verdict()
    {
        var session = _run.Sessions[1];
        var result = session.Content.Result;

        Assert.Equal(1, session.Files.ReviewTry);
        Assert.Equal(2, session.Content.UnparsedLines);
        Assert.NotNull(result);
        var review = result.Review;
        Assert.NotNull(review);
        Assert.Equal("pass", review.SpecVerdict);
        Assert.Equal("fail", review.QualityVerdict);
        Assert.Equal(2, review.Issues.Length);
        Assert.NotNull(review.Issues[0].File);
        Assert.Null(review.Issues[1].File);

        var parsed = StructuredOutput.Parse(result.StructuredJson).Review;
        Assert.NotNull(parsed);
        Assert.Equal(review.Issues, parsed.Issues);
        Assert.Equal(review with { Issues = [] }, parsed with { Issues = [] });
    }

    [Fact]
    public void Beta_worker_is_running_with_an_open_tool_call()
    {
        var session = _run.Sessions[2];
        var content = session.Content;

        Assert.Equal(SessionState.Running, session.State);
        Assert.False(session.Files.HasResultFile);
        Assert.Null(content.Result);
        Assert.True(content.Items.Length >= 6);
        var last = Assert.IsType<ToolCall>(content.Items[^1]);
        Assert.Null(last.Result);
        Assert.Equal("12:29:30", Look.Clock(content.LastEventAt!.Value));
    }

    [Fact]
    public void Progress_has_three_kinds_and_there_is_one_problem()
    {
        Assert.Equal(
        [
            (null, ProgressKind.Info),
            ("alpha", ProgressKind.Success),
            ("gamma", ProgressKind.Failure),
        ],
        _run.Progress.Select(p => (p.Source, p.Kind)));
        Assert.Equal(2, _run.Progress[1].Message.Split('\n').Length);
        Assert.Single(_run.Problems);
    }
}
