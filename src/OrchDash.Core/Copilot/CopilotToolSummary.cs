using System.Text.Json;

namespace OrchDash.Core.Copilot;

/// <summary>The tool summary rules of spec 4.3: <c>&lt;Name&gt; &lt;detail&gt;</c>.</summary>
internal static class CopilotToolSummary
{
    private const int MaxDetailLength = 100;
    private const string Ellipsis = "...";

    /// <param name="name">The tool name.</param>
    /// <param name="input">The tool's arguments; any kind, including <c>Undefined</c>.</param>
    /// <param name="intentionSummary">The tool request's <c>intentionSummary</c>, used for tools not in the table.</param>
    /// <param name="workDir">Paths under this folder are shown relative to it.</param>
    public static string Create(string name, JsonElement input, string? intentionSummary, string? workDir)
    {
        var detail = Detail(name, input, intentionSummary, workDir);
        if (detail.Length > MaxDetailLength)
            detail = string.Concat(detail.AsSpan(0, MaxDetailLength - Ellipsis.Length), Ellipsis);
        return detail.Length == 0 ? name : $"{name} {detail}";
    }

    private static string Detail(string name, JsonElement input, string? intentionSummary, string? workDir) => name switch
    {
        "Read" or "Edit" or "Write" or "MultiEdit" or "NotebookEdit" =>
            RelativePath(CopilotJson.String(input, "file_path"), workDir),
        "view" => RelativePath(CopilotJson.String(input, "path"), workDir),
        "Glob" or "glob" or "Grep" or "rg" => Search(input, workDir),
        "Bash" or "PowerShell" or "powershell" => FirstLine(CopilotJson.String(input, "command")),
        "StructuredOutput" => "reporting the result",
        "Task" or "Agent" => CopilotJson.String(input, "description") ?? "",
        _ => intentionSummary ?? "",
    };

    private static string Search(JsonElement input, string? workDir)
    {
        var pattern = CopilotJson.String(input, "pattern") ?? "";
        var path = CopilotJson.String(input, "path");
        return string.IsNullOrEmpty(path) ? pattern : $"{pattern} in {RelativePath(path, workDir)}";
    }

    private static string FirstLine(string? text)
    {
        if (text is null)
            return "";
        var end = text.IndexOf('\n');
        return (end < 0 ? text : text[..end]).TrimEnd('\r');
    }

    /// <summary>
    /// A path under <paramref name="workDir"/> relative to it with forward slashes; any other path as it is.
    /// Case and slash kind are ignored when comparing.
    /// </summary>
    private static string RelativePath(string? path, string? workDir)
    {
        if (path is null)
            return "";
        if (string.IsNullOrEmpty(workDir))
            return path;

        var root = workDir.Replace('\\', '/').TrimEnd('/');
        var normalized = path.Replace('\\', '/');
        if (root.Length == 0)
            return path;
        if (normalized.TrimEnd('/').Equals(root, StringComparison.OrdinalIgnoreCase))
            return ".";
        if (normalized.Length > root.Length + 1
            && normalized[root.Length] == '/'
            && normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return normalized[(root.Length + 1)..];
        return path;
    }
}
