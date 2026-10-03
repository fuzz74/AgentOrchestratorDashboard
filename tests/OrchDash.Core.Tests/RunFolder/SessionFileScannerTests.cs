using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;
using OrchDash.Core.Tests.Fixtures;
using Xunit;

namespace OrchDash.Core.Tests.RunFolder;

public sealed class SessionFileScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OrchDash.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _logs;
    private readonly List<string> _problems = [];

    public SessionFileScannerTests()
    {
        _logs = Path.Combine(_root, "logs");
        Directory.CreateDirectory(_logs);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("bootstrap-20261003-110028/attempt-2.json", null, AgentRole.Bootstrap, "bootstrap-20261003-110028", 2, 0)]
    [InlineData("planner-20261003-110111-3.json", null, AgentRole.Planner, null, 3, 0)]
    [InlineData("core/20261003-113444/attempt-1-worker.json", "core", AgentRole.Worker, "20261003-113444", 1, 0)]
    [InlineData("count/20261003-115046/attempt-2-resolver.json", "count", AgentRole.Resolver, "20261003-115046", 2, 0)]
    [InlineData("count/20261003-114955/attempt-3-review-12.json", "count", AgentRole.Reviewer, "20261003-114955", 3, 12)]
    public void Each_grammar_row_gives_a_session_with_its_values(
        string key, string? taskId, AgentRole role, string? startFolder, int attempt, int reviewTry)
    {
        CreateSession(key);

        var session = Assert.Single(Scan());

        var resultPath = Path.Combine(_logs, key.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(
            new SessionFiles(key, taskId, role, startFolder, attempt, reviewTry, false,
                resultPath, resultPath + ".prompt.md", resultPath + ".events.jsonl", resultPath + ".stderr",
                true, true, session.PromptWrittenAt),
            session);
        Assert.NotNull(session.PromptWrittenAt);
        Assert.Empty(_problems);
    }

    [Fact]
    public void A_nudge_is_its_own_session_with_the_values_of_its_base()
    {
        Touch("core/20261003-113444/attempt-2-review-1.json");
        Touch("core/20261003-113444/attempt-2-review-1.json.prompt.md");
        Touch("core/20261003-113444/attempt-2-review-1.json.nudge.json.prompt.md");
        Touch("core/20261003-113444/attempt-2-review-1.json.nudge.json.events.jsonl");

        var sessions = Scan();

        Assert.Equal(2, sessions.Count);
        var main = sessions[0];
        Assert.Equal("core/20261003-113444/attempt-2-review-1.json", main.Key);
        Assert.False(main.IsNudge);
        Assert.True(main.HasResultFile);
        Assert.False(main.HasEventsFile);

        var nudge = sessions[1];
        var resultPath = Path.Combine(_logs, "core", "20261003-113444", "attempt-2-review-1.json.nudge.json");
        Assert.Equal("core/20261003-113444/attempt-2-review-1.json.nudge.json", nudge.Key);
        Assert.True(nudge.IsNudge);
        Assert.Equal(("core", AgentRole.Reviewer, "20261003-113444", 2, 1),
            (nudge.TaskId, nudge.Role, nudge.StartFolder, nudge.Attempt, nudge.ReviewTry));
        Assert.Equal(resultPath, nudge.ResultPath);
        Assert.Equal(resultPath + ".prompt.md", nudge.PromptPath);
        Assert.Equal(resultPath + ".events.jsonl", nudge.EventsPath);
        Assert.Equal(resultPath + ".stderr", nudge.StderrPath);
        Assert.False(nudge.HasResultFile);
        Assert.True(nudge.HasEventsFile);
        Assert.NotNull(nudge.PromptWrittenAt);
    }

    [Fact]
    public void A_session_with_only_a_prompt_file_has_its_write_time()
    {
        var writtenAt = new DateTime(2026, 10, 3, 11, 34, 44, DateTimeKind.Local);
        var path = Touch("core/20261003-113444/attempt-1-worker.json.prompt.md");
        File.SetLastWriteTime(path, writtenAt);

        var session = Assert.Single(Scan());

        Assert.Equal("core/20261003-113444/attempt-1-worker.json", session.Key);
        Assert.False(session.HasResultFile);
        Assert.False(session.HasEventsFile);
        Assert.Equal(new DateTimeOffset(writtenAt), session.PromptWrittenAt);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(writtenAt), session.PromptWrittenAt!.Value.Offset);
    }

    [Fact]
    public void A_session_with_only_an_events_file_has_no_prompt_time()
    {
        Touch("bootstrap-20261003-110028/attempt-1.json.events.jsonl");

        var session = Assert.Single(Scan());

        Assert.Equal("bootstrap-20261003-110028/attempt-1.json", session.Key);
        Assert.False(session.HasResultFile);
        Assert.True(session.HasEventsFile);
        Assert.Null(session.PromptWrittenAt);
    }

    [Fact]
    public void A_session_with_only_a_stderr_file_exists()
    {
        Touch("planner-20261003-110111-1.json.stderr");

        var session = Assert.Single(Scan());

        Assert.Equal("planner-20261003-110111-1.json", session.Key);
        Assert.False(session.HasResultFile);
        Assert.False(session.HasEventsFile);
        Assert.Null(session.PromptWrittenAt);
    }

    [Fact]
    public void Command_output_and_unknown_files_create_no_session()
    {
        string[] files =
        [
            "core-integration-setup.log",
            "core-integration-setup.log.stderr",
            "core-integration-check.log",
            "notes.txt",
            "attempt-1.json",
            "attempt-1-worker.json",
            "planner-2026-1.json",
            "planner-20261003-110111-x.json",
            "bootstrap-20261003-110028/attempt-1-setup.log",
            "bootstrap-20261003-110028/attempt-1-setup.log.stderr",
            "bootstrap-20261003-110028/attempt-1-integration-check.log",
            "bootstrap-20261003-110028/attempt-1-worker.json",
            "bootstrap-20261003-110028/planner-20261003-110111-1.json",
            "bootstrap-20261003-110028/deeper/attempt-1.json",
            "bootstrap-2026/attempt-1.json",
            "core/attempt-1-worker.json",
            "core/latest/attempt-1-worker.json",
            "core/20261003-113444/setup.log",
            "core/20261003-113444/setup.log.stderr",
            "core/20261003-113444/attempt-1-acceptance.log",
            "core/20261003-113444/attempt-1-acceptance.log.stderr",
            "core/20261003-113444/attempt-1.json",
            "core/20261003-113444/attempt-1-review.json",
            "core/20261003-113444/attempt-1-builder.json",
            "core/20261003-113444/attempt-1-worker.json.txt",
            "core/20261003-113444/attempt-1-worker.json.prompt.md.stderr",
            "core/20261003-113444/attempt-1-worker.json.nudge.json.nudge.json",
            "core/20261003-113444/attempt-99999999999-worker.json",
            "core/20261003-113444/deeper/attempt-1-worker.json",
        ];
        foreach (var file in files)
            Touch(file);

        Assert.Empty(Scan());
        Assert.Empty(_problems);
    }

    [Fact]
    public void Sessions_are_ordered_by_key()
    {
        Touch("planner-20261003-110111-1.json");
        Touch("core/20261003-113444/attempt-1-worker.json");
        Touch("Zeta/20261003-113444/attempt-1-worker.json.events.jsonl");
        Touch("bootstrap-20261003-110028/attempt-1.json");
        Touch("core/20261003-113444/attempt-1-review-1.json");

        var keys = Scan().Select(s => s.Key).ToArray();

        Assert.Equal(
            [
                "Zeta/20261003-113444/attempt-1-worker.json",
                "bootstrap-20261003-110028/attempt-1.json",
                "core/20261003-113444/attempt-1-review-1.json",
                "core/20261003-113444/attempt-1-worker.json",
                "planner-20261003-110111-1.json",
            ],
            keys);
    }

    [Fact]
    public void The_task_id_is_the_folder_name_as_it_is()
    {
        Touch("Text.Kit-2 x/20261003-113444/attempt-1-worker.json");

        var session = Assert.Single(Scan());

        Assert.Equal("Text.Kit-2 x", session.TaskId);
        Assert.Equal("Text.Kit-2 x/20261003-113444/attempt-1-worker.json", session.Key);
    }

    [Fact]
    public void A_missing_logs_folder_gives_no_sessions_and_no_problem()
    {
        var sessions = SessionFileScanner.Scan(Path.Combine(_root, "missing", "logs"), _problems);

        Assert.Empty(sessions);
        Assert.Empty(_problems);
    }

    [Fact]
    public void A_logs_folder_that_cannot_be_listed_adds_one_problem()
    {
        Directory.Delete(_logs);
        File.WriteAllText(_logs, "");

        var sessions = SessionFileScanner.Scan(_logs, _problems);

        Assert.Empty(sessions);
        var problem = Assert.Single(_problems);
        Assert.StartsWith("logs: ", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Claude_fixture_has_three_sessions()
    {
        var logs = Path.Combine(FixturePaths.ClaudeRunDir, "logs");

        var sessions = SessionFileScanner.Scan(logs, _problems);

        Assert.Empty(_problems);
        Assert.Equal(
            [
                ("audio-synth/20261001-104634/attempt-1-review-1.json", "audio-synth", AgentRole.Reviewer, "20261001-104634", 1, 1),
                ("audio-synth/20261001-104634/attempt-1-worker.json", "audio-synth", AgentRole.Worker, "20261001-104634", 1, 0),
                ("bootstrap-20261001-100433/attempt-1.json", null, AgentRole.Bootstrap, "bootstrap-20261001-100433", 1, 0),
            ],
            sessions.Select(Values));
        AssertAllFilesPresent(logs, sessions);
    }

    [Fact]
    public void Copilot_fixture_has_ten_sessions()
    {
        var logs = Path.Combine(FixturePaths.CopilotRunDir, "logs");

        var sessions = SessionFileScanner.Scan(logs, _problems);

        Assert.Empty(_problems);
        Assert.Equal(
            [
                ("bootstrap-20261003-110028/attempt-1.json", null, AgentRole.Bootstrap, "bootstrap-20261003-110028", 1, 0),
                ("core/20261003-113444/attempt-1-review-1.json", "core", AgentRole.Reviewer, "20261003-113444", 1, 1),
                ("core/20261003-113444/attempt-1-worker.json", "core", AgentRole.Worker, "20261003-113444", 1, 0),
                ("count/20261003-114955/attempt-1-review-1.json", "count", AgentRole.Reviewer, "20261003-114955", 1, 1),
                ("count/20261003-114955/attempt-1-worker.json", "count", AgentRole.Worker, "20261003-114955", 1, 0),
                ("count/20261003-115046/attempt-1-resolver.json", "count", AgentRole.Resolver, "20261003-115046", 1, 0),
                ("count/20261003-115046/attempt-1-review-1.json", "count", AgentRole.Reviewer, "20261003-115046", 1, 1),
                ("count/20261003-115126/attempt-1-resolver.json", "count", AgentRole.Resolver, "20261003-115126", 1, 0),
                ("count/20261003-115126/attempt-1-review-1.json", "count", AgentRole.Reviewer, "20261003-115126", 1, 1),
                ("planner-20261003-110111-1.json", null, AgentRole.Planner, null, 1, 0),
            ],
            sessions.Select(Values));
        AssertAllFilesPresent(logs, sessions);
    }

    private static (string Key, string? TaskId, AgentRole Role, string? StartFolder, int Attempt, int ReviewTry) Values(
        SessionFiles s) => (s.Key, s.TaskId, s.Role, s.StartFolder, s.Attempt, s.ReviewTry);

    private static void AssertAllFilesPresent(string logs, IEnumerable<SessionFiles> sessions)
    {
        foreach (var session in sessions)
        {
            Assert.False(session.IsNudge);
            Assert.True(session.HasResultFile, session.Key);
            Assert.True(session.HasEventsFile, session.Key);
            Assert.NotNull(session.PromptWrittenAt);
            Assert.Equal(Path.Combine(logs, session.Key.Replace('/', Path.DirectorySeparatorChar)), session.ResultPath);
        }
    }

    private List<SessionFiles> Scan() => [.. SessionFileScanner.Scan(_logs, _problems)];

    private void CreateSession(string key)
    {
        Touch(key);
        Touch(key + ".prompt.md");
        Touch(key + ".events.jsonl");
        Touch(key + ".stderr");
    }

    private string Touch(string relativePath)
    {
        var path = Path.Combine(_logs, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }
}
