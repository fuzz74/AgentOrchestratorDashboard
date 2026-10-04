using System.Collections.Immutable;
using System.Text.RegularExpressions;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation;
using OrchDash.Tests.Host;
using OrchDash.Tests.Support;
using XenoAtom.Terminal;
using Xunit;

namespace OrchDash.Tests.Pages.Conversation;

public sealed partial class ConversationPageTests
{
    private const string AlphaWorkerRow = "✔ alpha worker #1 claude-sonnet-4-5 5m50s 2 tool calls 0.25 USD";
    private const string AlphaReviewRow = "✔ alpha reviewer #1.1 gpt-5.1 2m25s 1 tool call";
    private const string BetaWorkerRow = "▶ beta worker #1 claude-sonnet-4-5 19m55s 3 tool calls";
    private const string AlphaWorkerHeader = "Claude worker alpha claude-sonnet-4-5 ✔ succeeded";
    private const string AlphaReviewHeader = "Copilot reviewer alpha gpt-5.1 ✔ succeeded";
    private const string BetaWorkerHeader = "Claude worker beta claude-sonnet-4-5 ▶ running";
    private const string BetaLastEntry = "Bash dotnet build src/Beta running";

    private static UiTestHost Start(RunSnapshot? snapshot = null) =>
        UiTestHost.Start([new ConversationPage(), new SessionPickerPage()], snapshot ?? SampleRun.Create());

    [Fact]
    public void The_list_shows_the_sessions_in_order_with_their_row_fields()
    {
        using var host = Start();

        var list = PaneText(host, "Sessions");
        var worker = list.IndexOf(AlphaWorkerRow, StringComparison.Ordinal);
        var review = list.IndexOf(AlphaReviewRow, StringComparison.Ordinal);
        var beta = list.IndexOf(BetaWorkerRow, StringComparison.Ordinal);
        Assert.True(worker >= 0 && worker < review && review < beta, list);
        Assert.Equal(AlphaWorkerHeader, SelectedEntry(host));
        Assert.Contains("[Enter] Entries", host.Frame(), StringComparison.Ordinal);
        Assert.Contains("[Tab] Switch list", host.Frame(), StringComparison.Ordinal);
        host.SaveSvg("conversation");
    }

    [Fact]
    public void Down_selects_the_next_session_shows_its_entries_and_sets_the_selected_key()
    {
        using var host = Start();

        host.Press(TerminalKey.Down);

        Assert.StartsWith(AlphaReviewRow, SelectedSession(host), StringComparison.Ordinal);
        Assert.Contains(AlphaReviewHeader, PaneText(host, "Entries"), StringComparison.Ordinal);
        Assert.DoesNotContain(AlphaWorkerHeader, PaneText(host, "Entries"), StringComparison.Ordinal);
        host.Type('2');
        Assert.Contains($"selected key: {SampleRun.AlphaReviewKey}", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_on_a_session_shows_its_entries_and_sets_the_selected_key()
    {
        using var host = Start();

        host.ClickText("beta worker #1");

        Assert.StartsWith(BetaWorkerRow, SelectedSession(host), StringComparison.Ordinal);
        Assert.Contains(BetaWorkerHeader, PaneText(host, "Entries"), StringComparison.Ordinal);
        host.Type('2');
        Assert.Contains($"selected key: {SampleRun.BetaWorkerKey}", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_set_by_another_page_selects_that_session()
    {
        using var host = Start();
        host.Type('2');
        Assert.Contains("selected key: none", host.Frame(), StringComparison.Ordinal);

        host.Type('b');

        Assert.StartsWith(BetaWorkerRow, SelectedSession(host), StringComparison.Ordinal);
        Assert.Contains(BetaWorkerHeader, PaneText(host, "Entries"), StringComparison.Ordinal);
        Assert.Equal(BetaLastEntry, SelectedEntry(host));
    }

    [Fact]
    public void A_key_that_matches_no_session_shows_the_first_session()
    {
        using var host = Start();
        host.ClickText("beta worker #1");

        host.SetSnapshot(SampleRun.Create() with { Version = 2, Sessions = SampleRun.Create().Sessions.RemoveAt(2) });

        Assert.StartsWith(AlphaWorkerRow, SelectedSession(host), StringComparison.Ordinal);
        Assert.Contains(AlphaWorkerHeader, PaneText(host, "Entries"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_alpha_worker_session_shows_header_prompt_calls_thinking_tool_calls_and_result()
    {
        using var host = Start();

        var entries = EntryLines(host);

        Assert.Equal(
        [
            AlphaWorkerHeader,
            "session 0b7f3c2a-5d41-4e8a-9c11-2f6a8d3e4b70 · started 12:00:10 · 5m50s · 9 turns · 0.25 USD",
            "prompt 223 chars You are a worker agent of the orchestrator.",
            "Task: alpha - Alpha parser",
            "Implement the alpha parser in src/Alpha....",
            "call 1 · 12:00:10 · context 19.2k",
            "The task asks for a parser in src/Alpha.",
            "The spec says empty input gives null.",
            "There is a stub Parser class already....",
            "I will replace the Parser stub in src/Alpha/Parser.cs.",
            "Edit src/Alpha/Parser.cs ok 1s",
            "The file src/Alpha/Parser.cs has been updated.",
            "call 2 · 12:03:00 · context 43.3k",
            "thinking (~1.2k tokens, no text)",
            "Bash dotnet test tests/Alpha error 35s",
            "Build FAILED....",
            "The parser is in place and all alpha tests pass.",
            "result success done Added Parser with Parse and TryParse.",
            "Parse returns null on empty input.",
            "Added six tests in tests/Alpha/ParserTests.cs....",
        ], entries);
    }

    [Fact]
    public void The_review_session_shows_both_verdicts_the_issue_count_and_the_lines_not_understood()
    {
        using var host = Start();

        host.Press(TerminalKey.Down);

        var entries = EntryLines(host);
        Assert.Equal(AlphaReviewHeader, entries[0]);
        Assert.Contains("2 lines not understood", entries);
        Assert.Contains("call 1 · 12:06:30", entries);
        Assert.Contains("view src/Alpha/Parser.cs ok 1s", entries);
        Assert.Equal("result success spec pass quality fail 2 issues", entries[^1]);
    }

    [Fact]
    public void Tab_moves_the_keys_between_the_list_and_the_entries()
    {
        using var host = Start();

        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);

        Assert.StartsWith(AlphaWorkerRow, SelectedSession(host), StringComparison.Ordinal);
        Assert.StartsWith("prompt 223 chars", SelectedEntry(host), StringComparison.Ordinal);
        Assert.Contains("[End] Follow", host.Frame(), StringComparison.Ordinal);
        Assert.Contains("[Enter] Open", host.Frame(), StringComparison.Ordinal);

        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);

        Assert.StartsWith(AlphaReviewRow, SelectedSession(host), StringComparison.Ordinal);
    }

    [Fact]
    public void Enter_on_a_session_moves_the_keys_to_its_entries()
    {
        using var host = Start();

        host.Press(TerminalKey.Enter);
        host.Press(TerminalKey.End);

        Assert.StartsWith("result success done", SelectedEntry(host), StringComparison.Ordinal);
    }

    [Fact]
    public void Enter_on_the_header_opens_every_session_value()
    {
        using var host = Start();
        host.Press(TerminalKey.Tab);

        host.Press(TerminalKey.Enter);

        AssertPopup(host, SampleRun.AlphaWorkerKey, "Files", "Init", "Result");
        Assert.Contains("Key: " + SampleRun.AlphaWorkerKey, PopupLines(host));
    }

    [Fact]
    public void A_click_on_the_header_opens_every_session_value()
    {
        using var host = Start();

        host.ClickText(AlphaWorkerHeader);

        AssertPopup(host, SampleRun.AlphaWorkerKey, "Files", "Init");
        host.Press(TerminalKey.Escape);
        Assert.Equal(AlphaWorkerHeader, SelectedEntry(host));
    }

    [Fact]
    public void Enter_on_the_prompt_opens_the_whole_prompt()
    {
        using var host = Start();
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);

        host.Press(TerminalKey.Enter);

        AssertPopup(host, "Prompt", "Prompt");
        Assert.Contains("Acceptance: dotnet test tests/Alpha", PopupLines(host));
    }

    [Fact]
    public void A_click_on_the_prompt_opens_the_whole_prompt()
    {
        using var host = Start();

        host.ClickText("prompt 223 chars");

        AssertPopup(host, "Prompt", "Prompt");
        Assert.Contains("Acceptance: dotnet test tests/Alpha", PopupLines(host));
    }

    [Fact]
    public void Enter_on_a_tool_call_opens_its_input_result_and_diff()
    {
        using var host = Start();
        host.Press(TerminalKey.Tab);
        for (var i = 0; i < 5; i++)
        {
            host.Press(TerminalKey.Down);
        }
        Assert.Equal("Edit src/Alpha/Parser.cs ok 1s", SelectedEntry(host));

        host.Press(TerminalKey.Enter);

        AssertPopup(host, "Edit src/Alpha/Parser.cs", "Input", "Result", "Diff");
        Assert.Contains("+public class Parser", PopupLines(host));
        host.SaveSvg("conversation-tool-popup");
    }

    [Fact]
    public void A_click_on_a_tool_call_opens_its_input_result_and_diff()
    {
        using var host = Start();

        host.ClickText("Edit src/Alpha/Parser.cs ok");

        AssertPopup(host, "Edit src/Alpha/Parser.cs", "Input", "Result", "Diff");
        Assert.Contains("The file src/Alpha/Parser.cs has been updated.", PopupLines(host));
    }

    [Fact]
    public void Enter_on_the_review_result_opens_its_issues()
    {
        using var host = Start();
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.End);

        host.Press(TerminalKey.Enter);

        AssertPopup(host, "Result", "Text", "Structured");
        host.Press(TerminalKey.End);
        AssertPopup(host, "Result", "Issues");
        var lines = PopupLines(host);
        Assert.Contains("major · src/Alpha/Parser.cs · TryParse catches every exception and hides the cause.", lines);
        Assert.Contains("minor · No test covers input with only whitespace.", lines);
        host.SaveSvg("conversation-result-popup");
    }

    [Fact]
    public void A_click_on_the_review_result_opens_its_issues()
    {
        using var host = Start();
        host.ClickText("alpha reviewer");

        host.ClickText("result success spec pass");

        AssertPopup(host, "Result", "Text", "Structured");
        host.Wheel(80, 30, -100);
        AssertPopup(host, "Result", "Issues");
    }

    [Fact]
    public void An_entry_without_a_popup_opens_nothing()
    {
        using var host = Start();
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Down);
        Assert.Equal("call 1 · 12:00:10 · context 19.2k", SelectedEntry(host));

        host.Press(TerminalKey.Enter);

        Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_running_session_follows_new_entries_until_the_user_moves_up()
    {
        using var host = Start();
        host.ClickText("beta worker #1");
        host.Press(TerminalKey.Tab);
        Assert.Equal(BetaLastEntry, SelectedEntry(host));

        host.SetSnapshot(WithBetaText(2, "Step two."));
        Assert.Equal("Step two.", SelectedEntry(host));

        host.Press(TerminalKey.Up);
        host.SetSnapshot(WithBetaText(3, "Step two.", "Step three."));
        Assert.Equal(BetaLastEntry, SelectedEntry(host));

        host.Press(TerminalKey.End);
        Assert.Equal("Step three.", SelectedEntry(host));
        host.SetSnapshot(WithBetaText(4, "Step two.", "Step three.", "Step four."));
        Assert.Equal("Step four.", SelectedEntry(host));
    }

    [Fact]
    public void A_click_on_the_last_entry_resumes_following()
    {
        using var host = Start();
        host.ClickText("beta worker #1");
        host.SetSnapshot(WithBetaText(2, "Step two."));
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Up);
        host.SetSnapshot(WithBetaText(3, "Step two.", "Step three."));
        Assert.Equal(BetaLastEntry, SelectedEntry(host));

        host.ClickText("Step three.");
        AssertPopup(host, "Assistant text", "Text");
        host.Press(TerminalKey.Escape);
        host.SetSnapshot(WithBetaText(4, "Step two.", "Step three.", "Step four."));

        Assert.Equal("Step four.", SelectedEntry(host));
    }

    [Fact]
    public void A_new_snapshot_keeps_the_selected_session_and_entry()
    {
        using var host = Start();
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Down);
        Assert.StartsWith("call 1 · 12:06:30", SelectedEntry(host), StringComparison.Ordinal);

        host.SetSnapshot(WithBetaText(2, "Step two."));

        Assert.StartsWith(AlphaReviewRow, SelectedSession(host), StringComparison.Ordinal);
        Assert.StartsWith("call 1 · 12:06:30", SelectedEntry(host), StringComparison.Ordinal);
        host.Press(TerminalKey.Down);
        Assert.Equal("I need to read the parser and its tests.", SelectedEntry(host));
    }

    [Fact]
    public void The_wheel_scrolls_the_entries_and_a_new_snapshot_keeps_the_scroll_position()
    {
        using var host = Start(WithBetaSteps(1, 30));
        host.ClickText("beta worker #1");
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Home);
        Assert.Equal(BetaWorkerHeader, EntryLines(host)[0]);

        var entriesColumn = host.Frame().Split('\n').First(line => line.Contains("┌ Entries", StringComparison.Ordinal))
            .IndexOf("┌ Entries", StringComparison.Ordinal) + 10;
        host.Wheel(entriesColumn, 20, -5);
        var top = EntryLines(host)[0];
        Assert.NotEqual(BetaWorkerHeader, top);

        host.SetSnapshot(WithBetaSteps(2, 31));

        Assert.Equal(top, EntryLines(host)[0]);
        Assert.Null(SelectedEntry(host, visible: false));
    }

    [Fact]
    public void PageDown_and_PageUp_move_the_selection_by_a_page_and_keep_it_in_view()
    {
        using var host = Start(WithBetaSteps(1, 30));
        host.ClickText("beta worker #1");
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Home);

        host.Press(TerminalKey.PageDown);

        var paged = SelectedEntry(host);
        Assert.StartsWith("Bash step ", paged, StringComparison.Ordinal);
        Assert.NotEqual(BetaWorkerHeader, EntryLines(host)[0]);
        host.Press(TerminalKey.PageDown);
        Assert.NotEqual(paged, SelectedEntry(host));

        host.Press(TerminalKey.PageUp);
        host.Press(TerminalKey.PageUp);

        Assert.Equal(BetaWorkerHeader, SelectedEntry(host));
        Assert.Equal(BetaWorkerHeader, EntryLines(host)[0]);
    }

    [Fact]
    public void Following_scrolls_the_newest_entry_into_view()
    {
        using var host = Start(WithBetaSteps(1, 30));
        host.Type('2');

        host.Type('b');

        Assert.Equal("Bash step 30 ok 1s", SelectedEntry(host));
        host.SetSnapshot(WithBetaSteps(2, 35));
        Assert.Equal("Bash step 35 ok 1s", SelectedEntry(host));
        Assert.Equal("out 35", EntryLines(host)[^1]);
    }

    [Fact]
    public void A_session_without_an_event_log_shows_header_prompt_and_no_event_log()
    {
        var snapshot = SampleRun.Create();
        var worker = snapshot.Sessions[0];
        var withoutLog = worker with { Files = worker.Files with { HasEventsFile = false }, Content = SessionContent.Empty };
        using var host = Start(snapshot with { Sessions = snapshot.Sessions.SetItem(0, withoutLog) });

        var entries = EntryLines(host);

        Assert.Equal("Claude worker alpha ✔ succeeded", entries[0]);
        Assert.StartsWith("prompt 223 chars", entries[2], StringComparison.Ordinal);
        Assert.Equal("no event log", entries[^1]);
    }

    [Fact]
    public void A_run_without_sessions_shows_no_sessions_yet()
    {
        using var host = Start(SampleRun.Create() with { Sessions = [] });

        Assert.Equal("No sessions yet", PaneLines(host, "Sessions")[0]);
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Enter);
        Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
    }

    /// <summary>The sample run with assistant texts added to the beta worker session.</summary>
    private static RunSnapshot WithBetaText(long version, params string[] texts) =>
        WithBetaItems(version, [.. texts.Select(text => new AssistantText(null, SampleRun.At(12, 29, 40), text))]);

    /// <summary>The sample run with <paramref name="count"/> finished tool calls added to the beta worker session.</summary>
    private static RunSnapshot WithBetaSteps(long version, int count) =>
        WithBetaItems(version, [.. Enumerable.Range(1, count).Select(i => new ToolCall(null, SampleRun.At(12, 29, 40),
            $"toolu_step{i}", "Bash", "{}", $"Bash step {i}", new ToolResult(SampleRun.At(12, 29, 41), false, $"out {i}", null, null)))]);

    private static RunSnapshot WithBetaItems(long version, ImmutableArray<ConversationItem> items)
    {
        var snapshot = SampleRun.Create();
        var beta = snapshot.Sessions[2];
        var longer = beta with { Content = beta.Content with { Items = beta.Content.Items.AddRange(items) } };
        return snapshot with { Version = version, Sessions = snapshot.Sessions.SetItem(2, longer) };
    }

    /// <summary>The text rows inside the pane with that title, without its borders and scroll bar.</summary>
    private static string[] PaneLines(UiTestHost host, string title)
    {
        var lines = host.Frame().Split('\n');
        var top = Array.FindIndex(lines, line => line.Contains($"┌ {title} ", StringComparison.Ordinal));
        Assert.True(top >= 0, host.Frame());
        var left = lines[top].IndexOf($"┌ {title} ", StringComparison.Ordinal);
        var right = lines[top].IndexOf('┐', left);
        var rows = new List<string>();
        for (var row = top + 1; row < lines.Length && lines[row].Length > left && lines[row][left] == '│'; row++)
        {
            var line = lines[row];
            rows.Add(line.Length <= left + 1 ? "" : line[(left + 1)..Math.Min(right - 1, line.Length)].TrimEnd());
        }
        return [.. rows];
    }

    /// <summary>The non-empty rows of the entries pane without the selection marker column.</summary>
    private static string[] EntryLines(UiTestHost host) =>
        [.. PaneLines(host, "Entries").Where(line => line.Trim().Length > 0).Select(line => line[2..])];

    /// <summary>The pane's rows without the marker column, joined into one line, so that wrapped rows read as one.</summary>
    private static string PaneText(UiTestHost host, string title) =>
        Spaces().Replace(string.Join(' ', PaneLines(host, title).Select(line => line.Length > 2 ? line[2..] : "")), " ");

    /// <summary>The first row of the selected item in a pane: the row with the arrow.</summary>
    private static string? SelectedRow(UiTestHost host, string title, bool visible = true)
    {
        var row = PaneLines(host, title).FirstOrDefault(line => line.StartsWith('→'));
        if (visible)
        {
            Assert.True(row is not null, host.Frame());
        }
        return row?[2..];
    }

    private static string SelectedSession(UiTestHost host)
    {
        // A wrapped row continues on the next lines, up to the next row, which starts with a state icon.
        var lines = PaneLines(host, "Sessions");
        var start = Array.FindIndex(lines, line => line.StartsWith('→'));
        Assert.True(start >= 0, host.Frame());
        var parts = new List<string> { lines[start][2..] };
        for (var i = start + 1; i < lines.Length && lines[i].Length > 2 && !"✔▶✖◌".Contains(lines[i][2], StringComparison.Ordinal); i++)
        {
            parts.Add(lines[i].Trim());
        }
        return string.Join(' ', parts);
    }

    private static string? SelectedEntry(UiTestHost host, bool visible = true)
    {
        Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
        return SelectedRow(host, "Entries", visible);
    }

    /// <summary>The text rows of the open pop-up, without borders and scroll bar.</summary>
    private static string[] PopupLines(UiTestHost host)
    {
        var lines = host.Frame().Split('\n');
        var top = Array.FindIndex(lines, line => line.Contains('┌') && line.Contains("[X]", StringComparison.Ordinal));
        Assert.True(top >= 0, host.Frame());
        var left = lines[top].IndexOf('┌');
        var right = lines[top].IndexOf('┐', left);
        var rows = new List<string>();
        for (var row = top + 1; row < lines.Length && lines[row].Length > left && lines[row][left] == '│'; row++)
        {
            rows.Add(lines[row][(left + 1)..(right - 1)].TrimEnd());
        }
        return [.. rows];
    }

    private static void AssertPopup(UiTestHost host, string title, params string[] headings)
    {
        var lines = host.Frame().Split('\n');
        Assert.Contains(lines, line => line.Contains($"┌ {title} ", StringComparison.Ordinal) && line.Contains("[X]", StringComparison.Ordinal));
        var popup = PopupLines(host);
        var positions = headings.Select(heading => Array.IndexOf(popup, heading)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0, host.Frame()));
        Assert.Equal(positions.Order(), positions);
    }

    [GeneratedRegex(" +")]
    private static partial Regex Spaces();
}
