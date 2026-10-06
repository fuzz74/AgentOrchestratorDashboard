using OrchDash.Core.Model;
using OrchDash.Core.Store;
using Xunit;
using static OrchDash.Core.Tests.Store.RunData;

namespace OrchDash.Core.Tests.Store;

// Spec 23.1-23.6: the git, process and command-log data of the insight sources on the snapshot.
public sealed class RunStoreInsightTests : IDisposable
{
    private const string AlphaTip = "5d6e7f8";

    private readonly TempRepo _repo = new();
    private readonly FakeParserFactory _factory = new();
    private readonly ManualClock _clock = new(At(12, 30, 0));
    private readonly FakeRunFolderReader _reader;
    private readonly FakeGitReader _git = new(() => Git());
    private readonly FakeProcessLister _processes = new(() => Processes());
    private readonly FakeCommandLogReader _commands = new(() => Commands());

    public RunStoreInsightTests()
    {
        _reader = new FakeRunFolderReader(() => Data(plan: PlanWithSetup(), tasks: [TaskOf("beta", 2), TaskOf("alpha")]));
    }

    public void Dispose() => _repo.Dispose();

    // The same content on every call, in new arrays and records.
    private static GitInfo Git(string tip = AlphaTip, DateTimeOffset? readAt = null, string? problem = null) =>
        new(readAt ?? At(12, 29, 55), @"C:\Work\Repo", "orch/integration", "3f9c2e1", "main", "a1b2c3d",
            [new GitWorktree(@"C:\Work\Repo.worktrees\alpha", "orch/task/alpha", tip)],
            [new GitBranch("orch/task/alpha", tip, At(12, 8, 40), GitBranchKind.Task)],
            [
                new GitTask("alpha", "orch/task/alpha", tip, @"C:\Work\Repo.worktrees\alpha", true,
                    [" M src/alpha/Parser.cs"],
                    new DiffStat([new DiffFile("src/alpha/Parser.cs", 2, 1)]),
                    new DiffStat([new DiffFile("src/alpha/Lexer.cs", 38, 0), new DiffFile("assets/logo.png", null, null)]),
                    [new GitCommit(tip + "0000", tip, At(12, 8, 40), GitCommitKind.Attempt, "orch(alpha): Title alpha", "Attempt 1.", 1, "alpha")],
                    null, ["orch/archive/alpha/20261003-121500000"],
                    "diff --git a/src/alpha/Lexer.cs b/src/alpha/Lexer.cs\n", "diff --git a/src/alpha/Parser.cs b/src/alpha/Parser.cs\n"),
                new GitTask("beta", null, null, @"C:\Work\Repo.worktrees\beta", false, [], null, null, [], null, [], null, null),
            ],
            problem);

    private static ProcessInfo Processes(double? cpuShare = 0.12, string? problem = null) =>
        new(At(12, 29, 58),
            [
                new AgentProcess(4242, "claude.exe", "claude -p --output-format stream-json --name orch:alpha", At(12, 10, 0),
                    367001600, cpuShare, null, null, null),
                new AgentProcess(4343, "claude.exe", "claude --help", At(12, 11, 0), 1000, null, null, null, null),
            ],
            problem);

    private static CommandLogData Commands(string text = "> dotnet restore\nRestored.", params string[] problems) =>
        new([new CommandLog(CommandKind.Setup, "alpha", "20261003-120000", null, "alpha/20261003-120000/setup.log",
                @"C:\Work\Repo\.orchestrator\logs\alpha\20261003-120000\setup.log",
                @"C:\Work\Repo\.orchestrator\logs\alpha\20261003-120000\setup.log.stderr",
                At(12, 0, 8), text.Length, text, "", null, CommandOutcome.Unknown, null)],
            [.. problems]);

    private static PlanInfo PlanWithSetup()
    {
        var plan = Plan();
        return plan with { Settings = plan.Settings.Add("setup", "\"dotnet restore\"") };
    }

    private RunStore Store(InsightSources? sources) =>
        new(_repo.RepoPath, _reader, _factory.Create, time: _clock, sources: sources);

    private RunStore Store() => Store(new InsightSources(_git, _processes, _commands));

    [Fact]
    public void Each_member_comes_from_its_source()
    {
        var git = Git();
        _git.Next = () => git;
        using var store = Store();

        store.Poll();

        var snapshot = store.Current;
        Assert.Same(git, snapshot.Git);

        Assert.Equal(At(12, 29, 58), snapshot.Processes.SampledAt);
        var process = Assert.Single(snapshot.Processes.Processes);   // ProcessRules.Match dropped the one without a name
        Assert.Equal(4242, process.Pid);
        Assert.Equal("alpha", process.TaskId);
        Assert.Equal(AgentRole.Worker, process.Role);

        var log = Assert.Single(snapshot.Commands);
        Assert.Equal("alpha/20261003-120000/setup.log", log.Key);
        Assert.Equal("dotnet restore", log.Command);   // CommandRules.Resolve read the plan setting
        Assert.Empty(snapshot.Problems);
    }

    [Fact]
    public void The_sources_get_the_repo_path_the_run_folder_the_poll_time_and_the_ordered_tasks()
    {
        using var store = Store();

        store.Poll();

        var snapshot = store.Current;
        Assert.Equal(_repo.RepoPath, _git.LastRepoPath);
        Assert.Same(snapshot.Plan, _git.LastPlan);
        Assert.Equal(["alpha", "beta"], _git.LastTasks.Select(t => t.Id));
        Assert.Equal(snapshot.Tasks, _git.LastTasks);
        Assert.Equal(At(12, 30, 0), _git.LastNow);
        Assert.Equal(At(12, 30, 0), _processes.LastNow);
        Assert.Equal(Path.Combine(_repo.RepoPath, ".orchestrator"), _commands.LastRunDir);
        Assert.Equal((1, 1, 1), (_git.Reads, _processes.Lists, _commands.Reads));
    }

    [Fact]
    public void Problem_lines_follow_the_store_lines_in_the_order_git_processes_commands()
    {
        _reader.Next = () => Data(plan: PlanWithSetup(), tasks: [TaskOf("alpha")], problems: ["state.json: in use"]);
        _git.Next = () => Git(problem: "git status --porcelain: fatal: bad index\ngit diff HEAD: timed out");
        _processes.Next = () => Processes(problem: "processes: access denied");
        _commands.Next = () => Commands(problems: ["alpha/20261003-120000/setup.log: in use", "logs: access denied"]);
        using var store = Store();

        store.Poll();

        Assert.Equal(
            [
                "state.json: in use",
                "git status --porcelain: fatal: bad index",
                "git diff HEAD: timed out",
                "processes: access denied",
                "alpha/20261003-120000/setup.log: in use",
                "logs: access denied",
            ],
            store.Current.Problems);
    }

    [Fact]
    public void Problem_lines_come_after_the_no_plan_line_and_empty_git_lines_are_skipped()
    {
        using var store = Store();
        store.Poll();

        _reader.Next = () => Data(problems: ["tasks.json: unexpected end of data"]);
        _git.Next = () => Git(problem: "git worktree list --porcelain: fatal: x\n\n");
        _commands.Next = () => Commands(problems: ["logs: access denied"]);
        store.Poll();

        var problems = store.Current.Problems;
        Assert.Equal(4, problems.Length);
        Assert.Equal("tasks.json: unexpected end of data", problems[0]);
        Assert.Contains("plan", problems[1], StringComparison.Ordinal);
        Assert.Equal("git worktree list --porcelain: fatal: x", problems[2]);
        Assert.Equal("logs: access denied", problems[3]);
    }

    [Theory]
    [InlineData("git")]
    [InlineData("processes")]
    [InlineData("commands")]
    public void A_source_that_throws_keeps_its_member_and_the_others_update(string failing)
    {
        using var store = Store();
        store.Poll();
        var before = store.Current;

        _git.Next = () => Git(tip: "9a8b7c6");
        _processes.Next = () => Processes(cpuShare: 0.5);
        _commands.Next = () => Commands(text: "> dotnet restore\nRestored again.");
        var exception = new InvalidOperationException(failing + " broke");
        switch (failing)
        {
            case "git": _git.ThrowOnce(exception); break;
            case "processes": _processes.ThrowOnce(exception); break;
            default: _commands.ThrowOnce(exception); break;
        }
        store.Poll();

        var after = store.Current;
        Assert.Equal(2, after.Version);
        Assert.Equal([failing + " broke"], after.Problems);
        if (failing == "git")
            Assert.Same(before.Git, after.Git);
        else
            Assert.Equal("9a8b7c6", after.Git.Tasks[0].Tip);
        if (failing == "processes")
            Assert.Same(before.Processes, after.Processes);
        else
            Assert.Equal(0.5, Assert.Single(after.Processes.Processes).CpuShare);
        if (failing == "commands")
            Assert.Same(before.Commands[0], after.Commands[0]);
        else
            Assert.Equal("> dotnet restore\nRestored again.", Assert.Single(after.Commands).Text);

        store.Poll();

        var recovered = store.Current;
        Assert.Equal(3, recovered.Version);
        Assert.Empty(recovered.Problems);
        Assert.Equal("9a8b7c6", recovered.Git.Tasks[0].Tip);
        Assert.Equal(0.5, Assert.Single(recovered.Processes.Processes).CpuShare);
        Assert.Equal("> dotnet restore\nRestored again.", Assert.Single(recovered.Commands).Text);
    }

    [Fact]
    public void Equal_content_in_new_arrays_and_records_keeps_the_version()
    {
        using var store = Store();
        store.Poll();
        var first = store.Current;

        store.Poll();
        store.Poll();

        Assert.Equal(3, _git.Reads);
        Assert.Same(first, store.Current);
        Assert.Equal(1, store.Current.Version);
    }

    [Theory]
    [InlineData("cpu-share")]
    [InlineData("command-text")]
    [InlineData("git-tip")]
    [InlineData("git-read-at")]
    public void A_changed_insight_raises_the_version_by_one(string change)
    {
        using var store = Store();
        store.Poll();
        store.Poll();

        switch (change)
        {
            case "cpu-share": _processes.Next = () => Processes(cpuShare: 0.13); break;
            case "command-text": _commands.Next = () => Commands(text: "> dotnet restore\nRestored!"); break;
            case "git-tip": _git.Next = () => Git(tip: "9a8b7c6"); break;
            default: _git.Next = () => Git(readAt: At(12, 30, 0)); break;
        }
        store.Poll();
        store.Poll();

        Assert.Equal(2, store.Current.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Without_sources_the_members_keep_their_defaults(bool none)
    {
        using var store = Store(none ? InsightSources.None : null);

        store.Poll();
        store.Poll();

        var snapshot = store.Current;
        Assert.Equal(1, snapshot.Version);
        Assert.Same(GitInfo.Empty, snapshot.Git);
        Assert.Same(ProcessInfo.Empty, snapshot.Processes);
        Assert.False(snapshot.Commands.IsDefault);
        Assert.Empty(snapshot.Commands);
        Assert.Empty(snapshot.Problems);
        Assert.Equal((0, 0, 0), (_git.Reads, _processes.Lists, _commands.Reads));
    }

    [Theory]
    [InlineData("git")]
    [InlineData("processes")]
    [InlineData("commands")]
    public void Only_the_set_source_is_called(string only)
    {
        using var store = Store(new InsightSources(
            only == "git" ? _git : null, only == "processes" ? _processes : null, only == "commands" ? _commands : null));

        store.Poll();

        var snapshot = store.Current;
        Assert.Equal(only == "git" ? 1 : 0, _git.Reads);
        Assert.Equal(only == "processes" ? 1 : 0, _processes.Lists);
        Assert.Equal(only == "commands" ? 1 : 0, _commands.Reads);
        Assert.Equal(only != "git", ReferenceEquals(GitInfo.Empty, snapshot.Git));
        Assert.Equal(only != "processes", ReferenceEquals(ProcessInfo.Empty, snapshot.Processes));
        Assert.Equal(only == "commands" ? 1 : 0, snapshot.Commands.Length);
    }

    [Fact]
    public async Task Dispose_disposes_the_disposable_sources_after_the_poll_thread_ended()
    {
        var store = new RunStore(_repo.RepoPath, _reader, _factory.Create, pollInterval: TimeSpan.FromMilliseconds(20),
            time: _clock, sources: new InsightSources(_git, _processes, _commands));
        store.Start();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.False(_git.Disposed);
        Assert.False(_commands.Disposed);

        store.Dispose();

        Assert.True(_git.Disposed);
        Assert.True(_commands.Disposed);
        var (gitReads, lists, commandReads) = (_git.Reads, _processes.Lists, _commands.Reads);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Equal(gitReads, _git.ReadsAtDispose);
        Assert.Equal(commandReads, _commands.ReadsAtDispose);
        Assert.Equal((gitReads, lists, commandReads), (_git.Reads, _processes.Lists, _commands.Reads));

        store.Dispose();

        Assert.Equal(1, _git.DisposeCount);
        Assert.Equal(1, _commands.DisposeCount);
    }
}
