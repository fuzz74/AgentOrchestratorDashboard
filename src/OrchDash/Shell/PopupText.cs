using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OrchDash.Contracts;
using XenoAtom.Ansi;

namespace OrchDash.Shell;

/// <summary>Builds the markup shown in the pop-up's scrollable area.</summary>
public static class PopupText
{
    private static readonly JsonWriterOptions IndentedJson = new()
    {
        Indented = true,
        IndentSize = 2,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Each section as its bold heading followed by its text, separated by a blank line. All text is escaped;
    /// <see cref="TextKind.Diff"/> lines are coloured and <see cref="TextKind.Json"/> is re-indented when it parses.
    /// </summary>
    public static string Build(IReadOnlyList<PopupSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var markup = new StringBuilder();
        foreach (var section in sections)
        {
            if (markup.Length > 0)
            {
                markup.Append("\n\n");
            }
            markup.Append(Look.Tag("bold", section.Heading)).Append('\n');
            markup.Append(section.Kind switch
            {
                TextKind.Plain => Escape(section.Text),
                TextKind.Json => Escape(IndentJson(section.Text)),
                TextKind.Diff => ColorDiff(section.Text),
                _ => throw new ArgumentOutOfRangeException(nameof(sections), section.Kind, null),
            });
        }
        return markup.ToString();
    }

    /// <summary>The JSON indented by two spaces per level, or the text unchanged when it does not parse.</summary>
    private static string IndentJson(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return text;
        }
        using (document)
        {
            var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, IndentedJson))
            {
                document.RootElement.WriteTo(writer);
            }
            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
    }

    private static string ColorDiff(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var color = line.StartsWith("@@", StringComparison.Ordinal) ? "accent"
                : line.StartsWith('+') ? "success"
                : line.StartsWith('-') ? "error"
                : "";
            lines[i] = line.Length == 0 ? "" : Look.Tag(color, line);
        }
        return string.Join('\n', lines);
    }

    private static string Escape(string text) => AnsiMarkup.Escape(text.Replace("\r\n", "\n", StringComparison.Ordinal));
}
