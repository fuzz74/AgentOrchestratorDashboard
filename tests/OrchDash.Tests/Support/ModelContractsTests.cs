using System.Collections.Immutable;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.Support;

public sealed class ModelContractsTests
{
    [Fact]
    public void StoreData_Empty_has_nulls_empty_arrays_and_no_unparsed_lines()
    {
        var empty = StoreData.Empty;

        Assert.Null(empty.CliVersion);
        Assert.False(empty.SystemPrompt.IsDefault);
        Assert.Empty(empty.SystemPrompt);
        Assert.False(empty.Tools.IsDefault);
        Assert.Empty(empty.Tools);
        Assert.False(empty.Injected.IsDefault);
        Assert.Empty(empty.Injected);
        Assert.False(empty.Calls.IsDefault);
        Assert.Empty(empty.Calls);
        Assert.Null(empty.CostUsd);
        Assert.Null(empty.LinesAdded);
        Assert.Null(empty.LinesRemoved);
        Assert.Equal(0, empty.UnparsedLines);
        Assert.Same(empty, StoreData.Empty);
    }

    [Fact]
    public void StoreData_has_one_shared_empty_sub_agent_dictionary_with_ordinal_keys()
    {
        // (36.1) 4.3: one instance, so store data without sub-agents stays equal.
        var none = StoreData.NoSubAgents;

        Assert.Empty(none);
        Assert.Same(StringComparer.Ordinal, none.KeyComparer);
        Assert.Same(none, StoreData.NoSubAgents);
        Assert.Same(none, StoreData.Empty.SubAgents);
        Assert.Same(none, new StoreData(null, [], [], [], [], null, null, null, 0).SubAgents);
    }

    [Fact]
    public void UsageRows_Empty_has_no_rows_and_ordinal_keys()
    {
        var empty = UsageRows.Empty;

        Assert.Empty(empty.BySession);
        Assert.Same(StringComparer.Ordinal, empty.BySession.KeyComparer);
        Assert.Null(empty.SchemaVersion);
        Assert.Null(empty.Problem);
        Assert.Same(empty, UsageRows.Empty);
    }

    [Fact]
    public void ProviderStores_None_has_no_sources()
    {
        var none = ProviderStores.None;

        Assert.Null(none.ClaudeTranscripts);
        Assert.Null(none.CopilotFolders);
        Assert.Null(none.CopilotIds);
        Assert.Null(none.CopilotUsage);
        Assert.Same(none, ProviderStores.None);
    }

    [Fact]
    public void A_session_built_from_its_parameters_has_no_store_data()
    {
        var sample = SampleRun.Create().Sessions[0];
        var session = new Session(sample.Files, sample.Provider, sample.State, sample.Prompt, sample.StartedAt,
            SessionContent.Empty);

        Assert.Same(StoreData.Empty, session.Stores);
        Assert.False(session.Unavailable.IsDefault);
        Assert.Empty(session.Unavailable);
    }

    [Fact]
    public void A_model_call_built_from_its_parameters_has_null_new_members()
    {
        var call = new ModelCall("msg_01", "claude-sonnet-4-5", SampleRun.At(12, 0, 10), null);

        Assert.Null(call.ThinkingTokens);
        Assert.Null(call.NanoAiu);
        Assert.Null(call.Duration);
        Assert.Null(call.StopReason);
        Assert.Null(call.AgentId);
    }

    [Fact]
    public void Records_built_from_their_parameters_belong_to_no_sub_agent()
    {
        // (35.2, 35.4, 36.2, 37.1, 38.2)
        var figures = new CallFigures("msg_01", SampleRun.At(12, 0, 10), new TokenUsage(1, 2, 3, 4), null, null, null, null);
        var tool = new ToolCall("msg_01", SampleRun.At(12, 0, 10), "toolu_01", "Read", "{}", "a.cs", null);
        var entry = new ProgressEntry(SampleRun.At(12, 0, 0), "planner", "Glob **/*", ProgressKind.Activity);
        var timeline = new TimelineEvent("progress:0", entry.Time, TimelineKind.Orchestrator, "run", 0, entry,
            null, null, null, null);

        Assert.Null(figures.AgentId);
        Assert.Null(figures.ParentToolCallId);
        Assert.Null(tool.AgentId);
        Assert.Null(entry.SubAgent);
        Assert.Null(timeline.AgentId);
    }

    [Fact]
    public void SessionContent_Empty_has_null_new_members()
    {
        var empty = SessionContent.Empty;

        Assert.Null(empty.SentPrompt);
        Assert.Null(empty.Checkpoint);
        Assert.Null(empty.RateLimit);
        Assert.Same(empty, SessionContent.Empty);
    }

    [Fact]
    public void SessionContent_has_no_sub_agents_unless_set()
    {
        // (35.1) 4.3: an empty array, not a default one.
        var built = new SessionContent(null, null, null, [], [], null, null, null, 0);

        Assert.False(SessionContent.Empty.SubAgents.IsDefault);
        Assert.Empty(SessionContent.Empty.SubAgents);
        Assert.False(built.SubAgents.IsDefault);
        Assert.Empty(built.SubAgents);
    }

    [Fact]
    public void A_timeline_event_has_record_equality()
    {
        var run = SampleRun.Create();
        var session = run.Sessions[0];
        TimelineEvent Event(TimelineKind kind) => new($"{session.Files.Key}:item:2", SampleRun.At(12, 0, 15), kind,
            "alpha", 2, null, session, null, session.Content.Items[2], null);

        Assert.Equal(Event(TimelineKind.Tool), Event(TimelineKind.Tool));
        Assert.NotEqual(Event(TimelineKind.Tool), Event(TimelineKind.Text));
        Assert.Equal(
            new TimelineEvent("progress:0", run.Progress[0].Time, TimelineKind.Orchestrator, "run", 0, run.Progress[0],
                null, null, null, null),
            new TimelineEvent("progress:0", run.Progress[0].Time, TimelineKind.Orchestrator, "run", 0, run.Progress[0],
                null, null, null, null));
    }

    [Fact]
    public void A_run_entry_and_a_run_catalog_have_record_equality()
    {
        RunEntry Entry(string? problem) => new(@"C:\X\Repo.runs\20261006-195310", "20261006-195310", "spec.md",
            SampleRun.At(12, 0, 0), SampleRun.At(12, 30, 0), 5, 4, 1, Provider.Claude, "opus", problem);

        Assert.Equal(Entry(null), Entry(null));
        Assert.NotEqual(Entry(null), Entry("tasks.json: not JSON"));

        var runs = new[] { Entry(null) }.ToImmutableArray();
        Assert.Equal(new RunCatalog(runs, null), new RunCatalog(runs, null));
        Assert.NotEqual(new RunCatalog(runs, null), new RunCatalog(runs, @"C:\X\Repo.runs: access denied"));
    }

    [Fact]
    public void Tested_versions()
    {
        Assert.Equal("2.1.285", TestedVersions.ClaudeCode);
        Assert.Equal("1.0.91", TestedVersions.CopilotCli);
        Assert.Equal(8, TestedVersions.CopilotSchema);
    }
}
