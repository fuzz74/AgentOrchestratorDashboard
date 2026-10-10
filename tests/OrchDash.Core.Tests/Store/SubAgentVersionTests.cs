using System.Collections.Immutable;
using OrchDash.Core.Model;
using OrchDash.Core.Store;
using Xunit;
using static OrchDash.Core.Tests.Store.ProviderRun;
using static OrchDash.Core.Tests.Store.RunData;

namespace OrchDash.Core.Tests.Store;

// Spec 4.3: the sub-agents of the content and of the store data are compared by content, so equal content in new
// instances keeps the version, and a change raises it.
public sealed class SubAgentVersionTests : IDisposable
{
    private readonly ProviderRun _run = new();
    private string? _change;   // the content differs in this one part
    private int _builds;

    public SubAgentVersionTests()
    {
        _run.Sessions = [_run.Claude];
        _run.Transcripts.Next = (id, _) => id == "s1" ? ClaudeStore() : null;
    }

    public void Dispose() => _run.Dispose();

    // Every session's parser builds Content().
    private RunStore NewStore() =>
        new(_run.Repo.RepoPath, _run.Reader, (_, _) => new ContentParser(Content), stores: _run.Stores);

    // The same content on every call, in new arrays and new records: a foreground sub-agent with a call and a tool
    // call of its own (35.1, 35.2).
    private SessionContent Content()
    {
        _builds++;
        var survey = new SubAgent("toolu_s1", null, "toolu_s1", "Survey the parser module", "Survey the parser module",
            "Explore", "claude-haiku-4-5", false, "List the files in src/Alpha.", At(12, 0, 11), null,
            _change == "sub-agent-state" ? SessionState.Succeeded : SessionState.Running, null);
        SubAgent[] subAgents = _change == "added-sub-agent"
            ? [survey, new SubAgent("toolu_s2", null, "toolu_s2", "Survey CLI flags", "Survey CLI flags", "Explore",
                null, true, "List the CLI flags.", At(12, 0, 12), null, SessionState.Running, null)]
            : [survey];

        return new SessionContent("s1", "claude-sonnet-4-5", null,
            [
                new ModelCall("msg_1", "claude-sonnet-4-5", At(12, 0, 10), new TokenUsage(5, 900, 10, 40)),
                new ModelCall("msg_s1_1", "claude-haiku-4-5", At(12, 0, 12), new TokenUsage(3, 0, 4000, 20)) { AgentId = "toolu_s1" },
            ],
            [
                new ToolCall("msg_1", At(12, 0, 10), "toolu_s1", "Agent", """{"description":"Survey the parser module"}""",
                    "Survey the parser module", null),
                new ToolCall("msg_s1_1", At(12, 0, 12), "toolu_g1", "Glob", """{"pattern":"src/Alpha/**"}""", "src/Alpha/**",
                    new ToolResult(At(12, 0, 13), false, "src/Alpha/Parser.cs", null, null))
                {
                    AgentId = _change == "item-agent" ? null : "toolu_s1",
                },
            ],
            null, At(12, 0, 10), At(12, 0, 13), 0)
        {
            SubAgents = [.. subAgents],
        };
    }

    // The same store data on every call, with a new sub-agent dictionary holding new records (36.1).
    private StoreData ClaudeStore() => StoreData.Empty with
    {
        CliVersion = TestedVersions.ClaudeCode,
        SystemPrompt = ["You are Claude Code."],
        Calls = [new CallFigures("msg_1", At(12, 0, 10), new TokenUsage(5, 900, 10, 40), null, null, null, "tool_use")],
        SubAgents = new Dictionary<string, StoreData>
        {
            ["toolu_s1"] = StoreData.Empty with
            {
                SystemPrompt = ["You are a file search specialist."],
                Tools = [new ToolDefinition("Glob", "Finds files.", """{"type":"object"}""")],
                Calls =
                [
                    new CallFigures("msg_s1_1", At(12, 0, 12),
                        new TokenUsage(3, 0, 4000, _change == "store-figure" ? 21 : 20), null, null, null, "tool_use"),
                ],
            },
        }.ToImmutableDictionary(StringComparer.Ordinal),
    };

    [Fact]
    public void Equal_sub_agents_in_new_instances_keep_the_version()
    {
        using var store = NewStore();
        store.Poll();
        var first = store.Current;

        PollAgain(store);
        PollAgain(store);

        Assert.Equal(3, _builds);
        Assert.Equal(3, _run.Transcripts.Calls.Count);
        Assert.Same(first, store.Current);
        Assert.Equal(1, store.Current.Version);
        var session = SessionOf(store, _run.Claude);
        Assert.Single(session.Content.SubAgents);
        Assert.Equal("toolu_s1", Assert.Single(session.Stores.SubAgents).Key);
    }

    [Theory]
    [InlineData("sub-agent-state")]
    [InlineData("added-sub-agent")]
    [InlineData("item-agent")]
    [InlineData("store-figure")]
    public void A_change_of_a_sub_agent_raises_the_version_by_one(string change)
    {
        using var store = NewStore();
        store.Poll();
        PollAgain(store);

        _change = change;
        PollAgain(store);
        PollAgain(store);

        Assert.Equal(2, store.Current.Version);
    }

    // A new line makes the parser build its content again.
    private void PollAgain(RunStore store)
    {
        _run.Repo.Append(_run.Claude, "\n");
        store.Poll();
    }

    // Ignores its lines and builds the content it is given.
    private sealed class ContentParser(Func<SessionContent> build) : ISessionParser
    {
        public void AddLine(string line)
        {
        }

        public SessionContent Build() => build();
    }
}
