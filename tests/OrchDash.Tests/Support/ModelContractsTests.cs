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
    public void Tested_versions()
    {
        Assert.Equal("2.1.285", TestedVersions.ClaudeCode);
        Assert.Equal("1.0.91", TestedVersions.CopilotCli);
        Assert.Equal(8, TestedVersions.CopilotSchema);
    }
}
