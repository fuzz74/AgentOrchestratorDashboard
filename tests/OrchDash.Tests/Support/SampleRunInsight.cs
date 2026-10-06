using System.Collections.Immutable;
using System.Text;
using OrchDash.Core.Model;

namespace OrchDash.Tests.Support;

// CreateEnriched() with the git state, the beta worker's process and the command logs of the run.
public static partial class SampleRun
{
    public const int BetaWorkerPid = 4242;

    private const string WorktreesDir = @"C:\Work\SampleRepo.worktrees\";
    private const string GammaArchiveBranch = "orch/archive/gamma/20261003-121500000";
    private const string SetupCommand = "dotnet restore Sample.slnx";
    private const string CheckCommand = "dotnet build Sample.slnx -warnaserror && dotnet test Sample.slnx --no-build";

    public static RunSnapshot CreateInsight()
    {
        var run = CreateEnriched();
        var betaSessionId = run.Sessions.Single(s => s.Files.Key == BetaWorkerKey).Content.SessionId;
        return run with
        {
            Git = SampleGit(),
            Processes = new ProcessInfo(At(12, 30, 0),
            [
                new AgentProcess(BetaWorkerPid, "claude.exe",
                    "claude -p --output-format stream-json --verbose --name orch:beta",
                    At(12, 10, 0), 367_001_600, 0.12, "beta", AgentRole.Worker, betaSessionId),
            ], null),
            Commands = SampleCommands(run.Tasks),
        };
    }

    private static GitInfo SampleGit() => new(
        ReadAt: At(12, 30, 0),
        RepoRoot: RepoPath,
        IntegrationBranch: "orch/integration", IntegrationTip: "3f9c2e1",
        BaseBranch: "main", BaseTip: "a1b2c3d",
        Worktrees:
        [
            new GitWorktree(WorktreesDir + "_integration", "orch/integration", "3f9c2e1"),
            new GitWorktree(WorktreesDir + "alpha", "orch/task/alpha", "5d6e7f8"),
            new GitWorktree(WorktreesDir + "beta", "orch/task/beta", "7a8b9c0"),
            new GitWorktree(WorktreesDir + "gamma", "orch/task/gamma", "b1c2d3e"),
        ],
        Branches:
        [
            new GitBranch("main", "a1b2c3d", At(11, 56, 10), GitBranchKind.Base),
            new GitBranch("orch/integration", "3f9c2e1", At(12, 9, 25), GitBranchKind.Integration),
            new GitBranch("orch/task/alpha", "5d6e7f8", At(12, 8, 40), GitBranchKind.Task),
            new GitBranch("orch/task/beta", "7a8b9c0", At(12, 10, 0), GitBranchKind.Task),
            new GitBranch("orch/task/gamma", "b1c2d3e", At(12, 18, 0), GitBranchKind.Task),
            new GitBranch(GammaArchiveBranch, "0a1b2c3", At(12, 14, 30), GitBranchKind.Archive),
        ],
        Tasks: [AlphaGit(), GammaGit(), BetaGit(), NoBranchGit("delta"), NoBranchGit("epsilon")],
        Problem: null);

    private static GitTask AlphaGit() => new(
        TaskId: "alpha", Branch: "orch/task/alpha", Tip: "5d6e7f8", WorktreePath: WorktreesDir + "alpha",
        WorktreeExists: true, UncommittedFiles: [],
        Uncommitted: new DiffStat([]),
        Committed: new DiffStat([new DiffFile("src/Alpha/Parser.cs", 38, 0), new DiffFile("tests/Alpha/ParserTests.cs", 22, 3)]),
        Commits:
        [
            Commit("5d6e7f8a9b0c1d2e3f405162738495a6b7c8d9e0", At(12, 8, 40), GitCommitKind.Attempt,
                "orch(alpha): Alpha parser", "Attempt 1.", 1, "alpha"),
        ],
        MergeCommit: Commit("3f9c2e14a7b8c9d0e1f2031425364758697a8b9c", At(12, 9, 25), GitCommitKind.Merge,
            "Merge task alpha: Alpha parser", "", null, "alpha"),
        ArchiveBranches: [],
        CommittedDiff:
            "diff --git a/src/Alpha/Parser.cs b/src/Alpha/Parser.cs\n" +
            "index 1c2d3e4..5d6e7f8 100644\n" +
            "--- a/src/Alpha/Parser.cs\n" +
            "+++ b/src/Alpha/Parser.cs\n" +
            "@@ -1,2 +1,7 @@\n" +
            " namespace Alpha;\n" +
            " \n" +
            "+public class Parser\n" +
            "+{\n" +
            "+    public static Node? Parse(string input) =>\n" +
            "+        input.Length == 0 ? null : new Node(input);\n" +
            "+}\n" +
            "diff --git a/tests/Alpha/ParserTests.cs b/tests/Alpha/ParserTests.cs\n" +
            "index 2e3f405..6a7b8c9 100644\n" +
            "--- a/tests/Alpha/ParserTests.cs\n" +
            "+++ b/tests/Alpha/ParserTests.cs\n" +
            "@@ -3,5 +3,6 @@ namespace Alpha.Tests;\n" +
            " public class ParserTests\n" +
            " {\n" +
            "-    [Fact(Skip = \"no parser yet\")]\n" +
            "+    [Fact]\n" +
            "     public void Empty_input_gives_null() =>\n" +
            "-        Assert.True(false);\n" +
            "+        Assert.Null(Parser.Parse(\"\"));\n" +
            " }\n",
        UncommittedDiff: "");

    private static GitTask GammaGit() => new(
        TaskId: "gamma", Branch: "orch/task/gamma", Tip: "b1c2d3e", WorktreePath: WorktreesDir + "gamma",
        WorktreeExists: true, UncommittedFiles: [],
        Uncommitted: new DiffStat([]),
        Committed: new DiffStat([new DiffFile("src/Gamma/Formatter.cs", 80, 0)]),
        Commits:
        [
            Commit("c4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f7", At(12, 3, 0), GitCommitKind.Attempt,
                "orch(gamma): Gamma formatter", "Attempt 1.", 1, "gamma"),
            Commit("d2e3f405162738495a6b7c8d9eafb0c1d2e3f405", At(12, 10, 0), GitCommitKind.Sync,
                "Merge branch 'orch/integration' into orch/task/gamma", "", null, "gamma"),
            Commit("e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90a1b", At(12, 11, 0), GitCommitKind.Attempt,
                "orch(gamma): Gamma formatter", "Attempt 2.", 2, "gamma"),
            Commit("b1c2d3e4f5061728394a5b6c7d8e9fa0b1c2d3e4", At(12, 18, 0), GitCommitKind.Attempt,
                "orch(gamma): Gamma formatter", "Attempt 3.", 3, "gamma"),
        ],
        MergeCommit: null,
        ArchiveBranches: [GammaArchiveBranch],
        CommittedDiff:
            "diff --git a/src/Gamma/Formatter.cs b/src/Gamma/Formatter.cs\n" +
            "index 4d5e6f7..b1c2d3e 100644\n" +
            "--- a/src/Gamma/Formatter.cs\n" +
            "+++ b/src/Gamma/Formatter.cs\n" +
            "@@ -1,2 +1,6 @@\n" +
            " namespace Gamma;\n" +
            " \n" +
            "+public static class Formatter\n" +
            "+{\n" +
            "+    public static string Format(string? input) => input ?? \"\";\n" +
            "+}\n",
        UncommittedDiff: "");

    private static GitTask BetaGit() => new(
        TaskId: "beta", Branch: "orch/task/beta", Tip: "7a8b9c0", WorktreePath: WorktreesDir + "beta",
        WorktreeExists: true, UncommittedFiles: [" M src/Beta/Parser.cs", "?? src/Beta/Lexer.cs"],
        Uncommitted: new DiffStat([new DiffFile("src/Beta/Parser.cs", 12, 3)]),
        Committed: new DiffStat([]),
        Commits: [],
        MergeCommit: null,
        ArchiveBranches: [],
        CommittedDiff: "",
        UncommittedDiff:
            "diff --git a/src/Beta/Parser.cs b/src/Beta/Parser.cs\n" +
            "index 7a8b9c0..8b9c0d1 100644\n" +
            "--- a/src/Beta/Parser.cs\n" +
            "+++ b/src/Beta/Parser.cs\n" +
            "@@ -1,5 +1,6 @@\n" +
            " namespace Beta;\n" +
            " \n" +
            "-public class Checker { }\n" +
            "+public class Checker\n" +
            "+{\n" +
            "+    public bool Check(string input) => Alpha.Parser.Parse(input) is not null;\n" +
            "+}\n");

    private static GitTask NoBranchGit(string taskId) => new(
        taskId, Branch: null, Tip: null, WorktreePath: WorktreesDir + taskId, WorktreeExists: false,
        UncommittedFiles: [], Uncommitted: null, Committed: null, Commits: [], MergeCommit: null,
        ArchiveBranches: [], CommittedDiff: null, UncommittedDiff: null);

    private static GitCommit Commit(string sha, DateTimeOffset time, GitCommitKind kind, string subject, string body,
        int? attempt, string taskId) =>
        new(sha, sha[..7], time, kind, subject, body, attempt, taskId);

    // Newest first, as CommandRules resolves them for this run.
    private static ImmutableArray<CommandLog> SampleCommands(ImmutableArray<TaskView> tasks)
    {
        var alphaAcceptance = tasks.Single(t => t.Id == "alpha").Acceptance!;
        var gammaAcceptance = tasks.Single(t => t.Id == "gamma").Acceptance!;
        const string start = "20261003-120005";
        const string betaStart = "20261003-121000";
        const string bootstrap = "bootstrap-20261003-115500";

        return
        [
            Log(CommandKind.Acceptance, "gamma", start, 3, $"gamma/{start}/attempt-3-acceptance.log", At(12, 19, 50),
                gammaAcceptance, CommandOutcome.Failed, 1, GammaTestText(failed: 1, passed: 5), "error: 1 test failed"),
            Log(CommandKind.Acceptance, "gamma", start, 2, $"gamma/{start}/attempt-2-acceptance.log", At(12, 12, 30),
                gammaAcceptance, CommandOutcome.Failed, 1, GammaTestText(failed: 2, passed: 4)),
            Log(CommandKind.Setup, "beta", betaStart, null, $"beta/{betaStart}/setup.log", At(12, 10, 5),
                SetupCommand, CommandOutcome.Passed, null, RestoreText("beta", "Beta")),
            Log(CommandKind.IntegrationCheck, "alpha", null, null, "alpha-integration-check.log", At(12, 9, 28),
                CheckCommand, CommandOutcome.Passed, null,
                "  Alpha -> C:\\Work\\SampleRepo.worktrees\\_integration\\src\\Alpha\\bin\\Debug\\net10.0\\Alpha.dll\n" +
                "Build succeeded.\n" +
                "    0 Warning(s)\n" +
                "    0 Error(s)\n" +
                "Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 44 ms - Alpha.Tests.dll (net10.0)"),
            Log(CommandKind.IntegrationSetup, "alpha", null, null, "alpha-integration-setup.log", At(12, 9, 26),
                SetupCommand, CommandOutcome.Passed, null, RestoreText("_integration", "Alpha")),
            Log(CommandKind.Acceptance, "alpha", start, 1, $"alpha/{start}/attempt-1-acceptance.log", At(12, 8, 50),
                alphaAcceptance, CommandOutcome.Passed, null,
                "  Determining projects to restore...\n" +
                "  All projects are up-to-date for restore.\n" +
                "  Alpha.Tests -> C:\\Work\\SampleRepo.worktrees\\alpha\\tests\\Alpha\\bin\\Debug\\net10.0\\Alpha.Tests.dll\n" +
                "Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 41 ms - Alpha.Tests.dll (net10.0)"),
            Log(CommandKind.Acceptance, "gamma", start, 1, $"gamma/{start}/attempt-1-acceptance.log", At(12, 4, 30),
                gammaAcceptance, CommandOutcome.Failed, 1, GammaTestText(failed: 2, passed: 4)),
            Log(CommandKind.Setup, "gamma", start, null, $"gamma/{start}/setup.log", At(12, 0, 10),
                SetupCommand, CommandOutcome.Passed, null, RestoreText("gamma", "Gamma")),
            Log(CommandKind.Setup, "alpha", start, null, $"alpha/{start}/setup.log", At(12, 0, 8),
                SetupCommand, CommandOutcome.Passed, null, RestoreText("alpha", "Alpha")),
            Log(CommandKind.BootstrapCheck, null, bootstrap, 1, $"{bootstrap}/attempt-1-integration-check.log", At(11, 56, 0),
                CheckCommand, CommandOutcome.Passed, null,
                "> " + CheckCommand + "\n" +
                "Build succeeded.\n" +
                "    0 Warning(s)\n" +
                "    0 Error(s)\n" +
                "No test is available in C:\\Work\\SampleRepo\\tests\\Sample.Tests\\bin\\Debug\\net10.0\\Sample.Tests.dll."),
            Log(CommandKind.BootstrapSetup, null, bootstrap, 1, $"{bootstrap}/attempt-1-setup.log", At(11, 55, 30),
                SetupCommand, CommandOutcome.Passed, null,
                "> " + SetupCommand + "\n" +
                "  Determining projects to restore...\n" +
                "  Restored C:\\Work\\SampleRepo\\src\\Sample\\Sample.csproj (in 380 ms).\n" +
                "  Restored C:\\Work\\SampleRepo\\tests\\Sample.Tests\\Sample.Tests.csproj (in 512 ms)."),
        ];
    }

    private static string RestoreText(string worktree, string project) =>
        "  Determining projects to restore...\n" +
        $"  Restored C:\\Work\\SampleRepo.worktrees\\{worktree}\\src\\{project}\\{project}.csproj (in 402 ms).\n" +
        $"  Restored C:\\Work\\SampleRepo.worktrees\\{worktree}\\tests\\{project}\\{project}.Tests.csproj (in 517 ms).";

    private static string GammaTestText(int failed, int passed) =>
        "  Determining projects to restore...\n" +
        "  All projects are up-to-date for restore.\n" +
        "  Gamma.Tests -> C:\\Work\\SampleRepo.worktrees\\gamma\\tests\\Gamma\\bin\\Debug\\net10.0\\Gamma.Tests.dll\n" +
        "  Failed GammaTests.Formats_empty_input [12 ms]\n" +
        "  Error Message: Assert.Equal() Failure: expected \"\" but was null\n" +
        $"Failed!  - Failed:     {failed}, Passed:     {passed}, Skipped:     0, Total:     6, Duration: 85 ms - Gamma.Tests.dll (net10.0)";

    private static CommandLog Log(CommandKind kind, string? taskId, string? startFolder, int? attempt, string key,
        DateTimeOffset writtenAt, string command, CommandOutcome outcome, int? exitCode, string text, string stderrText = "")
    {
        var path = LogsDir + key.Replace('/', '\\');
        return new CommandLog(kind, taskId, startFolder, attempt, key, path, path + ".stderr", writtenAt,
            Encoding.UTF8.GetByteCount(text), text, stderrText, command, outcome, exitCode);
    }
}
