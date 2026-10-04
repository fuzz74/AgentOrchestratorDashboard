using System.Text.RegularExpressions;
using OrchDash.Core.Model;
using OrchDash.Pages.ContextWindow;
using OrchDash.Shell;
using OrchDash.Tests.Host;
using OrchDash.Tests.Support;
using XenoAtom.Terminal;
using Xunit;

namespace OrchDash.Tests.Pages.ContextWindow;

public sealed class ContextPageTests
{
    private const string AlphaWorkerRow = "✔ alpha  worker     #1     43.3k";
    private const string AlphaReviewRow = "✔ alpha  reviewer   #1.1   17.0k";
    private const string Gamma1Row = "✖ gamma  worker     #1     26.5k";
    private const string Gamma2Row = "✖ gamma  worker     #2     33.5k";
    private const string BetaRow = "▶ beta   worker     #1     34.5k";
    private const string AlphaWorkerHeader =
        "Claude · worker · alpha · claude-sonnet-4-5 · 2 calls · peak 43.3k · context 43.3k of 200.0k (22 %)";
    private const string AlphaReviewHeader = "Copilot · reviewer · alpha · gpt-5.1 · 2 calls · peak 17.0k · context 17.0k";

    private static UiTestHost Start(RunSnapshot? snapshot = null) =>
        UiTestHost.Start([new ContextPage(), new KeyPickerPage()], snapshot ?? SampleRun.CreateEnriched());

    [Fact]
    public void The_page_shows_the_sessions_and_the_context_of_the_first_session()
    {
        using var host = Start();

        Assert.Equal([AlphaWorkerRow, AlphaReviewRow, Gamma1Row, Gamma2Row, BetaRow], Rows(host, "Sessions"));
        Assert.Equal(AlphaWorkerRow, Selected(host, "Sessions"));
        Assert.Contains(AlphaWorkerHeader, host.Frame(), StringComparison.Ordinal);
        Assert.Contains("┌ Context per call ", host.Frame(), StringComparison.Ordinal);
        Assert.Equal(
        [
            "call 1  12:00:10   19.2k   +19.2k  out 640     think 210     tool_use",
            "call 2  12:03:00   43.3k   +24.1k  out 2.5k    think 1.2k    end_turn",
        ], Rows(host, "Calls"));
        Assert.StartsWith("call 2", Selected(host, "Calls"), StringComparison.Ordinal);
        var makeup = PaneLines(host, "Make-up at call 2");
        Assert.Equal(
        [
            "System prompt       3.3k    8 %     213 chars  2 parts   est.",
            "Tool definitions    9.0k   21 %     580 chars  2 parts   est.",
            "Injected            7.4k   17 %     303 chars  3 parts   est.",
            "Prompt              3.5k    8 %     223 chars  1 part    est.",
            "Conversation       20.1k   46 %     405 chars  3 parts   est.",
        ], makeup[^5..]);
        Assert.Equal(
        [
            "System prompt     block 1                      ~883      57 chars  call 1",
            "System prompt     block 2                     ~2.4k     156 chars  call 1",
            "Tool definitions  Read                        ~3.8k     242 chars  call 1",
            "Tool definitions  Bash                        ~5.2k     338 chars  call 1",
            "Injected          skill_listing               ~1.5k      94 chars  call 1",
            "Injected          nested_memory               ~2.0k     129 chars  call 1",
            "Prompt            prompt #1                   ~3.5k     223 chars  call 1",
            "Injected          total_tokens_reminder       ~4.0k      80 chars  call 2",
            "Conversation      thinking                    ~7.4k     149 chars  call 2",
            "Conversation      text                        ~2.7k      54 chars  call 2",
            "Conversation      Edit src/Alpha/Parser.cs   ~10.0k     202 chars  call 2",
        ], Rows(host, "Parts"));
        Assert.StartsWith("System prompt     block 1", Selected(host, "Parts"), StringComparison.Ordinal);
        foreach (var command in new[] { "[Enter] Calls", "[Tab] Switch list", "[s] System prompt", "[t] Tool definitions" })
        {
            Assert.Contains(command, host.Frame(), StringComparison.Ordinal);
        }
        host.SaveSvg("context");
    }

    [Fact]
    public void Down_selects_the_next_session_and_sets_the_selected_key()
    {
        using var host = Start();

        host.Press(TerminalKey.Down);

        Assert.Equal(AlphaReviewRow, Selected(host, "Sessions"));
        Assert.Contains(AlphaReviewHeader, host.Frame(), StringComparison.Ordinal);
        Assert.StartsWith("call 2  12:07:40", Selected(host, "Calls"), StringComparison.Ordinal);
        host.Type('2');
        Assert.Contains($"selected key: {SampleRun.AlphaReviewKey}", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_on_a_session_selects_it_and_sets_the_selected_key()
    {
        using var host = Start();

        host.ClickText(BetaRow[2..]);

        Assert.Equal(BetaRow, Selected(host, "Sessions"));
        Assert.Contains("Claude · worker · beta · claude-sonnet-4-5 · 2 calls", host.Frame(), StringComparison.Ordinal);
        host.Type('2');
        Assert.Contains($"selected key: {SampleRun.BetaWorkerKey}", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_set_by_another_page_selects_that_session_and_its_last_call()
    {
        using var host = Start();
        host.Type('2');
        Assert.Contains("selected key: none", host.Frame(), StringComparison.Ordinal);

        host.Type('b');

        Assert.Equal(BetaRow, Selected(host, "Sessions"));
        Assert.StartsWith("call 2  12:20:00   34.5k", Selected(host, "Calls"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_that_matches_no_session_shows_the_first_session()
    {
        using var host = Start();
        host.ClickText(BetaRow[2..]);

        var run = SampleRun.CreateEnriched();
        host.SetSnapshot(run with { Version = 2, Sessions = run.Sessions.RemoveAll(s => s.Files.Key == SampleRun.BetaWorkerKey) });

        Assert.Equal(AlphaWorkerRow, Selected(host, "Sessions"));
        Assert.Contains(AlphaWorkerHeader, host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_running_session_follows_new_calls_until_the_user_moves_up_and_End_resumes()
    {
        using var host = Start();
        host.ClickText(BetaRow[2..]);
        host.Press(TerminalKey.Tab);
        Assert.StartsWith("call 2", Selected(host, "Calls"), StringComparison.Ordinal);

        host.SetSnapshot(WithBetaCalls(2, 1));
        Assert.StartsWith("call 3  12:23:00   37.5k", Selected(host, "Calls"), StringComparison.Ordinal);

        host.Press(TerminalKey.Up);
        host.SetSnapshot(WithBetaCalls(3, 2));
        Assert.StartsWith("call 2", Selected(host, "Calls"), StringComparison.Ordinal);
        Assert.Contains("call 4  12:24:00", Rows(host, "Calls")[^1], StringComparison.Ordinal);

        host.Press(TerminalKey.End);
        Assert.StartsWith("call 4", Selected(host, "Calls"), StringComparison.Ordinal);
        host.SetSnapshot(WithBetaCalls(4, 3));
        Assert.StartsWith("call 5", Selected(host, "Calls"), StringComparison.Ordinal);
        Assert.Contains("Make-up at call 5", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_on_the_last_call_resumes_following()
    {
        using var host = Start();
        host.ClickText(BetaRow[2..]);
        host.SetSnapshot(WithBetaCalls(2, 1));
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Up);
        host.SetSnapshot(WithBetaCalls(3, 2));
        Assert.StartsWith("call 2", Selected(host, "Calls"), StringComparison.Ordinal);

        host.ClickText("call 4  12:24:00");
        host.SetSnapshot(WithBetaCalls(4, 3));

        Assert.StartsWith("call 5", Selected(host, "Calls"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_snapshot_keeps_the_selected_session_call_and_part()
    {
        using var host = Start();
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Up);
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Down);
        Assert.StartsWith("System prompt     tool_instructions", Selected(host, "Parts"), StringComparison.Ordinal);

        host.SetSnapshot(WithBetaCalls(2, 1));

        Assert.Equal(AlphaReviewRow, Selected(host, "Sessions"));
        Assert.StartsWith("call 1  12:06:30", Selected(host, "Calls"), StringComparison.Ordinal);
        Assert.StartsWith("System prompt     tool_instructions", Selected(host, "Parts"), StringComparison.Ordinal);
        host.Press(TerminalKey.Down);
        Assert.StartsWith("Tool definitions  2 tools", Selected(host, "Parts"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_gamma_chain_shows_both_attempts_and_selects_the_last_call_of_the_selected_one()
    {
        using var host = Start();

        host.ClickText(Gamma2Row[2..]);

        Assert.Contains("Claude · worker · gamma · claude-opus-4-5 · 4 calls · peak 33.5k · context 33.5k of 200.0k (17 %)",
            host.Frame(), StringComparison.Ordinal);
        Assert.Equal(
        [
            "call 1  #1  12:00:30   23.0k   +23.0k  out 420     think -       -",
            "call 2  #1  12:02:40   26.5k    +3.5k  out 1.9k    think -       -",
            "call 3  #2  12:11:00   30.7k    +4.2k  out 610     think -       -",
            "call 4  #2  12:14:30   33.5k    +2.8k  out 2.2k    think -       -",
        ], Rows(host, "Calls"));
        Assert.StartsWith("call 4  #2", Selected(host, "Calls"), StringComparison.Ordinal);

        host.ClickText(Gamma1Row[2..]);

        Assert.Equal(4, Rows(host, "Calls").Length);
        Assert.StartsWith("call 2  #1", Selected(host, "Calls"), StringComparison.Ordinal);
        Assert.Contains("Make-up at call 2", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_beta_worker_shows_that_its_transcript_is_unavailable()
    {
        using var host = Start();

        host.ClickText(BetaRow[2..]);

        Assert.Contains("unavailable: no transcript", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_session_without_calls_shows_no_context_sizes_and_no_model_calls_yet()
    {
        var run = SampleRun.CreateEnriched();
        var beta = run.Sessions.Single(s => s.Files.Key == SampleRun.BetaWorkerKey);
        var withoutCalls = beta with { Content = beta.Content with { Calls = [] } };
        using var host = Start(run with { Sessions = run.Sessions.Replace(beta, withoutCalls) });

        host.ClickText(BetaRow[2..^5]);

        Assert.Equal(["no context sizes"], Rows(host, "Context per call", marker: false));
        Assert.Equal(["no model calls yet"], Rows(host, "Calls", marker: false));
        Assert.Contains("0 calls · peak -", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Enter_on_a_part_opens_it_and_Escape_closes_it()
    {
        using var host = Start();
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);
        host.Press(TerminalKey.Down);

        host.Press(TerminalKey.Enter);

        AssertPopup(host, "Tool definitions: Read", "Description", "Schema");
        host.SaveSvg("context-part-popup");
        host.Press(TerminalKey.Escape);
        Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
        Assert.StartsWith("Tool definitions  Read", Selected(host, "Parts"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_on_a_part_opens_it()
    {
        using var host = Start();

        host.ClickText("total_tokens_reminder");

        AssertPopup(host, "Injected: total_tokens_reminder", "Text");
        Assert.Contains("Token usage: 19200/200000; 180800 remaining", PopupLines(host));
        host.Press(TerminalKey.Escape);
        Assert.StartsWith("Injected          total_tokens_reminder", Selected(host, "Parts"), StringComparison.Ordinal);
    }

    [Fact]
    public void S_opens_the_system_prompt_blocks_and_Escape_closes_them()
    {
        using var host = Start();

        host.Type('s');

        AssertPopup(host, "System prompt", "Block 1, 57 characters", "Block 2, 156 characters");
        host.SaveSvg("context-system-popup");
        host.Press(TerminalKey.Escape);
        Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_on_the_system_prompt_line_opens_the_system_prompt()
    {
        using var host = Start();
        host.ClickText(BetaRow[2..]);

        ClickCategoryLine(host, "System prompt");

        AssertPopup(host, "System prompt", "Unavailable");
        Assert.Contains("unavailable: no transcript", PopupLines(host));
    }

    [Fact]
    public void T_opens_the_tool_definitions_of_each_tool()
    {
        using var host = Start();

        host.Type('t');

        AssertPopup(host, "Tool definitions", "Read", "Bash");
        host.SaveSvg("context-tools-popup");
        host.Press(TerminalKey.Escape);
        Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void T_without_tools_or_checkpoint_shows_why_they_are_unavailable()
    {
        using var host = Start();
        host.ClickText(BetaRow[2..]);

        host.Type('t');

        AssertPopup(host, "Tool definitions", "Unavailable");
        Assert.Contains("unavailable: no transcript", PopupLines(host));
    }

    [Fact]
    public void A_click_on_the_tool_definitions_line_shows_the_checkpoint_tools()
    {
        using var host = Start();
        host.Press(TerminalKey.Down);

        ClickCategoryLine(host, "Tool definitions");

        AssertPopup(host, "Tool definitions", "Tools");
        var lines = PopupLines(host);
        var tokens = Array.IndexOf(lines, "680 tokens");
        Assert.True(tokens >= 0, host.Frame());
        Assert.Equal(["view", "powershell"], lines[(tokens + 1)..(tokens + 3)]);
    }

    [Fact]
    public void Tab_moves_the_keys_from_the_sessions_to_the_calls_to_the_parts_and_back()
    {
        using var host = Start();
        host.ClickText(Gamma1Row[2..]);

        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);
        Assert.Equal(Gamma1Row, Selected(host, "Sessions"));
        Assert.StartsWith("call 3", Selected(host, "Calls"), StringComparison.Ordinal);

        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);
        Assert.StartsWith("call 3", Selected(host, "Calls"), StringComparison.Ordinal);
        Assert.Equal(Rows(host, "Parts")[1], Selected(host, "Parts"));

        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);
        Assert.Equal(Gamma2Row, Selected(host, "Sessions"));
    }

    [Fact]
    public void Shift_Tab_moves_the_keys_from_the_sessions_to_the_parts_to_the_calls_and_back()
    {
        var snapshot = SampleRun.CreateEnriched();
        var shell = new AppShell([new ContextPage()], () => snapshot, new FixedTimeProvider(snapshot.ReadAt));
        using var harness = TerminalHarness.Start(shell.Root, shell.OnUpdate);
        harness.ClickText(Gamma1Row[2..]);

        harness.Press(TerminalKey.Tab, TerminalModifiers.Shift);
        harness.Press(TerminalKey.Down);
        Assert.Equal(Gamma1Row, Selected(harness.Frame(), "Sessions"));
        Assert.StartsWith("call 2", Selected(harness.Frame(), "Calls"), StringComparison.Ordinal);
        Assert.Equal(Rows(harness.Frame(), "Parts")[1], Selected(harness.Frame(), "Parts"));

        harness.Press(TerminalKey.Tab, TerminalModifiers.Shift);
        harness.Press(TerminalKey.Down);
        Assert.StartsWith("call 3", Selected(harness.Frame(), "Calls"), StringComparison.Ordinal);

        harness.Press(TerminalKey.Tab, TerminalModifiers.Shift);
        harness.Press(TerminalKey.Down);
        Assert.Equal(Gamma2Row, Selected(harness.Frame(), "Sessions"));
    }

    [Fact]
    public void The_wheel_scrolls_the_parts()
    {
        var run = SampleRun.CreateEnriched();
        var worker = run.Sessions.Single(s => s.Files.Key == SampleRun.AlphaWorkerKey);
        var notes = Enumerable.Range(1, 40).Select(i => new InjectedItem($"note_{i:00}", "system", null, $"Note {i}."));
        var longer = worker with { Stores = worker.Stores with { Injected = worker.Stores.Injected.AddRange(notes) } };
        using var host = Start(run with { Sessions = run.Sessions.Replace(worker, longer) });
        Assert.StartsWith("System prompt     block 1", Rows(host, "Parts")[0], StringComparison.Ordinal);

        var (x, y) = PaneOrigin(host.Frame(), "Parts");
        host.Wheel(x + 10, y + 5, -5);

        var top = Rows(host, "Parts")[0];
        Assert.DoesNotContain("block 1", top, StringComparison.Ordinal);
        Assert.Null(Selected(host.Frame(), "Parts", visible: false));
        host.Wheel(x + 10, y + 5, 5);
        Assert.StartsWith("System prompt     block 1", Rows(host, "Parts")[0], StringComparison.Ordinal);
    }

    /// <summary>The enriched sample run with <paramref name="count"/> calls added to the beta worker session.</summary>
    private static RunSnapshot WithBetaCalls(long version, int count)
    {
        var run = SampleRun.CreateEnriched();
        var beta = run.Sessions.Single(s => s.Files.Key == SampleRun.BetaWorkerKey);
        var calls = Enumerable.Range(3, count).Select(n => new ModelCall(
            $"msg_0{n}beta", "claude-sonnet-4-5", SampleRun.At(12, 20 + n, 0), new TokenUsage(600, 33_000 + n * 1_000, 900, null)));
        var longer = beta with { Content = beta.Content with { Calls = beta.Content.Calls.AddRange(calls) } };
        return run with { Version = version, Sessions = run.Sessions.Replace(beta, longer) };
    }

    /// <summary>Clicks the category line of the make-up pane, which ends in its number of parts.</summary>
    private static void ClickCategoryLine(UiTestHost host, string category)
    {
        var rows = host.Frame().Split('\n');
        var pattern = new Regex(Regex.Escape(category) + @" .* \d+ parts?\b");
        for (var y = 0; y < rows.Length; y++)
        {
            if (pattern.Match(rows[y]) is { Success: true } match)
            {
                host.Click(AnsiScreen.CellColumn(rows[y], match.Index), y);
                return;
            }
        }
        Assert.Fail($"No {category} line. Frame:\n{host.Frame()}");
    }

    /// <summary>The cell column and row of the top left corner of the pane whose title starts with <paramref name="title"/>.</summary>
    private static (int X, int Y) PaneOrigin(string frame, string title)
    {
        var lines = frame.Split('\n');
        var top = Array.FindIndex(lines, line => line.Contains($"┌ {title}", StringComparison.Ordinal));
        Assert.True(top >= 0, frame);
        return (AnsiScreen.CellColumn(lines[top], lines[top].IndexOf($"┌ {title}", StringComparison.Ordinal)), top);
    }

    /// <summary>The text rows inside the pane whose title starts with <paramref name="title"/>, without borders.</summary>
    private static string[] PaneLines(string frame, string title)
    {
        var lines = frame.Split('\n');
        var top = Array.FindIndex(lines, line => line.Contains($"┌ {title}", StringComparison.Ordinal));
        Assert.True(top >= 0, frame);
        var left = lines[top].IndexOf($"┌ {title}", StringComparison.Ordinal);
        var right = lines[top].IndexOf('┐', left);
        var rows = new List<string>();
        for (var row = top + 1; row < lines.Length && lines[row].Length > left && lines[row][left] == '│'; row++)
        {
            var line = lines[row];
            rows.Add(line.Length <= left + 1 ? "" : line[(left + 1)..Math.Min(right - 1, line.Length)].TrimEnd());
        }
        return [.. rows];
    }

    private static string[] PaneLines(UiTestHost host, string title) => PaneLines(host.Frame(), title);

    /// <summary>The non-empty rows of a pane, without the marker column of a list unless <paramref name="marker"/> is false.</summary>
    private static string[] Rows(string frame, string title, bool marker = true) =>
        [.. PaneLines(frame, title).Where(line => line.Trim().Length > 0).Select(line => marker ? line[2..] : line.Trim())];

    private static string[] Rows(UiTestHost host, string title, bool marker = true) => Rows(host.Frame(), title, marker);

    /// <summary>The selected row of a list pane: the row with the arrow.</summary>
    private static string? Selected(string frame, string title, bool visible = true)
    {
        var row = PaneLines(frame, title).FirstOrDefault(line => line.StartsWith('→'));
        if (visible)
        {
            Assert.True(row is not null, frame);
        }
        return row?[2..];
    }

    private static string Selected(UiTestHost host, string title)
    {
        Assert.DoesNotContain("[X]", host.Frame(), StringComparison.Ordinal);
        return Selected(host.Frame(), title)!;
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

    /// <summary>Asserts that the pop-up with the title is open and shows the headings in that order.</summary>
    private static void AssertPopup(UiTestHost host, string title, params string[] headings)
    {
        var lines = host.Frame().Split('\n');
        Assert.Contains(lines, line => line.Contains($"┌ {title} ", StringComparison.Ordinal) && line.Contains("[X]", StringComparison.Ordinal));
        var popup = PopupLines(host);
        var positions = headings.Select(heading => Array.IndexOf(popup, heading)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0, host.Frame()));
        Assert.Equal(positions.Order(), positions);
    }
}
