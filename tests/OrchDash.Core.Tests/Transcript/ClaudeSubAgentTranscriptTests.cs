using OrchDash.Core.Model;
using OrchDash.Core.Transcript;
using Xunit;
using static OrchDash.Core.Tests.Transcript.TranscriptLines;

namespace OrchDash.Core.Tests.Transcript;

/// <summary>The sub-agent transcripts in &lt;folder&gt;/&lt;sessionId&gt;/subagents (spec 36.1).</summary>
public sealed class ClaudeSubAgentTranscriptTests : IDisposable
{
    private const string SubAgentSnapshot =
        """{"type":"attachment","isSidechain":true,"agentId":"a1","attachment":{"type":"prompt_snapshot","systemPrompt":["You explore."],"tools":[{"name":"Glob"},{"name":"Read"}]}}""";

    private readonly TempProjects _projects = new();
    private readonly ClaudeTranscriptStore _store;

    public ClaudeSubAgentTranscriptTests() => _store = new ClaudeTranscriptStore(_projects.ProjectsDir);

    public void Dispose() => _projects.Dispose();

    private string WriteMain(params string[] lines) =>
        _projects.Write(Folder, SessionId, Lines([Assistant("msg_1", Time1, Usage(1, 2, 3, 4, 0)), .. lines]));

    private string WriteSubAgent(string x, string toolUseId, params string[] lines) =>
        _projects.WriteSubAgent(Folder, SessionId, x, Lines(lines), SubAgentMeta(toolUseId));

    private static string SubAgentCall(string id, string time = Time1) =>
        Assistant(id, time, Usage(3, 0, 4000, 20, 0), isSidechain: true);

    private StoreData Read()
    {
        var data = _store.Read(SessionId, WorkDir);
        Assert.NotNull(data);
        return data;
    }

    [Fact]
    public void A_transcript_with_a_meta_file_maps_into_the_entry_of_its_tool_use_id_with_sidechain_lines()
    {
        WriteMain(Assistant("msg_side", Time2, Usage(9, 9, 9, 9, 0), isSidechain: true));
        WriteSubAgent("a1b2c3", "toolu_s1",
            SubAgentSnapshot,
            Assistant("msg_s1_1", Time1, Usage(3, 0, 4000, 10, 0), isSidechain: true),
            Assistant("msg_s1_1", Time1, Usage(3, 0, 4000, 20, 5), isSidechain: true),
            Assistant("msg_s1_2", Time2, Usage(1, 4000, 50, 30, 0), stopReason: "end_turn", isSidechain: true));

        var data = Read();

        var call = Assert.Single(data.Calls);
        Assert.Equal("msg_1", call.CallId);
        Assert.Null(call.AgentId);
        var (toolUseId, sub) = Assert.Single(data.SubAgents);
        Assert.Equal("toolu_s1", toolUseId);
        Assert.Equal(
            [
                new CallFigures("msg_s1_1", At1, new TokenUsage(3, 0, 4000, 20), 5, null, null, "tool_use") { AgentId = "toolu_s1" },
                new CallFigures("msg_s1_2", At2, new TokenUsage(1, 4000, 50, 30), 0, null, null, "end_turn") { AgentId = "toolu_s1" },
            ],
            sub.Calls);
        Assert.Equal(["You explore."], sub.SystemPrompt);
        Assert.Equal(["Glob", "Read"], sub.Tools.Select(tool => tool.Name));
        Assert.Same(StoreData.NoSubAgents, sub.SubAgents);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""["toolu_s2"]""")]
    [InlineData("""{"agentType":"Explore"}""")]
    [InlineData("""{"toolUseId":7}""")]
    [InlineData("""{"toolUseId":null}""")]
    [InlineData("""{"toolUseId":""}""")]
    public void A_transcript_whose_meta_file_names_no_tool_use_id_is_skipped(string? metaJson)
    {
        WriteMain();
        WriteSubAgent("a1", "toolu_s1", SubAgentCall("msg_s1_1"));
        _projects.WriteSubAgent(Folder, SessionId, "b2", Lines(SubAgentCall("msg_s2_1")), metaJson);

        var data = Read();

        Assert.Equal(["toolu_s1"], data.SubAgents.Keys);
    }

    [Fact]
    public void A_meta_file_that_cannot_be_opened_skips_the_transcript()
    {
        WriteMain();
        var path = WriteSubAgent("a1", "toolu_s1", SubAgentCall("msg_s1_1"));

        using (TempProjects.Hold(TempProjects.MetaPath(path)))
            Assert.Same(StoreData.NoSubAgents, Read().SubAgents);
    }

    [Fact]
    public void The_meta_file_is_read_again_only_when_the_transcript_changes()
    {
        WriteMain();
        var path = _projects.WriteSubAgent(Folder, SessionId, "a1", Lines(SubAgentCall("msg_s1_1")), metaJson: null);
        var first = Read();
        Assert.Empty(first.SubAgents);

        TempProjects.WriteMeta(path, SubAgentMeta("toolu_s1"));
        Assert.Same(first, Read());

        TempProjects.Append(path, Lines(SubAgentCall("msg_s1_2", Time2)));
        Assert.Equal(["msg_s1_1", "msg_s1_2"], Read().SubAgents["toolu_s1"].Calls.Select(call => call.CallId));
    }

    [Fact]
    public void Without_sub_agent_entries_the_sub_agents_are_the_shared_empty_dictionary()
    {
        WriteMain();
        var first = Read();
        Assert.Same(StoreData.NoSubAgents, first.SubAgents);

        Directory.CreateDirectory(Path.Combine(_projects.ProjectsDir, Folder, SessionId, "subagents"));
        Assert.Same(first, Read());

        _projects.WriteSubAgent(Folder, SessionId, "a1", Lines(SubAgentCall("msg_s1_1")), metaJson: null);
        Assert.Same(StoreData.NoSubAgents, Read().SubAgents);
    }

    [Fact]
    public void Nothing_changed_gives_the_same_instance()
    {
        WriteMain();
        WriteSubAgent("a1", "toolu_s1", SubAgentCall("msg_s1_1"));
        WriteSubAgent("b2", "toolu_s2", SubAgentCall("msg_s2_1"));

        var first = Read();

        Assert.Equal(2, first.SubAgents.Count);
        Assert.Same(first, Read());
    }

    [Fact]
    public void A_grown_sub_agent_transcript_gives_a_new_instance_with_the_last_main_data()
    {
        WriteMain();
        var path = WriteSubAgent("a1", "toolu_s1", SubAgentCall("msg_s1_1"));
        var first = Read();

        TempProjects.Append(path, Lines(SubAgentCall("msg_s1_2", Time2)));
        var second = Read();

        Assert.NotSame(first, second);
        // Record equality compares the arrays by reference, so this holds only when the main file was not mapped again.
        Assert.Equal(first with { SubAgents = StoreData.NoSubAgents }, second with { SubAgents = StoreData.NoSubAgents });
        Assert.Equal(["msg_s1_1", "msg_s1_2"], second.SubAgents["toolu_s1"].Calls.Select(call => call.CallId));
        Assert.Same(second, Read());
    }

    [Fact]
    public void An_added_sub_agent_transcript_gives_a_new_instance()
    {
        WriteMain();
        WriteSubAgent("a1", "toolu_s1", SubAgentCall("msg_s1_1"));
        var first = Read();

        WriteSubAgent("b2", "toolu_s2", SubAgentCall("msg_s2_1"));
        var second = Read();

        Assert.NotSame(first, second);
        Assert.Equal(["toolu_s1", "toolu_s2"], second.SubAgents.Keys.Order(StringComparer.Ordinal));
        Assert.Same(first.SubAgents["toolu_s1"], second.SubAgents["toolu_s1"]);
    }

    [Fact]
    public void A_removed_sub_agent_transcript_drops_its_entry()
    {
        WriteMain();
        var path = WriteSubAgent("a1", "toolu_s1", SubAgentCall("msg_s1_1"));
        var first = Read();

        File.Delete(path);
        var second = Read();

        Assert.NotSame(first, second);
        Assert.Same(StoreData.NoSubAgents, second.SubAgents);
        Assert.Equal(first.Calls, second.Calls);
    }

    [Fact]
    public void A_changed_main_transcript_keeps_the_unchanged_sub_agent_entries()
    {
        var mainPath = WriteMain();
        WriteSubAgent("a1", "toolu_s1", SubAgentCall("msg_s1_1"));
        var first = Read();

        TempProjects.Append(mainPath, Lines(Assistant("msg_2", Time2, Usage(1, 2, 3, 5, 0))));
        var second = Read();

        Assert.Equal(["msg_1", "msg_2"], second.Calls.Select(call => call.CallId));
        Assert.Same(first.SubAgents, second.SubAgents);
    }

    [Fact]
    public void A_sub_agent_transcript_that_cannot_be_opened_keeps_its_last_value()
    {
        WriteMain();
        var path = WriteSubAgent("a1", "toolu_s1", SubAgentCall("msg_s1_1"));
        var first = Read();

        TempProjects.Append(path, Lines(SubAgentCall("msg_s1_2", Time2)));
        using (TempProjects.Hold(path))
            Assert.Same(first, Read());

        Assert.Equal(2, Read().SubAgents["toolu_s1"].Calls.Length);
    }

    [Fact]
    public void A_sub_agent_transcript_that_cannot_be_opened_on_its_first_read_has_no_entry_until_it_can()
    {
        WriteMain();
        var path = WriteSubAgent("a1", "toolu_s1", SubAgentCall("msg_s1_1"));

        using (TempProjects.Hold(path))
            Assert.Empty(Read().SubAgents);

        Assert.Equal(["toolu_s1"], Read().SubAgents.Keys);
    }

    [Fact]
    public void Of_two_transcripts_naming_one_tool_use_id_the_first_in_path_order_gives_the_entry()
    {
        WriteMain();
        WriteSubAgent("b2", "toolu_s1", SubAgentCall("msg_b2"));
        WriteSubAgent("a1", "toolu_s1", SubAgentCall("msg_a1"));

        var data = Read();

        Assert.Equal("msg_a1", Assert.Single(Assert.Single(data.SubAgents).Value.Calls).CallId);
    }

    [Fact]
    public void A_sub_agent_transcript_is_read_with_sharing_so_a_writer_can_keep_it_open()
    {
        WriteMain();
        var path = WriteSubAgent("a1", "toolu_s1");
        using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete);
        writer.Write(System.Text.Encoding.UTF8.GetBytes(Lines(SubAgentCall("msg_s1_1"))));
        writer.Flush();

        var data = Read();

        Assert.Equal("msg_s1_1", Assert.Single(data.SubAgents["toolu_s1"].Calls).CallId);
    }
}
