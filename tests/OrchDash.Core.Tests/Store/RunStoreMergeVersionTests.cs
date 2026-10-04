using OrchDash.Core.Model;
using OrchDash.Core.Store;
using Xunit;
using static OrchDash.Core.Tests.Store.ProviderRun;
using static OrchDash.Core.Tests.Store.RunData;

namespace OrchDash.Core.Tests.Store;

// Spec 14.8: a new Version only when the merged content differs, with the provider-store data compared element by element.
public sealed class RunStoreMergeVersionTests : IDisposable
{
    private readonly ProviderRun _run = new();
    private string? _change;   // the fakes' content differs in this one part

    public RunStoreMergeVersionTests()
    {
        _run.Transcripts.Next = (id, _) => id == "s1" ? ClaudeStore() : null;
        _run.Folders.Next = (_, _) => _change == "reason" ? null : CopilotStore();
        _run.Usage.Next = _ => Rows();
    }

    public void Dispose() => _run.Dispose();

    // The same content on every call, in new arrays and new records.
    private StoreData ClaudeStore() => new(TestedVersions.ClaudeCode,
        ["You are Claude Code.", _change == "system-prompt" ? "Be brief!" : "Be brief."],
        [new ToolDefinition("Bash", "Runs a command.", _change == "tool" ? """{"type":"array"}""" : """{"type":"object"}""")],
        [new InjectedItem("skill_listing", "system", At(12, 0, 8), _change == "injected" ? "Skills: none" : "Skills: review")],
        [new CallFigures("call-1", At(12, 0, 10), new TokenUsage(10, 100, 20, 50), 7, null, null, "end_turn")],
        _change == "cost" ? 0.26 : 0.25, _change == "lines" ? 13 : 12, 3, _change == "unparsed" ? 1 : 0);

    // Without content, so that "reason" (null) changes nothing but Unavailable.
    private StoreData CopilotStore() => new(_change == "version-line" ? "1.0.90" : null, [], [], [], [], null, null, null, 0);

    private UsageRows Rows() => MergeData.Rows(
        _change == "schema" ? 7 : TestedVersions.CopilotSchema,
        _change == "usage-problem" ? "Copilot database: locked" : null,
        (CopilotId, [new CallFigures(null, At(12, 0, 22), new TokenUsage(30, 0, 12000, 191),
            _change == "figure" ? 9 : 8, 3_000_000_000, TimeSpan.FromSeconds(2), "stop")]));

    [Fact]
    public void Equal_content_from_the_sources_in_new_arrays_keeps_the_version()
    {
        using var store = _run.NewStore(_run.Stores);
        store.Poll();
        var first = store.Current;
        var builds = _run.Factory.Parsers.Sum(p => p.BuildCount);

        Append(_run.Claude, "\n");   // the fake parsers build equal content again
        Append(_run.Copilot, "\n");
        store.Poll();
        store.Poll();

        Assert.Equal(builds + 2, _run.Factory.Parsers.Sum(p => p.BuildCount));
        Assert.Equal(3, _run.Transcripts.Calls.Count);
        Assert.Equal(3, _run.Usage.Calls.Count);
        Assert.Same(first, store.Current);
        Assert.Equal(1, store.Current.Version);
        var copilot = SessionOf(store, _run.Copilot);
        Assert.NotNull(copilot.Content.Checkpoint);
        Assert.NotNull(copilot.Content.SentPrompt);
        Assert.NotNull(SessionOf(store, _run.Claude).Content.RateLimit);
    }

    [Theory]
    [InlineData("system-prompt")]
    [InlineData("tool")]
    [InlineData("injected")]
    [InlineData("figure")]
    [InlineData("cost")]
    [InlineData("lines")]
    [InlineData("unparsed")]
    [InlineData("reason")]
    [InlineData("version-line")]
    [InlineData("schema")]
    [InlineData("usage-problem")]
    [InlineData("checkpoint-tool")]
    [InlineData("checkpoint-segment")]
    [InlineData("rate-limit")]
    [InlineData("sent-prompt")]
    public void A_change_raises_the_version_by_one(string change)
    {
        using var store = _run.NewStore(_run.Stores);
        store.Poll();
        store.Poll();

        switch (change)
        {
            case "checkpoint-tool":
                Append(_run.Copilot, CheckpointLine("edit", "tone_and_style") + "\n");
                break;
            case "checkpoint-segment":
                Append(_run.Copilot, CheckpointLine("view", "tool_efficiency") + "\n");
                break;
            case "rate-limit":
                Append(_run.Claude, RateLimitLine(0.21) + "\n");
                break;
            case "sent-prompt":
                Append(_run.Copilot, SentPromptLine("Build beta, and test it.") + "\n");
                break;
            default:
                _change = change;
                break;
        }
        store.Poll();
        store.Poll();

        Assert.Equal(2, store.Current.Version);
    }

    private void Append(SessionFiles session, string text) => _run.Repo.Append(session, text);
}
