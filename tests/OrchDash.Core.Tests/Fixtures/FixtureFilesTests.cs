using System.Text;
using System.Text.Json;
using Xunit;

namespace OrchDash.Core.Tests.Fixtures;

public sealed class FixtureFilesTests
{
    private const long MaxFixtureBytes = 4 * 1024 * 1024;
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

    [Fact]
    public void Every_manifest_file_is_in_the_test_output()
    {
        var missing = ClaudeFiles.Select(f => Path.Combine(FixturePaths.ClaudeRunDir, f))
            .Concat(CopilotFiles.Select(f => Path.Combine(FixturePaths.CopilotRunDir, f)))
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
    public void Fixture_folder_is_at_most_4_MB()
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
