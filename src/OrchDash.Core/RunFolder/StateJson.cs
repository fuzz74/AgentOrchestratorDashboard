using System.Text.Json;
using OrchDash.Core.Model;

namespace OrchDash.Core.RunFolder;

/// <summary>Reads the task states from the text of <c>state.json</c> (spec 2.2, 4.3).</summary>
internal static class StateJson
{
    public const string FileName = "state.json";

    // Task id -> state. Null, with one problem line, when the text is not a JSON object.
    public static Dictionary<string, TaskStateEntry>? Parse(string text, ICollection<string> problems)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{FileName}: the content is not a JSON object");
                return null;
            }

            var states = new Dictionary<string, TaskStateEntry>(StringComparer.Ordinal);
            if (JsonValues.Property(root, "tasks") is { ValueKind: JsonValueKind.Object } tasks)
            {
                foreach (var task in tasks.EnumerateObject())
                    states[task.Name] = ReadEntry(task.Value);
            }
            return states;
        }
        catch (JsonException e)
        {
            problems.Add($"{FileName}: {e.Message}");
            return null;
        }
    }

    private static TaskStateEntry ReadEntry(JsonElement entry) => new(
        ReadStatus(JsonValues.String(entry, "status")),
        JsonValues.String(entry, "mode") ?? TaskStateEntry.DefaultMode,
        JsonValues.Int(entry, "attempts"),
        JsonValues.Int(entry, "syncRuns"),
        JsonValues.Int(entry, "specRejections"),
        JsonValues.Double(entry, "costUsd"),
        JsonValues.String(entry, "sessionId"),
        JsonValues.String(entry, "summary"),
        JsonValues.String(entry, "notes"),
        JsonValues.String(entry, "error"),
        JsonValues.String(entry, "feedback"),
        JsonValues.Time(entry, "startedAt"),
        JsonValues.Time(entry, "finishedAt"),
        JsonValues.String(entry, "mergedSha"));

    private static TaskState ReadStatus(string? status) => status switch
    {
        "running" => TaskState.Running,
        "done" => TaskState.Done,
        "failed" => TaskState.Failed,
        _ => TaskState.Pending,
    };
}
