using OrchDash.Core.Model;
using Xunit;
using static OrchDash.Core.Tests.Store.MergeData;

namespace OrchDash.Core.Tests.Store;

// Spec 14.7 and the version rules in section 4.3.
public sealed class ProviderMergeProblemTests
{
    private static ProviderStores StoresOf(StoreData? transcript = null, StoreData? folder = null, UsageRows? rows = null) =>
        new(new FakeSessionStore((_, _) => transcript), new FakeSessionStore((_, _) => folder), null,
            new FakeUsageReader(_ => rows ?? UsageRows.Empty));

    [Fact]
    public void A_Claude_version_that_differs()
    {
        var result = Merge(StoresOf(), ClaudeSession(Files("alpha"), init: Init(cliVersion: "2.1.3")));

        Assert.Equal(["Claude Code 2.1.3: OrchDash was made for 2.1.285"], result.Problems);
    }

    [Fact]
    public void The_init_version_comes_before_the_transcript_version()
    {
        var result = Merge(StoresOf(transcript: Stored("2.1.4")),
            ClaudeSession(Files("alpha"), "c-1", init: Init(cliVersion: "2.1.3")),
            ClaudeSession(Files("beta"), "c-2", init: Init()),
            ClaudeSession(Files("gamma"), "c-3"));

        Assert.Equal(["Claude Code 2.1.3: OrchDash was made for 2.1.285", "Claude Code 2.1.4: OrchDash was made for 2.1.285"],
            result.Problems);
    }

    [Fact]
    public void A_Copilot_version_that_differs()
    {
        var result = Merge(StoresOf(folder: Stored("1.0.80")), CopilotSession(Files("alpha")));

        Assert.Equal(["Copilot CLI 1.0.80: OrchDash was made for 1.0.91"], result.Problems);
    }

    [Fact]
    public void A_schema_version_that_differs()
    {
        var result = Merge(StoresOf(rows: Rows(9, null)), CopilotSession(Files("alpha")));

        Assert.Equal(["Copilot database schema 9: OrchDash was made for 8"], result.Problems);
    }

    [Fact]
    public void Each_distinct_version_once_in_the_order_it_first_appears()
    {
        var result = Merge(StoresOf(),
            ClaudeSession(Files("alpha"), init: Init(cliVersion: "2.1.4")),
            ClaudeSession(Files("beta"), init: Init(cliVersion: "2.1.3")),
            ClaudeSession(Files("gamma"), init: Init(cliVersion: "2.1.4")),
            ClaudeSession(Files("delta"), init: Init(cliVersion: "2.1.3")));

        Assert.Equal(["Claude Code 2.1.4: OrchDash was made for 2.1.285", "Claude Code 2.1.3: OrchDash was made for 2.1.285"],
            result.Problems);
    }

    [Fact]
    public void The_tested_versions_give_no_line()
    {
        var result = Merge(StoresOf(transcript: Stored(TestedVersions.ClaudeCode), folder: Stored(TestedVersions.CopilotCli),
                rows: Rows(TestedVersions.CopilotSchema, null)),
            ClaudeSession(Files("alpha"), init: Init(cliVersion: TestedVersions.ClaudeCode)),
            ClaudeSession(Files("beta")),
            CopilotSession(Files("gamma")));

        Assert.Empty(result.Problems);
    }

    [Fact]
    public void The_usage_problem_comes_first_then_Claude_Copilot_and_the_schema()
    {
        var result = Merge(StoresOf(folder: Stored("1.0.80"), rows: Rows(7, "Copilot database: locked")),
            CopilotSession(Files("alpha")),
            ClaudeSession(Files("beta"), init: Init(cliVersion: "2.1.3")));

        Assert.Equal(
        [
            "Copilot database: locked",
            "Claude Code 2.1.3: OrchDash was made for 2.1.285",
            "Copilot CLI 1.0.80: OrchDash was made for 1.0.91",
            "Copilot database schema 7: OrchDash was made for 8",
        ], result.Problems);
    }
}
