using System.Collections.Immutable;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OrchDash.Core.RunFolder;

/// <summary>Reads optional properties of a JSON object; a missing property, null or a value of the wrong type gives the default.</summary>
internal static class JsonValues
{
    private static readonly JsonWriterOptions CompactOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static JsonElement? Property(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) ? value : null;

    public static string? String(JsonElement obj, string name) =>
        Property(obj, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    public static int Int(JsonElement obj, string name) =>
        Property(obj, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var result) ? result : 0;

    public static double Double(JsonElement obj, string name) =>
        Property(obj, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDouble(out var result) ? result : 0;

    public static DateTimeOffset? Time(JsonElement obj, string name) =>
        Property(obj, name) is { ValueKind: JsonValueKind.String } value && value.TryGetDateTimeOffset(out var result)
            ? result
            : null;

    // The string items of an array property; other items are skipped.
    public static ImmutableArray<string> Strings(JsonElement obj, string name) =>
        Property(obj, name) is { ValueKind: JsonValueKind.Array } array
            ? [.. array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
            : [];

    // The value as JSON text without indentation; a string keeps its quotes.
    public static string Compact(JsonElement value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, CompactOptions))
            value.WriteTo(writer);
        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
