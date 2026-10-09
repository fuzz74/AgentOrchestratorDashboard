using System.Globalization;
using System.Text.Json;

namespace OrchDash.Tests.App;

/// <summary>Values as the synthetic lines and rows of the end-to-end sub-agent run write them.</summary>
internal static class SubAgentJson
{
    /// <summary>The value as a JSON string literal.</summary>
    public static string Quote(string value) => JsonSerializer.Serialize(value);

    /// <summary>The time in UTC as the providers write it, for example <c>2026-10-09T10:00:02.500Z</c>.</summary>
    public static string Time(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
