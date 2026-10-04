using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OrchDash.Core.Copilot;

/// <summary>Tolerant reads from Copilot event lines: a missing value or one of the wrong type is null.</summary>
internal static class CopilotJson
{
    private static readonly JsonWriterOptions CompactOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonWriterOptions IndentedOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true,
        NewLine = "\n",
    };

    /// <summary>The property, or a default element (kind <c>Undefined</c>) when it is absent.</summary>
    public static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    public static string? String(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    public static bool? Boolean(JsonElement element, string name) => Property(element, name).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    public static int? Int32(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number)
            ? number
            : null;

    public static long? Int64(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number)
            ? number
            : null;

    public static double? Double(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDouble(out var number)
            ? number
            : null;

    public static TimeSpan? Milliseconds(JsonElement element, string name) =>
        // Well inside the range of TimeSpan, so FromMilliseconds cannot overflow.
        Double(element, name) is { } ms && Math.Abs(ms) < TimeSpan.MaxValue.TotalMilliseconds / 2
            ? TimeSpan.FromMilliseconds(ms)
            : null;

    public static DateTimeOffset? Time(JsonElement element, string name) =>
        String(element, name) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : null;

    /// <summary>The element as JSON, or <c>""</c> when it is absent.</summary>
    public static string Write(JsonElement element, bool indented = false)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
            return "";

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, indented ? IndentedOptions : CompactOptions))
            element.WriteTo(writer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
