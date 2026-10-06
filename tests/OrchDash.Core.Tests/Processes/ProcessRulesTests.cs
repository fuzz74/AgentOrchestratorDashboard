using System.Collections.Immutable;
using OrchDash.Core.Model;
using OrchDash.Core.Processes;
using Xunit;
using static OrchDash.Core.Tests.Processes.ProcessData;

namespace OrchDash.Core.Tests.Processes;

// Spec 21.3: the process matching table of section 4.3.
public sealed class ProcessRulesTests
{
    private static readonly ImmutableArray<TaskView> Tasks = [Task("alpha"), Task("beta", sessionId: SidB)];

    private static AgentProcess Single(string commandLine, ImmutableArray<Session> sessions = default) =>
        Assert.Single(ProcessRules.Match([Process(1, commandLine)], Tasks, sessions.IsDefault ? [] : sessions));

    [Fact]
    public void Planner_name_gives_the_planner_without_a_task()
    {
        var process = Single("claude -p --name orch:planner");

        Assert.Null(process.TaskId);
        Assert.Equal(AgentRole.Planner, process.Role);
    }

    [Fact]
    public void Bootstrap_name_gives_the_bootstrap_without_a_task()
    {
        var process = Single("claude -p --name orch:bootstrap");

        Assert.Null(process.TaskId);
        Assert.Equal(AgentRole.Bootstrap, process.Role);
    }

    [Fact]
    public void Plain_id_with_equals_sign_gives_the_worker()
    {
        var process = Single("claude -p --name=orch:alpha --verbose");

        Assert.Equal("alpha", process.TaskId);
        Assert.Equal(AgentRole.Worker, process.Role);
    }

    [Fact]
    public void Quoted_review_name_gives_the_reviewer()
    {
        var process = Single("""copilot.exe -p "do it" --name "orch:alpha:review" --allow-all-tools""");

        Assert.Equal("alpha", process.TaskId);
        Assert.Equal(AgentRole.Reviewer, process.Role);
    }

    [Fact]
    public void Resolve_name_gives_the_resolver()
    {
        var process = Single("claude -p --name orch:beta:resolve");

        Assert.Equal("beta", process.TaskId);
        Assert.Equal(AgentRole.Resolver, process.Role);
    }

    [Fact]
    public void Id_that_is_not_a_task_is_dropped()
    {
        Assert.Empty(ProcessRules.Match(
            [Process(1, "claude -p --name orch:zeta"), Process(2, "claude -p --name orch:zeta:review")], Tasks, []));
    }

    [Fact]
    public void Name_without_the_orch_prefix_or_glued_to_another_word_is_not_a_name()
    {
        Assert.Empty(ProcessRules.Match(
            [Process(1, "claude -p --name alpha"), Process(2, "claude -p x--name orch:alpha")], Tasks, []));
    }

    [Fact]
    public void Command_line_without_name_and_resume_is_dropped()
    {
        Assert.Empty(ProcessRules.Match([Process(1, "claude -p --verbose"), Process(2, "")], Tasks, []));
    }

    [Fact]
    public void Name_and_suffix_are_matched_ignoring_case()
    {
        var process = Single("claude -p --NAME ORCH:alpha:REVIEW");

        Assert.Equal("alpha", process.TaskId);
        Assert.Equal(AgentRole.Reviewer, process.Role);
    }

    [Fact]
    public void Resume_id_of_a_session_gives_its_task_role_and_session_id()
    {
        var reviewer = Session("alpha/20261003-120000/attempt-1-review.json", "alpha", AgentRole.Reviewer,
            SessionState.Succeeded, SidA);

        var process = Single("claude -p --resume " + SidA, [reviewer]);

        Assert.Equal("alpha", process.TaskId);
        Assert.Equal(AgentRole.Reviewer, process.Role);
        Assert.Equal(SidA, process.SessionId);
    }

    [Fact]
    public void Resume_id_of_a_task_gives_its_worker()
    {
        var process = Single("""claude -p --resume="11111111-2222-4333-8444-555555555555" --verbose""");

        Assert.Equal("beta", process.TaskId);
        Assert.Equal(AgentRole.Worker, process.Role);
        Assert.Equal(SidB, process.SessionId);
    }

    [Fact]
    public void Resume_is_matched_ignoring_case()
    {
        var upper = SidA.ToUpperInvariant();
        var session = Session("alpha/20261003-120000/attempt-1-worker.json", "alpha", AgentRole.Worker,
            SessionState.Running, SidA);

        var process = Single("claude -p --RESUME " + upper, [session]);

        Assert.Equal("alpha", process.TaskId);
        Assert.Equal(AgentRole.Worker, process.Role);
        Assert.Equal(upper, process.SessionId);
    }

    [Fact]
    public void Unknown_resume_id_is_dropped()
    {
        Assert.Empty(ProcessRules.Match([Process(1, "claude -p --resume " + SidA)], Tasks, []));
    }

    [Fact]
    public void Name_wins_over_resume_and_the_resume_id_is_the_session_id()
    {
        var process = Single($"claude -p --resume {SidA} --name orch:beta");

        Assert.Equal("beta", process.TaskId);
        Assert.Equal(AgentRole.Worker, process.Role);
        Assert.Equal(SidA, process.SessionId);
    }

    [Fact]
    public void Session_id_without_resume_is_the_last_running_session_of_the_task_and_role()
    {
        ImmutableArray<Session> sessions =
        [
            Session("alpha/20261003-120000/attempt-1-worker.json", "alpha", AgentRole.Worker, SessionState.Running, "first"),
            Session("alpha/20261003-120000/attempt-2-worker.json", "alpha", AgentRole.Worker, SessionState.Running, "second"),
            Session("alpha/20261003-120000/attempt-3-worker.json", "alpha", AgentRole.Worker, SessionState.Failed, "failed"),
            Session("alpha/20261003-120000/attempt-2-review.json", "alpha", AgentRole.Reviewer, SessionState.Running, "review"),
            Session("beta/20261003-120000/attempt-1-worker.json", "beta", AgentRole.Worker, SessionState.Running, "beta"),
        ];

        var process = Single("claude -p --name orch:alpha", sessions);

        Assert.Equal("second", process.SessionId);
    }

    [Fact]
    public void Session_id_of_the_planner_is_its_running_session()
    {
        var planner = Session("planner-20261003-115500-1.json", null, AgentRole.Planner, SessionState.Running, SidA);

        Assert.Equal(SidA, Single("claude -p --name orch:planner", [planner]).SessionId);
    }

    [Fact]
    public void Session_id_is_null_without_a_running_session()
    {
        var done = Session("alpha/20261003-120000/attempt-1-worker.json", "alpha", AgentRole.Worker, SessionState.Succeeded, SidA);

        Assert.Null(Single("claude -p --name orch:alpha", [done]).SessionId);
    }

    [Fact]
    public void Kept_processes_keep_the_given_order_and_their_figures()
    {
        ImmutableArray<AgentProcess> processes =
        [
            Process(30, "claude -p --name orch:beta"),
            Process(10, "claude -p"),
            Process(20, "claude -p --name orch:planner"),
            Process(40, "claude -p --name orch:alpha:review"),
        ];

        var kept = ProcessRules.Match(processes, Tasks, []);

        Assert.Equal([30, 20, 40], kept.Select(p => p.Pid));
        Assert.Equal(processes[0] with { TaskId = "beta", Role = AgentRole.Worker }, kept[0]);
    }
}
