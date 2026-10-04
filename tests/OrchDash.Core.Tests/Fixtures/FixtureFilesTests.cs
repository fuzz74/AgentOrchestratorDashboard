using System.Text;
using System.Text.Json;
using Xunit;

namespace OrchDash.Core.Tests.Fixtures;

public sealed class FixtureFilesTests
{
    private const long MaxFixtureBytes = 6 * 1024 * 1024;
    private const int MaxEphemeralLinesPerType = 5;
    private const string EphemeralMarker = "\"ephemeral\":true";

    // Relative to FixturePaths.ClaudeRunDir; the manifest in spec section 4.3.
    private static readonly string[] ClaudeFiles =
    [
        "tasks.json",
        "state.json",
        "progress.md",
        "project.json",
        "logs/bootstrap-20261001-100433/attempt-1-integration-check.log",
        "logs/bootstrap-20261001-100433/attempt-1-setup.log",
        "logs/bootstrap-20261001-100433/attempt-1.json",
        "logs/bootstrap-20261001-100433/attempt-1.json.events.jsonl",
        "logs/bootstrap-20261001-100433/attempt-1.json.prompt.md",
        "logs/bootstrap-20261001-100433/attempt-1.json.stderr",
        "logs/audio-synth/20261001-104634/attempt-1-acceptance.log",
        "logs/audio-synth/20261001-104634/attempt-1-review-1.json",
        "logs/audio-synth/20261001-104634/attempt-1-review-1.json.events.jsonl",
        "logs/audio-synth/20261001-104634/attempt-1-review-1.json.prompt.md",
        "logs/audio-synth/20261001-104634/attempt-1-review-1.json.stderr",
        "logs/audio-synth/20261001-104634/attempt-1-worker.json",
        "logs/audio-synth/20261001-104634/attempt-1-worker.json.events.jsonl",
        "logs/audio-synth/20261001-104634/attempt-1-worker.json.prompt.md",
        "logs/audio-synth/20261001-104634/attempt-1-worker.json.stderr",
        "logs/audio-synth/20261001-104634/setup.log",
        "logs/audio-synth-integration-setup.log",
        "logs/audio-synth-integration-check.log",
    ];

    // Relative to FixturePaths.CopilotRunDir; the manifest in spec section 4.3.
    private static readonly string[] CopilotFiles =
    [
        "tasks.json",
        "state.json",
        "progress.md",
        "project.json",
        "run.lock",
        "logs/bootstrap-20261003-110028/attempt-1-integration-check.log",
        "logs/bootstrap-20261003-110028/attempt-1-integration-check.log.stderr",
        "logs/bootstrap-20261003-110028/attempt-1-setup.log",
        "logs/bootstrap-20261003-110028/attempt-1-setup.log.stderr",
        "logs/bootstrap-20261003-110028/attempt-1.json",
        "logs/bootstrap-20261003-110028/attempt-1.json.events.jsonl",
        "logs/bootstrap-20261003-110028/attempt-1.json.prompt.md",
        "logs/bootstrap-20261003-110028/attempt-1.json.stderr",
        "logs/planner-20261003-110111-1.json",
        "logs/planner-20261003-110111-1.json.events.jsonl",
        "logs/planner-20261003-110111-1.json.prompt.md",
        "logs/planner-20261003-110111-1.json.stderr",
        "logs/core/20261003-113444/attempt-1-acceptance.log",
        "logs/core/20261003-113444/attempt-1-acceptance.log.stderr",
        "logs/core/20261003-113444/attempt-1-review-1.json",
        "logs/core/20261003-113444/attempt-1-review-1.json.events.jsonl",
        "logs/core/20261003-113444/attempt-1-review-1.json.prompt.md",
        "logs/core/20261003-113444/attempt-1-review-1.json.stderr",
        "logs/core/20261003-113444/attempt-1-worker.json",
        "logs/core/20261003-113444/attempt-1-worker.json.events.jsonl",
        "logs/core/20261003-113444/attempt-1-worker.json.prompt.md",
        "logs/core/20261003-113444/attempt-1-worker.json.stderr",
        "logs/core/20261003-113444/setup.log",
        "logs/core/20261003-113444/setup.log.stderr",
        "logs/count/20261003-114955/attempt-1-acceptance.log",
        "logs/count/20261003-114955/attempt-1-acceptance.log.stderr",
        "logs/count/20261003-114955/attempt-1-review-1.json",
        "logs/count/20261003-114955/attempt-1-review-1.json.events.jsonl",
        "logs/count/20261003-114955/attempt-1-review-1.json.prompt.md",
        "logs/count/20261003-114955/attempt-1-review-1.json.stderr",
        "logs/count/20261003-114955/attempt-1-worker.json",
        "logs/count/20261003-114955/attempt-1-worker.json.events.jsonl",
        "logs/count/20261003-114955/attempt-1-worker.json.prompt.md",
        "logs/count/20261003-114955/attempt-1-worker.json.stderr",
        "logs/count/20261003-115046/attempt-1-acceptance.log",
        "logs/count/20261003-115046/attempt-1-acceptance.log.stderr",
        "logs/count/20261003-115046/attempt-1-resolver.json",
        "logs/count/20261003-115046/attempt-1-resolver.json.events.jsonl",
        "logs/count/20261003-115046/attempt-1-resolver.json.prompt.md",
        "logs/count/20261003-115046/attempt-1-resolver.json.stderr",
        "logs/count/20261003-115046/attempt-1-review-1.json",
        "logs/count/20261003-115046/attempt-1-review-1.json.events.jsonl",
        "logs/count/20261003-115046/attempt-1-review-1.json.prompt.md",
        "logs/count/20261003-115046/attempt-1-review-1.json.stderr",
        "logs/count/20261003-115126/attempt-1-acceptance.log",
        "logs/count/20261003-115126/attempt-1-acceptance.log.stderr",
        "logs/count/20261003-115126/attempt-1-resolver.json",
        "logs/count/20261003-115126/attempt-1-resolver.json.events.jsonl",
        "logs/count/20261003-115126/attempt-1-resolver.json.prompt.md",
        "logs/count/20261003-115126/attempt-1-resolver.json.stderr",
        "logs/count/20261003-115126/attempt-1-review-1.json",
        "logs/count/20261003-115126/attempt-1-review-1.json.events.jsonl",
        "logs/count/20261003-115126/attempt-1-review-1.json.prompt.md",
        "logs/count/20261003-115126/attempt-1-review-1.json.stderr",
        "logs/core-integration-check.log",
        "logs/core-integration-check.log.stderr",
        "logs/core-integration-setup.log",
        "logs/core-integration-setup.log.stderr",
        "logs/count-integration-check.log",
        "logs/count-integration-check.log.stderr",
        "logs/count-integration-setup.log",
        "logs/count-integration-setup.log.stderr",
    ];

    // Folder of the provider-store copies; holds FixturePaths.ClaudeStore and FixturePaths.CopilotStore.
    private static readonly string StoresDir = Path.Combine(FixturePaths.Root, "stores");

    // Relative to StoresDir; the fixture manifest in spec section 4.3.
    private static readonly string[] StoreFiles =
    [
        "claude/projects/C--Data-AI-AnsiDemo/60e2b369-7dd9-4eeb-b389-cdadd402e942.jsonl",
        "claude/projects/C--Data-AI-AnsiDemo-worktrees-audio-synth/66a6a33c-01ca-42bf-85bb-4eec9505a991.jsonl",
        "claude/projects/C--Data-AI-AnsiDemo-worktrees-audio-synth/210c86fa-485b-48c1-8808-6dac62e28c71.jsonl",
        "copilot/session-state/1ebef052-3d86-4b0f-abd5-111868a6de34/workspace.yaml",
        "copilot/session-state/1ebef052-3d86-4b0f-abd5-111868a6de34/events.jsonl",
        "copilot/session-state/483087e5-0b46-4aa4-ad51-a9cb81de2f9d/workspace.yaml",
        "copilot/session-state/483087e5-0b46-4aa4-ad51-a9cb81de2f9d/events.jsonl",
        "copilot/session-state/632962e6-b77b-473f-8159-fa68fc99acba/workspace.yaml",
        "copilot/session-state/632962e6-b77b-473f-8159-fa68fc99acba/events.jsonl",
        "copilot/session-state/905a692e-5150-4700-8ea7-ac94558036db/workspace.yaml",
        "copilot/session-state/905a692e-5150-4700-8ea7-ac94558036db/events.jsonl",
        "copilot/session-state/93a2aa4b-22ca-410a-bef8-d28ca63c86ae/workspace.yaml",
        "copilot/session-state/93a2aa4b-22ca-410a-bef8-d28ca63c86ae/events.jsonl",
        "copilot/session-state/9db7bfa4-3063-4400-93f0-97f8a68e0d91/workspace.yaml",
        "copilot/session-state/9db7bfa4-3063-4400-93f0-97f8a68e0d91/events.jsonl",
        "copilot/session-state/ae783abf-0989-4988-88c1-089deac14062/workspace.yaml",
        "copilot/session-state/ae783abf-0989-4988-88c1-089deac14062/events.jsonl",
        "copilot/session-state/c61fb851-7815-4f6a-9f37-9e82a52e3ece/workspace.yaml",
        "copilot/session-state/c61fb851-7815-4f6a-9f37-9e82a52e3ece/events.jsonl",
        "copilot/session-state/d92e413e-38cc-401c-8118-611e46e77210/workspace.yaml",
        "copilot/session-state/d92e413e-38cc-401c-8118-611e46e77210/events.jsonl",
        "copilot/session-state/f7dfd185-96f7-4770-99c9-5d660aba6c5c/workspace.yaml",
        "copilot/session-state/f7dfd185-96f7-4770-99c9-5d660aba6c5c/events.jsonl",
        "copilot/session-store.db",
    ];

    [Fact]
    public void Every_manifest_file_is_in_the_test_output()
    {
        var missing = ClaudeFiles.Select(f => Path.Combine(FixturePaths.ClaudeRunDir, f))
            .Concat(CopilotFiles.Select(f => Path.Combine(FixturePaths.CopilotRunDir, f)))
            .Concat(StoreFiles.Select(f => Path.Combine(StoresDir, f)))
            .Where(path => !File.Exists(path))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Run_folders_hold_only_manifest_files()
    {
        Assert.Equal(ClaudeFiles.Order(StringComparer.Ordinal), FilesUnder(FixturePaths.ClaudeRunDir));
        Assert.Equal(CopilotFiles.Order(StringComparer.Ordinal), FilesUnder(FixturePaths.CopilotRunDir));
    }

    [Fact]
    public void Stores_folder_holds_only_manifest_files()
    {
        Assert.Equal(StoreFiles.Order(StringComparer.Ordinal), FilesUnder(StoresDir));
    }

    [Fact]
    public void Store_paths_are_inside_the_stores_folder()
    {
        Assert.Equal(Path.Combine(StoresDir, "claude"), FixturePaths.ClaudeStore);
        Assert.Equal(Path.Combine(StoresDir, "copilot"), FixturePaths.CopilotStore);
        Assert.True(Directory.Exists(Path.Combine(FixturePaths.ClaudeStore, "projects")));
        Assert.True(Directory.Exists(Path.Combine(FixturePaths.CopilotStore, "session-state")));
    }

    [Fact]
    public void Fixture_folder_is_at_most_6_MB()
    {
        var total = new DirectoryInfo(FixturePaths.Root)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);

        Assert.InRange(total, 1, MaxFixtureBytes);
    }

    [Fact]
    public void Copilot_events_keep_at_most_5_ephemeral_lines_per_type()
    {
        var eventFiles = CopilotFiles.Where(f => f.EndsWith(".events.jsonl", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(eventFiles);

        foreach (var file in eventFiles)
        {
            var counts = File.ReadLines(Path.Combine(FixturePaths.CopilotRunDir, file), Encoding.UTF8)
                .Where(line => line.Contains(EphemeralMarker, StringComparison.Ordinal))
                .GroupBy(TopLevelType)
                .ToDictionary(g => g.Key, g => g.Count());

            Assert.All(counts, pair => Assert.True(
                pair.Value <= MaxEphemeralLinesPerType,
                $"{file}: {pair.Value} ephemeral lines of type {pair.Key}"));
        }
    }

    [Theory]
    [InlineData(".gitattributes", "* -text")]
    [InlineData(".gitignore", "!*")]
    public void Git_files_hold_the_required_line(string name, string line)
    {
        var lines = File.ReadAllLines(Path.Combine(FixturePaths.Root, name));

        Assert.Equal([line], lines);
    }

    private static List<string> FilesUnder(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(dir, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string TopLevelType(string line)
    {
        using var doc = JsonDocument.Parse(line);
        return doc.RootElement.GetProperty("type").GetString() ?? "";
    }
}
