using System.Text.Json;
using OrchDash.Core.Model;
using OrchDash.Core.SessionFolder;
using OrchDash.Core.Tests.Fixtures;
using Xunit;

namespace OrchDash.Core.Tests.SessionFolder;

/// <summary>The folder store on the copied session folders of the Copilot run (tests/Fixtures/stores/copilot).</summary>
public sealed class CopilotFolderFixtureTests
{
    private const string CoreWorkerId = "ae783abf-0989-4988-88c1-089deac14062";

    /// <summary>The core worker's workDir with other slashes, other case and a trailing slash.</summary>
    private const string CoreWorkDir = "c:/data/ai/TextKit.worktrees/core/";

    private static readonly DateTimeOffset CoreStartedAt = new(2026, 10, 3, 9, 34, 50, TimeSpan.Zero);

    private static string SessionStateDir => Path.Combine(FixturePaths.CopilotStore, "session-state");

    public static TheoryData<string> SessionIds() => new(FolderNames());

    private static IEnumerable<string> FolderNames() =>
        Directory.GetDirectories(SessionStateDir).Select(folder => Path.GetFileName(folder)).Order(StringComparer.Ordinal);

    [Fact]
    public void The_fixture_has_ten_session_folders()
    {
        Assert.Equal(10, Directory.GetDirectories(SessionStateDir).Length);
    }

    [Theory]
    [MemberData(nameof(SessionIds))]
    public void Every_session_folder_gives_the_version_and_two_system_prompt_blocks(string sessionId)
    {
        var data = new CopilotFolderStore(SessionStateDir).Read(sessionId, null);

        Assert.NotNull(data);
        Assert.Equal(TestedVersions.CopilotCli, data.CliVersion);
        Assert.Equal(2, data.SystemPrompt.Length);
        Assert.All(data.SystemPrompt, block => Assert.NotEmpty(block));
        Assert.StartsWith("You are the GitHub Copilot CLI", data.SystemPrompt[0], StringComparison.Ordinal);
        Assert.Equal(0, data.UnparsedLines);
        Assert.Empty(data.Tools);
        Assert.Empty(data.Injected);
        Assert.Empty(data.Calls);
        Assert.Null(data.CostUsd);
        Assert.Null(data.LinesAdded);
        Assert.Null(data.LinesRemoved);
    }

    [Fact]
    public void Every_log_of_the_copilot_run_has_a_session_folder()
    {
        var store = new CopilotFolderStore(SessionStateDir);
        var logs = Directory.GetFiles(Path.Combine(FixturePaths.CopilotRunDir, "logs"), "*.events.jsonl",
            SearchOption.AllDirectories);

        var ids = logs.Select(LastLineSessionId).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(10, logs.Length);
        Assert.Equal(10, ids.Count);
        Assert.All(ids, id => Assert.NotNull(store.Read(id, null)));
        Assert.Equal(FolderNames(), ids.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Find_gives_the_core_worker_by_name_folder_and_time()
    {
        var store = new CopilotFolderStore(SessionStateDir);

        Assert.Equal(CoreWorkerId, store.Find("orch:core", CoreWorkDir, CoreStartedAt, new HashSet<string>()));
    }

    [Fact]
    public void Find_gives_null_when_the_core_worker_id_is_known()
    {
        var store = new CopilotFolderStore(SessionStateDir);

        Assert.Null(store.Find("orch:core", CoreWorkDir, CoreStartedAt, new HashSet<string> { CoreWorkerId }));
    }

    [Theory]
    [InlineData("2026-10-03T09:35:20Z")]   // 31.88 s after created_at 09:34:48.120
    [InlineData("2026-10-03T09:34:18Z")]   // 30.12 s before
    public void Find_gives_null_when_the_start_is_more_than_30_seconds_away(string startedAt)
    {
        var store = new CopilotFolderStore(SessionStateDir);

        Assert.Null(store.Find("orch:core", CoreWorkDir, DateTimeOffset.Parse(startedAt), new HashSet<string>()));
    }

    [Theory]
    [InlineData("orch:core:review")]
    [InlineData("orch:count")]
    [InlineData("orch:Core")]
    public void Find_gives_null_for_another_name(string name)
    {
        var store = new CopilotFolderStore(SessionStateDir);

        Assert.Null(store.Find(name, CoreWorkDir, CoreStartedAt, new HashSet<string>()));
    }

    private static string LastLineSessionId(string logPath)
    {
        var lastLine = File.ReadLines(logPath).Last(line => line.Length > 0);
        using var document = JsonDocument.Parse(lastLine);
        return document.RootElement.GetProperty("sessionId").GetString()!;
    }
}
