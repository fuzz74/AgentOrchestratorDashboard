using OrchDash.Core.Model;
using OrchDash.Core.Store;
using Xunit;
using static OrchDash.Core.Tests.Store.ProviderRun;
using static OrchDash.Core.Tests.Store.RunData;

namespace OrchDash.Core.Tests.Store;

// Spec 14.1-14.7 and 14.9 through RunStore: the provider stores' data on each session of the snapshot.
public sealed class RunStoreMergeTests : IDisposable
{
    private readonly ProviderRun _run = new();

    public void Dispose() => _run.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Without_a_source_every_session_has_empty_store_data(bool none)
    {
        using var store = _run.NewStore(none ? ProviderStores.None : null);

        store.Poll();

        Assert.Equal(2, store.Current.Sessions.Length);
        Assert.All(store.Current.Sessions, session =>
        {
            Assert.Same(StoreData.Empty, session.Stores);
            Assert.Empty(session.Unavailable);
            Assert.Equal(new TokenUsage(1, 2, 3, 4), Assert.Single(session.Content.Calls).Usage);
        });
        Assert.Null(SessionOf(store, _run.Copilot).Content.SessionId);
        Assert.Equal([ReaderProblem], store.Current.Problems);
    }

    [Fact]
    public void Claude_and_Copilot_sessions_are_enriched()
    {
        var gamma = _run.Repo.Session("gamma/20261003-120000/attempt-1-worker.json", "gamma");
        _run.Repo.Append(gamma, """{"session_id":"s3","ts":"2026-10-03T12:00:30+00:00"}""" + "\n");
        _run.Sessions = [_run.Claude, _run.Copilot, gamma];
        var claudeStore = StoreData.Empty with
        {
            CliVersion = TestedVersions.ClaudeCode,
            SystemPrompt = ["You are Claude Code."],
            Calls = [new CallFigures("call-1", At(12, 0, 10), new TokenUsage(10, 100, 20, 50), 7, null, null, "end_turn")],
            CostUsd = 0.25,
        };
        var copilotStore = StoreData.Empty with { CliVersion = "1.0.90", SystemPrompt = ["You are Copilot."] };
        var row = new CallFigures(null, At(12, 0, 22), new TokenUsage(30, 0, 12000, null), 8, 3_000_000_000, TimeSpan.FromSeconds(2), "stop");
        _run.Transcripts.Next = (id, _) => id == "s1" ? claudeStore : null;
        _run.Folders.Next = (_, _) => copilotStore;
        _run.Usage.Next = _ => MergeData.Rows(7, "Copilot database: locked", (CopilotId, [row]));
        using var store = _run.NewStore(_run.Stores);

        store.Poll();

        var claude = SessionOf(store, _run.Claude);
        Assert.Same(claudeStore, claude.Stores);
        Assert.Empty(claude.Unavailable);
        var claudeCall = Assert.Single(claude.Content.Calls);
        Assert.Equal(new TokenUsage(10, 100, 20, 50), claudeCall.Usage);
        Assert.Equal(7L, claudeCall.ThinkingTokens);
        Assert.Equal("end_turn", claudeCall.StopReason);
        Assert.Null(claudeCall.NanoAiu);

        var copilot = SessionOf(store, _run.Copilot);
        Assert.Equal(CopilotId, copilot.Content.SessionId);
        Assert.Equal("1.0.90", copilot.Stores.CliVersion);
        Assert.Equal(["You are Copilot."], copilot.Stores.SystemPrompt);
        Assert.Equal([row], copilot.Stores.Calls);
        Assert.Empty(copilot.Unavailable);
        var copilotCall = Assert.Single(copilot.Content.Calls);
        Assert.Equal(new TokenUsage(30, 0, 12000, 4), copilotCall.Usage);   // a null output keeps the call's own
        Assert.Equal(8L, copilotCall.ThinkingTokens);
        Assert.Equal(3_000_000_000L, copilotCall.NanoAiu);
        Assert.Equal(TimeSpan.FromSeconds(2), copilotCall.Duration);
        Assert.Equal("stop", copilotCall.StopReason);

        var noTranscript = SessionOf(store, gamma);
        Assert.Same(StoreData.Empty, noTranscript.Stores);
        Assert.Equal(["no transcript"], noTranscript.Unavailable);

        Assert.Equal(
            [
                ReaderProblem,
                "Copilot database: locked",
                $"Claude Code 1.0: OrchDash was made for {TestedVersions.ClaudeCode}",   // the fake parser's Init.CliVersion
                $"Copilot CLI 1.0.90: OrchDash was made for {TestedVersions.CopilotCli}",
                $"Copilot database schema 7: OrchDash was made for {TestedVersions.CopilotSchema}",
            ],
            store.Current.Problems);
        Assert.Equal(CopilotId, Assert.Single(Assert.Single(_run.Usage.Calls)));
    }

    [Fact]
    public void A_source_receives_the_work_dir_the_parser_received()
    {
        _run.Transcripts.Next = (_, _) => StoreData.Empty;
        _run.Folders.Next = (_, _) => StoreData.Empty;
        using var store = _run.NewStore(_run.Stores);

        store.Poll();

        var claudeWorkDir = _run.ParserOf(Provider.Claude).WorkDir;
        var copilotWorkDir = _run.ParserOf(Provider.Copilot).WorkDir;
        Assert.Equal(Path.Combine(_run.Repo.RepoPath + ".worktrees", "beta"), copilotWorkDir);
        Assert.Equal(("s1", claudeWorkDir), Assert.Single(_run.Transcripts.Calls));
        Assert.Equal((CopilotId, copilotWorkDir), Assert.Single(_run.Folders.Calls));
        var find = Assert.Single(_run.Finder.Calls);
        Assert.Equal(copilotWorkDir, find.WorkDir);
        Assert.Equal(At(12, 0, 20), find.StartedAt);
        Assert.Equal(["s1"], find.KnownIds);
    }

    [Theory]
    [InlineData("transcripts")]
    [InlineData("folders")]
    [InlineData("finder")]
    [InlineData("usage")]
    public void A_source_that_throws_keeps_the_last_read_with_the_message(string source)
    {
        using var store = _run.NewStore(_run.Stores);
        store.Poll();
        var before = store.Current;

        SetThrowing(source, true);
        store.Poll();

        var failed = store.Current;
        Assert.Equal(2, failed.Version);
        Assert.Equal([.. before.Problems, source + " broke"], failed.Problems);
        Assert.Equal(before.Sessions, failed.Sessions);

        SetThrowing(source, false);
        store.Poll();

        Assert.Equal(3, store.Current.Version);
        Assert.Equal(before.Problems, store.Current.Problems);
    }

    [Fact]
    public void A_run_with_only_Claude_sessions_calls_no_Copilot_source()
    {
        var undecided = _run.Repo.Session("planner-20261003-110111-1.json", role: AgentRole.Planner);
        _run.Repo.Append(undecided, "plain text\n");
        _run.Sessions = [_run.Claude, undecided];
        using var store = _run.NewStore(_run.Stores);

        store.Poll();
        store.Poll();

        Assert.Equal(2, _run.Transcripts.Calls.Count);
        Assert.Empty(_run.Folders.Calls);
        Assert.Empty(_run.Finder.Calls);
        Assert.Empty(_run.Usage.Calls);
    }

    [Fact]
    public void A_run_with_only_Copilot_sessions_calls_no_Claude_source()
    {
        _run.Sessions = [_run.Copilot];
        using var store = _run.NewStore(_run.Stores);

        store.Poll();
        store.Poll();

        Assert.Empty(_run.Transcripts.Calls);
        Assert.Equal(2, _run.Folders.Calls.Count);
        Assert.Equal(2, _run.Finder.Calls.Count);
        Assert.Equal(2, _run.Usage.Calls.Count);
    }

    private void SetThrowing(string source, bool throws)
    {
        var message = source + " broke";
        switch (source)
        {
            case "transcripts":
                _run.Transcripts.Next = throws ? (_, _) => throw new InvalidOperationException(message) : (_, _) => null;
                break;
            case "folders":
                _run.Folders.Next = throws ? (_, _) => throw new InvalidOperationException(message) : (_, _) => null;
                break;
            case "finder":
                _run.Finder.Next = throws ? (_, _, _, _) => throw new InvalidOperationException(message) : (_, _, _, _) => CopilotId;
                break;
            case "usage":
                _run.Usage.Next = throws ? _ => throw new InvalidOperationException(message) : _ => UsageRows.Empty;
                break;
        }
    }
}
