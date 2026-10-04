using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OrchDash.Core.Transcript;

/// <summary>
/// Reads values from parsed transcript lines without throwing: a missing value or a value of the
/// wrong type reads as null.
/// </summary>
internal static class TranscriptJson
{
    private static readonly JsonWriterOptions IndentedOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
    };

    /// <summary>Parses a line; null when it is not valid JSON.</summary>
    public static JsonDocument? TryParse(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // The text holds an unpaired surrogate and cannot be transcoded to UTF-8.
            return null;
        }
    }

    public static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
            return true;

        value = default;
        return false;
    }

    public static bool TryGetArray(JsonElement element, string name, out JsonElement value) =>
        TryGetProperty(element, name, out value) && value.ValueKind == JsonValueKind.Array;

    /// <summary>The property when it is an object; otherwise a default element, from which every read gives null.</summary>
    public static JsonElement GetObject(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;

    public static string? GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) ? AsString(value) : null;

    public static bool IsTrue(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.True;

    public static int? GetInt32(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number) ? number : null;

    public static long? GetInt64(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number) ? number : null;

    public static double? GetDouble(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;

    public static DateTimeOffset? GetTime(JsonElement element, string name) =>
        GetString(element, name) is { } text &&
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : null;

    /// <summary>The value of a string element, or null for any other kind.</summary>
    public static string? AsString(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
            return null;

        try
        {
            return element.GetString();
        }
        catch (InvalidOperationException)
        {
            // An escaped unpaired surrogate (\ud800) cannot be unescaped; keep the escaped text.
            var raw = element.GetRawText();
            return raw[1..^1];
        }
    }

    /// <summary>The element as JSON text, indented by two spaces with \n line breaks.</summary>
    public static string WriteIndented(JsonElement element)
    {
        try
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, IndentedOptions))
                element.WriteTo(writer);
            return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
        }
        catch (InvalidOperationException)
        {
            // An escaped unpaired surrogate cannot be rewritten; fall back to the text as it came.
            return element.GetRawText();
        }
    }
}
