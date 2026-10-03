using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Store;

// Records its lines and reports each non-empty one as an AssistantText. A JSON line may carry
// "ts" (event time), "session_id" and "result" ("ok" or "error"). Build fills every array-valued
// field with new arrays on each call, so equal content never shares an array.
public sealed class FakeSessionParser(Provider provider, string? workDir, FakeParserFactory factory) : ISessionParser
{
    private readonly List<string> _lines = [];

    public Provider Provider { get; } = provider;
    public string? WorkDir { get; } = workDir;
    public IReadOnlyList<string> Lines => [.. _lines];
    public int BuildCount { get; private set; }

    public void AddLine(string line)
    {
        if (factory.TakeThrow(line))
            throw new InvalidOperationException("fake parser failed on " + line);
        _lines.Add(line);
    }

    public SessionContent Build()
    {
        BuildCount++;
        string? sessionId = null;
        DateTimeOffset? first = null;
        DateTimeOffset? last = null;
        SessionResult? result = null;
        var items = ImmutableArray.CreateBuilder<ConversationItem>();
        foreach (var line in _lines.Where(l => l.Length > 0))
        {
            items.Add(new AssistantText(null, null, line));
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    continue;
                var root = document.RootElement;
                if (root.TryGetProperty("session_id", out var id) && id.ValueKind == JsonValueKind.String)
                    sessionId = id.GetString();
                if (root.TryGetProperty("ts", out var ts) && ts.ValueKind == JsonValueKind.String)
                {
                    var time = DateTimeOffset.Parse(ts.GetString()!, CultureInfo.InvariantCulture);
                    first ??= time;
                    last = time;
                }
                if (root.TryGetProperty("result", out var outcome) && outcome.ValueKind == JsonValueKind.String)
                    result = Result(outcome.GetString() == "error");
            }
            catch (JsonException)
            {
            }
        }

        return new SessionContent(sessionId, "fake-model",
            new SessionInit(WorkDir, "default", "1.0", ["Read", "Bash"], ["files (connected)"]),
            [new ModelCall("call-1", "fake-model", first, new TokenUsage(1, 2, 3, 4))],
            items.ToImmutable(), result, first, last, 0);
    }

    private static SessionResult Result(bool isError) => new(
        isError, isError ? "error" : "success", "text", null, null,
        new ReviewVerdict("pass", "pass", "fine", [new ReviewIssue("minor", null, "a nit")]),
        0.1, 1, null, null, null, null, null, null, null);
}
