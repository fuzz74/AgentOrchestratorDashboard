using System.Globalization;

namespace OrchDash.Core.SessionFolder;

// The four fields of a session folder's workspace.yaml that Find needs (spec 12.3, 12.4).
internal sealed record Workspace(string Id, string Name, string Cwd, DateTimeOffset CreatedAt)
{
    // Null when the text lacks a usable id, name, cwd or created_at.
    public static Workspace? Parse(string text)
    {
        var values = ReadPairs(text);
        if (!values.TryGetValue("id", out var id) || id.Length == 0
            || !values.TryGetValue("name", out var name) || name.Length == 0
            || !values.TryGetValue("cwd", out var cwd) || cwd.Length == 0
            || !values.TryGetValue("created_at", out var createdText)
            || !DateTimeOffset.TryParse(createdText, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var createdAt))
            return null;

        return new Workspace(id, name, cwd, createdAt);
    }

    // Lines of `key: value`, split at the first ':'; the value trimmed and one pair of surrounding quotes removed.
    // Lines without ':' are ignored; of a key that appears twice the first value counts.
    private static Dictionary<string, string> ReadPairs(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon < 0)
                continue;
            values.TryAdd(line[..colon].Trim(), Unquote(line[(colon + 1)..].Trim()));
        }
        return values;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] is '"' or '\'') && value[^1] == value[0] ? value[1..^1] : value;
}
