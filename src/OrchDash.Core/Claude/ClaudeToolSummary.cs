using System.Text.Json;

namespace OrchDash.Core.Claude;

/// <summary>The tool summary rules of spec section 4.3: <c>&lt;Name&gt; &lt;detail&gt;</c>.</summary>
internal static class ClaudeToolSummary
{
    private const int MaxDetailLength = 100;
    private const string Ellipsis = "...";

    /// <param name="name">The tool name.</param>
    /// <param name="input">The tool input; any JSON value.</param>
    /// <param name="baseDir">Paths under this folder are shown relative to it; null keeps paths as they are.</param>
    public static string Create(string name, JsonElement input, string? baseDir)
    {
        var detail = Detail(name, input, baseDir);
        if (detail.Length > MaxDetailLength)
            detail = string.Concat(detail.AsSpan(0, MaxDetailLength - Ellipsis.Length), Ellipsis);

        return detail.Length == 0 ? name : $"{name} {detail}";
    }

    private static string Detail(string name, JsonElement input, string? baseDir) => name switch
    {
        "Read" or "Edit" or "Write" or "MultiEdit" or "NotebookEdit" =>
            RelativePath(ClaudeJson.GetString(input, "file_path"), baseDir),
        "view" => RelativePath(ClaudeJson.GetString(input, "path"), baseDir),
        "Glob" or "glob" or "Grep" or "rg" => Search(input, baseDir),
        "Bash" or "PowerShell" or "powershell" => FirstLine(ClaudeJson.GetString(input, "command")),
        "StructuredOutput" => "reporting the result",
        "Task" or "Agent" => ClaudeJson.GetString(input, "description") ?? "",
        _ => "",
    };

    private static string Search(JsonElement input, string? baseDir)
    {
        var pattern = ClaudeJson.GetString(input, "pattern") ?? "";
        var path = ClaudeJson.GetString(input, "path");
        return string.IsNullOrEmpty(path) ? pattern : $"{pattern} in {RelativePath(path, baseDir)}";
    }

    private static string FirstLine(string? text)
    {
        if (text is null)
            return "";

        var end = text.IndexOf('\n', StringComparison.Ordinal);
        return end < 0 ? text : text[..end].TrimEnd('\r');
    }

    /// <summary>
    /// The path relative to <paramref name="baseDir"/> with forward slashes when it lies below it
    /// (case-insensitive, either slash kind); otherwise the path unchanged.
    /// </summary>
    private static string RelativePath(string? path, string? baseDir)
    {
        if (path is null)
            return "";
        if (string.IsNullOrEmpty(baseDir))
            return path;

        var normalizedBase = baseDir.Replace('\\', '/').TrimEnd('/');
        if (normalizedBase.Length == 0)
            return path;

        var normalizedPath = path.Replace('\\', '/');
        var prefixLength = normalizedBase.Length + 1;
        return normalizedPath.Length > prefixLength &&
               normalizedPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase) &&
               normalizedPath[normalizedBase.Length] == '/'
            ? normalizedPath[prefixLength..]
            : path;
    }
}
