using System.Collections.Immutable;
using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Store;

// Builders for reader results. Every call creates new arrays and dictionaries.
public static class StoreData
{
    public static DateTimeOffset At(int hour, int minute, int second) => new(2026, 10, 3, hour, minute, second, TimeSpan.Zero);

    public static string Claude(string text, DateTimeOffset? ts = null) =>
        ts is { } time ? $$"""{"session_id":"s1","text":"{{text}}","ts":"{{time:O}}"}""" : $$"""{"session_id":"s1","text":"{{text}}"}""";

    public static RunInfo Run(RunPhase phase = RunPhase.Running, int maxParallel = 2) =>
        new(phase, At(12, 0, 0), null, maxParallel, Provider.Claude, @"C:\bin\claude.exe", false);

    public static PlanInfo Plan(string model = "\"sonnet\"") =>
        new(".orchestrator/spec.md", "main", "orch/integration",
            new Dictionary<string, string> { ["maxAttempts"] = "3", ["model"] = model }.ToImmutableSortedDictionary(StringComparer.Ordinal));

    public static TaskView TaskOf(string id, int wave = 1, string[]? deps = null, string[]? owns = null,
        TaskState status = TaskState.Pending) =>
        new(id, "Title " + id, "Prompt " + id, [.. deps ?? []], [.. owns ?? ["src/" + id + "/**"]],
            "dotnet test", null, wave, 0, status, "fresh", 0, 0, 0, 0.0,
            null, null, null, null, null, null, null, null, "ready");

    public static ProgressEntry Progress(string message, int minute = 0) =>
        new(At(12, minute, 0), null, message, ProgressKind.Info);

    public static RunFolderData Data(RunInfo? run = null, PlanInfo? plan = null, IEnumerable<TaskView>? tasks = null,
        IEnumerable<ProgressEntry>? progress = null, IEnumerable<SessionFiles>? sessions = null,
        IEnumerable<string>? problems = null) =>
        new(run ?? Run(), plan, [.. tasks ?? []], [.. progress ?? []], [.. sessions ?? []], [.. problems ?? []]);

    public static string[] Texts(Session session) =>
        [.. session.Content.Items.OfType<AssistantText>().Select(t => t.Text)];
}
