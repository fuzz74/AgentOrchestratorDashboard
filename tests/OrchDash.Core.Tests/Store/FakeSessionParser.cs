using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Store;

// Records its lines and reports each non-empty one as an AssistantText. A JSON line may carry
// "ts" (event time), "session_id" and "result" ("ok" or "error"). A line with "sent_prompt" (a string),
// "rate_limit" (the 5-hour fraction) or "checkpoint" ({"tools":[names],"segments":[{"name","tokens"}]})
// sets that member instead, the latest line winning, and is not reported as text. Build fills every
// array-valued field with new arrays and creates new records on each call, so equal content never shares one.
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
        string? sentPrompt = null;
        RateLimit? rateLimit = null;
        ContextCheckpoint? checkpoint = null;
        var items = ImmutableArray.CreateBuilder<ConversationItem>();
        foreach (var line in _lines.Where(l => l.Length > 0))
        {
            var isText = true;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
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
                    if (root.TryGetProperty("sent_prompt", out var sent))
                    {
                        sentPrompt = sent.GetString();
                        isText = false;
                    }
                    if (root.TryGetProperty("rate_limit", out var used))
                    {
                        rateLimit = new RateLimit("allowed", "five_hour", used.GetDouble(), null, 0.27, null, last);
                        isText = false;
                    }
                    if (root.TryGetProperty("checkpoint", out var state))
                    {
                        checkpoint = Checkpoint(state);
                        isText = false;
                    }
                }
            }
            catch (JsonException)
            {
            }
            if (isText)
                items.Add(new AssistantText(null, null, line));
        }

        return new SessionContent(sessionId, "fake-model",
            new SessionInit(WorkDir, "default", "1.0", ["Read", "Bash"], ["files (connected)"]),
            [new ModelCall("call-1", "fake-model", first, new TokenUsage(1, 2, 3, 4))],
            items.ToImmutable(), result, first, last, 0)
        {
            SentPrompt = sentPrompt,
            RateLimit = rateLimit,
            Checkpoint = checkpoint,
        };
    }

    private static ContextCheckpoint Checkpoint(JsonElement state) => new(
        null, 1996,
        [.. state.GetProperty("tools").EnumerateArray().Select(t => t.GetString()!)],
        [.. state.GetProperty("segments").EnumerateArray()
            .Select(s => new TokenPart(s.GetProperty("name").GetString()!, s.GetProperty("tokens").GetInt64()))],
        12_930_990_000, 1);

    private static SessionResult Result(bool isError) => new(
        isError, isError ? "error" : "success", "text", null, null,
        new ReviewVerdict("pass", "pass", "fine", [new ReviewIssue("minor", null, "a nit")]),
        0.1, 1, null, null, null, null, null, null, null);
}
