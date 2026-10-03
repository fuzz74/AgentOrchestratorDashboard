using System.Collections.Immutable;
using System.Text.Json;

namespace OrchDash.Core.Model;

public static class StructuredOutput   // never throws; (null, null) when json is not an object of either shape
{   // worker: {status, summary, notes_for_dependents, blocked_reason}
    // review: {spec_verdict, quality_verdict, summary, issues: [{severity, file, description}]}
    public static (WorkerReport? Worker, ReviewVerdict? Review) Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (null, null);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, null);

            if (GetString(root, "spec_verdict") is not null || GetString(root, "quality_verdict") is not null)
                return (null, ParseReview(root));

            if (GetString(root, "status") is { } status)
            {
                var worker = new WorkerReport(
                    status,
                    GetString(root, "summary") ?? "",
                    GetString(root, "notes_for_dependents"),
                    GetString(root, "blocked_reason"));
                return (worker, null);
            }

            return (null, null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static ReviewVerdict ParseReview(JsonElement root)
    {
        var issues = ImmutableArray.CreateBuilder<ReviewIssue>();
        if (root.TryGetProperty("issues", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var issue in array.EnumerateArray())
            {
                if (issue.ValueKind != JsonValueKind.Object)
                    continue;
                issues.Add(new ReviewIssue(
                    GetString(issue, "severity") ?? "",
                    GetString(issue, "file"),
                    GetString(issue, "description") ?? ""));
            }
        }

        return new ReviewVerdict(
            GetString(root, "spec_verdict") ?? "",
            GetString(root, "quality_verdict") ?? "",
            GetString(root, "summary") ?? "",
            issues.ToImmutable());
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
