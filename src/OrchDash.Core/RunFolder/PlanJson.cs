using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using OrchDash.Core.Model;

namespace OrchDash.Core.RunFolder;

/// <summary>Reads the plan and the task list from the text of <c>tasks.json</c> (spec 2.1, 4.3).</summary>
internal static class PlanJson
{
    public const string FileName = "tasks.json";
    public const string DefaultIntegrationBranch = "orch/integration";

    // Null, with one problem line, when the text is not a JSON object. A task without a string id is
    // skipped with one problem line.
    public static (PlanInfo Plan, ImmutableArray<TaskDefinition> Tasks)? Parse(string text, ICollection<string> problems)
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

            var plan = new PlanInfo(
                JsonValues.String(root, "spec"),
                JsonValues.String(root, "baseBranch"),
                JsonValues.String(root, "integrationBranch") ?? DefaultIntegrationBranch,
                ReadSettings(root));
            return (plan, ReadTasks(root, problems));
        }
        catch (JsonException e)
        {
            problems.Add($"{FileName}: {e.Message}");
            return null;
        }
    }

    private static ImmutableSortedDictionary<string, string> ReadSettings(JsonElement root)
    {
        var settings = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        if (JsonValues.Property(root, "settings") is { ValueKind: JsonValueKind.Object } values)
        {
            foreach (var setting in values.EnumerateObject())
                settings[setting.Name] = JsonValues.Compact(setting.Value);
        }
        return settings.ToImmutable();
    }

    private static ImmutableArray<TaskDefinition> ReadTasks(JsonElement root, ICollection<string> problems)
    {
        if (JsonValues.Property(root, "tasks") is not { ValueKind: JsonValueKind.Array } items)
            return [];

        var tasks = ImmutableArray.CreateBuilder<TaskDefinition>();
        var position = 0;
        foreach (var item in items.EnumerateArray())
        {
            position++;
            if (JsonValues.String(item, "id") is not { } id)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{FileName}: task {position} has no id and is skipped"));
                continue;
            }

            tasks.Add(new TaskDefinition(
                id,
                JsonValues.String(item, "title") ?? "",
                JsonValues.String(item, "prompt") ?? "",
                JsonValues.Strings(item, "deps"),
                JsonValues.Strings(item, "owns"),
                JsonValues.String(item, "acceptance"),
                JsonValues.String(item, "model")));
        }
        return tasks.ToImmutable();
    }
}
