using System.Text;
using OrchDash.Core.SessionFolder;
using Xunit;

namespace OrchDash.Core.Tests.SessionFolder;

/// <summary>Read on events.jsonl files the tests write (spec 12.1, 12.2, 36.6).</summary>
public sealed class CopilotFolderReadTests : IDisposable
{
    private const string Id = "11111111-2222-3333-4444-555555555555";

    private const string Start =
        """{"type":"session.start","data":{"sessionId":"x","copilotVersion":"1.0.91"},"id":"1","timestamp":"2026-10-03T10:00:00.000Z","parentId":null}""";

    private const string WithBlocks =
        """{"type":"system.message","data":{"role":"system","content":"whole","contentBlocks":[{"content":"first","isStatic":true},{"content":"second"}]},"id":"2","timestamp":"2026-10-03T10:00:01.000Z","parentId":"1"}""";

    private const string WithoutBlocks =
        """{"type":"system.message","data":{"role":"system","content":"only content"},"id":"3","timestamp":"2026-10-03T10:00:02.000Z","parentId":"2"}""";

    private const string Other =
        """{"type":"assistant.turn_start","data":{"turnId":"0"},"id":"4","timestamp":"2026-10-03T10:00:03.000Z","parentId":"3"}""";

    private const string FromSubAgent =
        """{"type":"system.message","agentId":"agent-p1","data":{"role":"system","content":"sub-agent","contentBlocks":[{"content":"sub-agent block"}]},"id":"5","timestamp":"2026-10-03T10:00:04.000Z","parentId":"4"}""";

    private readonly TempSessionState _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Session_start_gives_the_version_and_the_blocks_give_the_system_prompt()
    {
        _temp.WriteEvents(Id, Lines(Start, WithBlocks));

        var data = new CopilotFolderStore(_temp.Dir).Read(Id, null);

        Assert.NotNull(data);
        Assert.Equal("1.0.91", data.CliVersion);
        Assert.Equal(["first", "second"], data.SystemPrompt);
        Assert.Equal(0, data.UnparsedLines);
        Assert.Empty(data.Tools);
        Assert.Empty(data.Injected);
        Assert.Empty(data.Calls);
        Assert.Null(data.CostUsd);
        Assert.Null(data.LinesAdded);
        Assert.Null(data.LinesRemoved);
    }

    [Fact]
    public void Without_blocks_the_content_is_the_one_block()
    {
        _temp.WriteEvents(Id, Lines(WithoutBlocks));

        var data = new CopilotFolderStore(_temp.Dir).Read(Id, null);

        Assert.NotNull(data);
        Assert.Null(data.CliVersion);
        Assert.Equal(["only content"], data.SystemPrompt);
    }

    [Fact]
    public void An_empty_block_list_also_gives_the_content()
    {
        _temp.WriteEvents(Id, Lines("""{"type":"system.message","data":{"content":"fallback","contentBlocks":[]},"id":"2","timestamp":"t","parentId":"1"}"""));

        var data = new CopilotFolderStore(_temp.Dir).Read(Id, null);

        Assert.Equal(["fallback"], data!.SystemPrompt);
    }

    [Fact]
    public void The_latest_system_message_wins()
    {
        _temp.WriteEvents(Id, Lines(Start, WithBlocks, WithoutBlocks));
        Assert.Equal(["only content"], new CopilotFolderStore(_temp.Dir).Read(Id, null)!.SystemPrompt);

        _temp.WriteEvents(Id, Lines(Start, WithoutBlocks, WithBlocks));
        Assert.Equal(["first", "second"], new CopilotFolderStore(_temp.Dir).Read(Id, null)!.SystemPrompt);
    }

    [Fact]
    public void A_system_message_with_an_agent_id_does_not_replace_the_system_prompt()
    {
        _temp.WriteEvents(Id, Lines(Start, WithBlocks, FromSubAgent));
        Assert.Equal(["first", "second"], new CopilotFolderStore(_temp.Dir).Read(Id, null)!.SystemPrompt);

        _temp.WriteEvents(Id, Lines(Start, FromSubAgent));
        Assert.Empty(new CopilotFolderStore(_temp.Dir).Read(Id, null)!.SystemPrompt);
    }

    [Fact]
    public void A_later_system_message_of_the_agent_itself_still_wins_after_one_with_an_agent_id()
    {
        _temp.WriteEvents(Id, Lines(Start, WithBlocks, FromSubAgent, WithoutBlocks));
        Assert.Equal(["only content"], new CopilotFolderStore(_temp.Dir).Read(Id, null)!.SystemPrompt);

        var emptyAgentId = WithBlocks.Replace("\"type\":\"system.message\",", "\"type\":\"system.message\",\"agentId\":\"\",");
        _temp.WriteEvents(Id, Lines(Start, WithoutBlocks, FromSubAgent, emptyAgentId));
        Assert.Equal(["first", "second"], new CopilotFolderStore(_temp.Dir).Read(Id, null)!.SystemPrompt);
    }

    [Fact]
    public void An_appended_system_message_with_an_agent_id_keeps_the_system_prompt()
    {
        _temp.WriteEvents(Id, Lines(Start, WithBlocks));
        var store = new CopilotFolderStore(_temp.Dir);
        Assert.Equal(["first", "second"], store.Read(Id, null)!.SystemPrompt);

        _temp.AppendEvents(Id, Lines(FromSubAgent));
        var data = store.Read(Id, null);

        Assert.Equal(["first", "second"], data!.SystemPrompt);
        Assert.Equal(0, data.UnparsedLines);
    }

    [Fact]
    public void A_line_that_is_not_a_json_object_counts_as_unparsed()
    {
        _temp.WriteEvents(Id, Lines(Start, "not json", "[1,2]", "\"text\"", WithBlocks));

        var data = new CopilotFolderStore(_temp.Dir).Read(Id, null);

        Assert.Equal(3, data!.UnparsedLines);
        Assert.Equal("1.0.91", data.CliVersion);
        Assert.Equal(["first", "second"], data.SystemPrompt);
    }

    [Fact]
    public void An_unknown_type_or_an_object_without_type_is_skipped_without_counting()
    {
        _temp.WriteEvents(Id, Lines(Start, Other, """{"data":{}}""", WithBlocks));

        var data = new CopilotFolderStore(_temp.Dir).Read(Id, null);

        Assert.Equal(0, data!.UnparsedLines);
        Assert.Equal(["first", "second"], data.SystemPrompt);
    }

    [Fact]
    public void A_last_line_without_a_line_break_is_not_read()
    {
        _temp.WriteEvents(Id, Lines(Start, WithBlocks) + WithoutBlocks);

        var data = new CopilotFolderStore(_temp.Dir).Read(Id, null);

        Assert.Equal(["first", "second"], data!.SystemPrompt);
        Assert.Equal(0, data.UnparsedLines);
    }

    [Fact]
    public void An_incomplete_last_line_is_not_counted_as_unparsed()
    {
        _temp.WriteEvents(Id, Lines(Start) + WithBlocks[..20]);

        var data = new CopilotFolderStore(_temp.Dir).Read(Id, null);

        Assert.Equal(0, data!.UnparsedLines);
        Assert.Empty(data.SystemPrompt);
    }

    [Fact]
    public void Lines_ended_by_crlf_and_a_byte_order_mark_are_read()
    {
        _temp.WriteEvents(Id, "\uFEFF" + Start + "\r\n" + WithBlocks + "\r\n");

        var data = new CopilotFolderStore(_temp.Dir).Read(Id, null);

        Assert.Equal("1.0.91", data!.CliVersion);
        Assert.Equal(["first", "second"], data.SystemPrompt);
        Assert.Equal(0, data.UnparsedLines);
    }

    [Fact]
    public void A_missing_file_gives_null()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Dir, Id));
        var store = new CopilotFolderStore(_temp.Dir);

        Assert.Null(store.Read(Id, null));
        Assert.Null(store.Read("no-such-session", null));
    }

    [Fact]
    public void A_missing_session_state_folder_gives_null()
    {
        var store = new CopilotFolderStore(Path.Combine(_temp.Dir, "missing"));

        Assert.Null(store.Read(Id, null));
    }

    [Fact]
    public void A_file_that_was_deleted_gives_null()
    {
        _temp.WriteEvents(Id, Lines(Start));
        var store = new CopilotFolderStore(_temp.Dir);
        Assert.NotNull(store.Read(Id, null));

        File.Delete(_temp.EventsPath(Id));

        Assert.Null(store.Read(Id, null));
    }

    [Fact]
    public void The_same_instance_comes_back_while_the_file_is_unchanged()
    {
        _temp.WriteEvents(Id, Lines(Start, WithBlocks));
        var store = new CopilotFolderStore(_temp.Dir);

        var first = store.Read(Id, null);
        var second = store.Read(Id, "C:\\other");

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void A_new_instance_comes_back_after_an_append()
    {
        _temp.WriteEvents(Id, Lines(Start, WithBlocks));
        var store = new CopilotFolderStore(_temp.Dir);
        var first = store.Read(Id, null);

        _temp.AppendEvents(Id, Lines(WithoutBlocks));
        var second = store.Read(Id, null);

        Assert.NotSame(first, second);
        Assert.Equal(["only content"], second!.SystemPrompt);
    }

    [Fact]
    public void The_last_result_comes_back_while_the_file_is_locked()
    {
        _temp.WriteEvents(Id, Lines(Start, WithBlocks));
        var store = new CopilotFolderStore(_temp.Dir);
        var first = store.Read(Id, null);
        _temp.AppendEvents(Id, Lines(WithoutBlocks));

        using (new FileStream(_temp.EventsPath(Id), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Same(first, store.Read(Id, null));

        Assert.Equal(["only content"], store.Read(Id, null)!.SystemPrompt);
    }

    [Fact]
    public void A_locked_file_without_an_earlier_result_gives_null()
    {
        _temp.WriteEvents(Id, Lines(Start, WithBlocks));
        var store = new CopilotFolderStore(_temp.Dir);

        using var locked = new FileStream(_temp.EventsPath(Id), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Null(store.Read(Id, null));
    }

    [Fact]
    public void A_line_completed_by_a_later_append_is_read()
    {
        _temp.WriteEvents(Id, Lines(Start) + WithBlocks[..30]);
        var store = new CopilotFolderStore(_temp.Dir);
        var first = store.Read(Id, null);
        Assert.Empty(first!.SystemPrompt);

        _temp.AppendEvents(Id, WithBlocks[30..] + "\n");
        var second = store.Read(Id, null);

        Assert.Equal("1.0.91", second!.CliVersion);
        Assert.Equal(["first", "second"], second.SystemPrompt);
        Assert.Equal(0, second.UnparsedLines);
    }

    [Fact]
    public void Appended_lines_add_to_what_was_read_before()
    {
        _temp.WriteEvents(Id, Lines(Start, "not json"));
        var store = new CopilotFolderStore(_temp.Dir);
        Assert.Equal(1, store.Read(Id, null)!.UnparsedLines);

        _temp.AppendEvents(Id, Lines("also not json", WithoutBlocks));
        var data = store.Read(Id, null);

        Assert.Equal("1.0.91", data!.CliVersion);
        Assert.Equal(["only content"], data.SystemPrompt);
        Assert.Equal(2, data.UnparsedLines);
    }

    [Fact]
    public void A_file_replaced_by_a_shorter_one_is_read_from_the_start()
    {
        _temp.WriteEvents(Id, Lines(Start, WithBlocks, "not json"));
        var store = new CopilotFolderStore(_temp.Dir);
        Assert.Equal(1, store.Read(Id, null)!.UnparsedLines);

        _temp.WriteEvents(Id, Lines(WithoutBlocks));
        var data = store.Read(Id, null);

        Assert.Null(data!.CliVersion);
        Assert.Equal(["only content"], data.SystemPrompt);
        Assert.Equal(0, data.UnparsedLines);
    }

    [Fact]
    public void A_string_with_an_unpaired_surrogate_is_read_without_an_exception()
    {
        _temp.WriteEvents(Id, Lines(
            """{"type":"session.start","data":{"copilotVersion":"\ud800"},"id":"1","timestamp":"t","parentId":null}""",
            """{"type":"system.message","data":{"content":"whole","contentBlocks":[{"content":"\ud800"},{"content":"kept"}]},"id":"2","timestamp":"t","parentId":"1"}""",
            """{"type":"system.message","data":{"content":"\udc00x"},"id":"3","timestamp":"t","parentId":"2"}""",
            WithBlocks.Replace("\"first\"", "\"\\ud800\"")));

        var data = new CopilotFolderStore(_temp.Dir).Read(Id, null);

        Assert.NotNull(data);
        Assert.Null(data.CliVersion);
        Assert.Equal(["second"], data.SystemPrompt);
    }

    [Fact]
    public void A_content_with_an_unpaired_surrogate_gives_no_block()
    {
        _temp.WriteEvents(Id, Lines(Start, """{"type":"system.message","data":{"content":"\ud800"},"id":"2","timestamp":"t","parentId":"1"}"""));

        var data = new CopilotFolderStore(_temp.Dir).Read(Id, null);

        Assert.NotNull(data);
        Assert.Equal("1.0.91", data.CliVersion);
        Assert.Empty(data.SystemPrompt);
    }

    [Fact]
    public void Bytes_that_are_not_utf8_do_not_throw()
    {
        var bytes = new List<byte>(Encoding.UTF8.GetBytes(Lines(Start)));
        bytes.AddRange(Encoding.UTF8.GetBytes("""{"type":"system.message","data":{"content":"a"""));
        bytes.AddRange([0xFF, 0xFE, 0xC3]);
        bytes.AddRange(Encoding.UTF8.GetBytes("\"},\"id\":\"2\",\"timestamp\":\"t\",\"parentId\":\"1\"}\n"));
        _temp.WriteEventsBytes(Id, [.. bytes]);

        var data = new CopilotFolderStore(_temp.Dir).Read(Id, null);

        Assert.NotNull(data);
        Assert.Equal("1.0.91", data.CliVersion);
    }

    private static string Lines(params string[] lines) => string.Concat(lines.Select(line => line + "\n"));
}
