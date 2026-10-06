using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Graph;
using OrchDash.Shell;
using OrchDash.Tests.Host;
using OrchDash.Tests.Support;
using XenoAtom.Terminal;
using Xunit;

namespace OrchDash.Tests.Pages.Graph;

public sealed class GraphPageTests
{
    private const string AlphaToBeta = "●✔ alpha  ───▸▶ beta";

    private static IPage[] Pages() => [new GraphPage(), new ConversationStubPage()];

    private static UiTestHost Start(RunSnapshot? snapshot = null, int height = 45) =>
        UiTestHost.Start(Pages(), snapshot ?? SampleRun.CreateInsight(), height: height);

    private static string[] Lines(string frame) => frame.Split('\n');

    private static int RowOf(string frame, string text)
    {
        var row = Array.FindIndex(Lines(frame), line => line.Contains(text, StringComparison.Ordinal));
        Assert.True(row >= 0, $"'{text}' is not on screen:\n{frame}");
        return row;
    }

    /// <summary>The column of the first occurrence of <paramref name="text"/> on the screen.</summary>
    private static int ColumnOf(string frame, string text) => Lines(frame)[RowOf(frame, text)].IndexOf(text, StringComparison.Ordinal);

    /// <summary>The text rows inside the panel whose title is <paramref name="title"/>, without borders.</summary>
    private static string[] PanelLines(string frame, string title)
    {
        var lines = Lines(frame);
        var top = RowOf(frame, $"┌ {title} ");
        var left = lines[top].IndexOf($"┌ {title} ", StringComparison.Ordinal);
        var right = lines[top].IndexOf('┐', left);
        var rows = new List<string>();
        for (var row = top + 1; row < lines.Length && lines[row].Length > left && lines[row][left] == '│'; row++)
        {
            rows.Add(lines[row][(left + 1)..Math.Min(right, lines[row].Length)].Trim());
        }
        return [.. rows];
    }

    /// <summary>The text lines inside the pop-up, without its borders and scroll bar; empty when no pop-up is open.</summary>
    private static List<string> PopupLines(string frame)
    {
        var lines = Lines(frame);
        var top = Array.FindIndex(lines, line => line.Contains("┌ ", StringComparison.Ordinal) && line.Contains("[X] ┐", StringComparison.Ordinal));
        if (top < 0)
        {
            return [];
        }
        var left = lines[top].IndexOf('┌', StringComparison.Ordinal);
        var right = lines[top].LastIndexOf('┐');
        var text = new List<string>();
        for (var row = top + 1; row < lines.Length && lines[row].Length > right && lines[row][left] == '│'; row++)
        {
            text.Add(lines[row][(left + 1)..(right - 1)].TrimEnd());
        }
        return text;
    }

    private static void AssertPopup(string frame, string title)
    {
        var titleRow = Lines(frame).FirstOrDefault(line => line.Contains("[X] ┐", StringComparison.Ordinal)) ?? "";
        Assert.Contains($"┌ {title} ", titleRow, StringComparison.Ordinal);
        Assert.Contains("Status", PopupLines(frame));
    }

    private static void AssertNoPopup(string frame) => Assert.DoesNotContain("[X]", frame, StringComparison.Ordinal);

    /// <summary>A snapshot with one wave of <paramref name="count"/> tasks <c>t01</c>, <c>t02</c>, ...</summary>
    private static RunSnapshot OneWave(int count)
    {
        var run = SampleRun.CreateInsight();
        var alpha = run.Tasks[0];
        var tasks = Enumerable.Range(1, count).Select(i => alpha with { Id = $"t{i:00}", Deps = [], Owns = [$"src/T{i:00}/**"], Wave = 1 });
        return run with { Tasks = [.. tasks] };
    }

    [Fact]
    public void The_page_shows_the_waves_the_selected_first_task_its_edges_and_its_details()
    {
        using var host = Start();
        var frame = host.Frame();

        var titles = RowOf(frame, "W1");
        Assert.Equal(titles, RowOf(frame, "W2"));
        Assert.Equal(titles, RowOf(frame, "W3"));
        var (w1, w2, w3) = (ColumnOf(frame, "W1"), ColumnOf(frame, "W2"), ColumnOf(frame, "W3"));
        Assert.True(w1 < w2 && w2 < w3, frame);
        Assert.Equal(w1 + 3, ColumnOf(frame, "alpha"));
        Assert.Equal(w1 + 3, ColumnOf(frame, "gamma"));
        Assert.Equal(w2 + 3, ColumnOf(frame, "beta"));
        Assert.Equal(w2 + 3, ColumnOf(frame, "delta"));
        Assert.Equal(w3 + 3, ColumnOf(frame, "epsilon"));
        Assert.True(RowOf(frame, "alpha") < RowOf(frame, "gamma"), frame);
        Assert.True(RowOf(frame, "beta") < RowOf(frame, "delta"), frame);

        Assert.Contains(AlphaToBeta, frame, StringComparison.Ordinal);
        Assert.Equal(RowOf(frame, "alpha"), RowOf(frame, "▸· epsilon"));
        Assert.Contains(" ✖ gamma", frame, StringComparison.Ordinal);
        Assert.Contains(" ⊘ delta", frame, StringComparison.Ordinal);

        Assert.Equal(
        [
            "alpha - Alpha parser",
            "deps: -",
            "dependents: ▶ beta",
            "owns: src/Alpha/**, tests/Alpha/**",
            "overlaps: -",
            "state: Done · 1 attempt · 0.25 USD",
            "detail: 0.25 USD, 1 attempt(s)",
            "sessions: 2",
        ], PanelLines(frame, "Task"));
        Assert.Contains("[Enter] Details", frame, StringComparison.Ordinal);
        Assert.Contains("[c] Conversation", frame, StringComparison.Ordinal);
        host.SaveSvg("graph");
    }

    [Fact]
    public void Down_and_Up_select_the_next_and_previous_card_of_the_column_and_stop_at_its_ends()
    {
        using var host = Start();

        host.Press(TerminalKey.Down);
        Assert.Contains("●✖ gamma", host.Frame(), StringComparison.Ordinal);
        Assert.Contains(" ✔ alpha", host.Frame(), StringComparison.Ordinal);
        Assert.Equal("gamma - Gamma formatter", PanelLines(host.Frame(), "Task")[0]);

        host.Press(TerminalKey.Down);
        Assert.Contains("●✖ gamma", host.Frame(), StringComparison.Ordinal);

        host.Press(TerminalKey.Up);
        Assert.Contains(AlphaToBeta, host.Frame(), StringComparison.Ordinal);
        host.Press(TerminalKey.Up);
        Assert.Contains(AlphaToBeta, host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_selects_the_nearest_card_of_the_next_column_and_wraps_to_the_first()
    {
        using var host = Start();

        host.Press(TerminalKey.Tab);
        Assert.Contains("◂✔ alpha  ───●▶ beta", host.Frame(), StringComparison.Ordinal);
        Assert.Equal("beta - Beta checker", PanelLines(host.Frame(), "Task")[0]);

        host.Press(TerminalKey.Tab);
        Assert.Contains("●· epsilon", host.Frame(), StringComparison.Ordinal);

        host.Press(TerminalKey.Tab);
        Assert.Contains(AlphaToBeta, host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_from_the_second_card_selects_the_card_on_the_nearest_row()
    {
        using var host = Start();
        host.Press(TerminalKey.Down);

        host.Press(TerminalKey.Tab);
        Assert.Contains("●⊘ delta", host.Frame(), StringComparison.Ordinal);

        host.Press(TerminalKey.Tab);
        Assert.Contains("●· epsilon", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Shift_Tab_selects_the_previous_column_and_wraps_to_the_last()
    {
        var snapshot = SampleRun.CreateInsight();
        var shell = new AppShell([new GraphPage()], () => snapshot, new FixedTimeProvider(snapshot.ReadAt));
        using var harness = TerminalHarness.Start(shell.Root, shell.OnUpdate);
        Assert.Contains(AlphaToBeta, harness.Frame(), StringComparison.Ordinal);

        harness.Press(TerminalKey.Tab, TerminalModifiers.Shift);
        Assert.Contains("●· epsilon", harness.Frame(), StringComparison.Ordinal);

        harness.Press(TerminalKey.Tab, TerminalModifiers.Shift);
        Assert.Contains("●▶ beta", harness.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_selects_a_card_and_a_second_click_opens_its_pop_up()
    {
        using var host = Start();

        host.ClickText("✖ gamma");
        Assert.Contains("●✖ gamma", host.Frame(), StringComparison.Ordinal);
        AssertNoPopup(host.Frame());

        host.ClickText("✖ gamma");
        AssertPopup(host.Frame(), "gamma - Gamma formatter");
        host.Press(TerminalKey.Escape);
        AssertNoPopup(host.Frame());
    }

    [Fact]
    public void A_click_beside_the_cards_changes_nothing()
    {
        using var host = Start();
        var frame = host.Frame();

        host.Click(ColumnOf(frame, "W1"), RowOf(frame, "W1"));
        host.Click(ColumnOf(frame, "───") + 1, RowOf(frame, "───"));

        Assert.Contains(AlphaToBeta, host.Frame(), StringComparison.Ordinal);
        AssertNoPopup(host.Frame());
    }

    [Fact]
    public void Enter_opens_the_task_pop_up_and_Escape_closes_it()
    {
        using var host = Start();

        host.Press(TerminalKey.Enter);

        AssertPopup(host.Frame(), "alpha - Alpha parser");
        host.SaveSvg("graph-popup");
        host.Press(TerminalKey.Escape);
        AssertNoPopup(host.Frame());
        Assert.Contains(AlphaToBeta, host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void C_shows_the_last_session_of_the_selected_task_on_the_conversation_page()
    {
        using var host = Start();

        host.Type('c');

        Assert.Contains(ConversationStubPage.Prefix + SampleRun.AlphaReviewKey, host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void C_on_a_task_without_a_session_does_nothing()
    {
        using var host = Start();
        host.Press(TerminalKey.Tab);
        host.Press(TerminalKey.Down);
        Assert.Contains("●⊘ delta", host.Frame(), StringComparison.Ordinal);

        host.Type('c');

        Assert.DoesNotContain(ConversationStubPage.Prefix, host.Frame(), StringComparison.Ordinal);
        Assert.Contains("●⊘ delta", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_snapshot_keeps_the_selected_task_by_id()
    {
        using var host = Start();
        host.Press(TerminalKey.Tab);
        var run = SampleRun.CreateInsight();

        host.SetSnapshot(run with { Version = 2, Tasks = [.. run.Tasks.Reverse()] });

        Assert.Contains("●▶ beta", host.Frame(), StringComparison.Ordinal);
        Assert.Equal("beta - Beta checker", PanelLines(host.Frame(), "Task")[0]);
    }

    [Fact]
    public void A_new_snapshot_without_the_selected_task_selects_the_first_task()
    {
        using var host = Start();
        var run = SampleRun.CreateInsight();

        host.SetSnapshot(run with { Version = 2, Tasks = run.Tasks.RemoveAt(0) });

        Assert.Contains("●✖ gamma", host.Frame(), StringComparison.Ordinal);
        Assert.Equal("gamma - Gamma formatter", PanelLines(host.Frame(), "Task")[0]);

        host.SetSnapshot(run with { Version = 3 });
        Assert.Contains("●✖ gamma", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_plan_the_page_shows_no_plan_yet_and_an_empty_detail_panel()
    {
        using var host = Start(SampleRun.CreateInsight() with { Plan = null });

        Assert.Contains("No plan yet", host.Frame(), StringComparison.Ordinal);
        Assert.DoesNotContain("W1", host.Frame(), StringComparison.Ordinal);
        Assert.All(PanelLines(host.Frame(), "Task"), line => Assert.Equal("", line));

        host.Press(TerminalKey.Enter);
        AssertNoPopup(host.Frame());
    }

    [Fact]
    public void A_tall_graph_scrolls_to_keep_the_selected_card_in_view_and_the_wheel_scrolls_back()
    {
        using var host = Start(OneWave(30), height: 30);
        Assert.Contains("W1", host.Frame(), StringComparison.Ordinal);
        Assert.DoesNotContain("t30", host.Frame(), StringComparison.Ordinal);

        for (var i = 1; i < 30; i++)
        {
            host.Press(TerminalKey.Down);
            Assert.Contains($"●✔ t{i + 1:00}", host.Frame(), StringComparison.Ordinal);
        }
        var frame = host.Frame();
        Assert.DoesNotContain("W1", frame, StringComparison.Ordinal);
        Assert.Equal("t30 - Alpha parser", PanelLines(frame, "Task")[0]);

        host.Wheel(ColumnOf(frame, "t30"), RowOf(frame, "t30"), 40);

        Assert.Contains("W1", host.Frame(), StringComparison.Ordinal);
        Assert.Contains(" ✔ t01", host.Frame(), StringComparison.Ordinal);
        Assert.DoesNotContain("●✔ t30", host.Frame(), StringComparison.Ordinal);

        host.Press(TerminalKey.Up);
        Assert.Contains("●✔ t29", host.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_snapshot_scrolls_to_a_selection_that_moved_out_of_view()
    {
        using var host = Start(OneWave(30), height: 30);
        host.Press(TerminalKey.Down);
        Assert.Contains("●✔ t02", host.Frame(), StringComparison.Ordinal);
        var run = OneWave(30);

        host.SetSnapshot(run with { Version = 2, Tasks = [.. run.Tasks.Reverse()] });

        Assert.Contains("●✔ t02", host.Frame(), StringComparison.Ordinal);
        Assert.Equal("t02 - Alpha parser", PanelLines(host.Frame(), "Task")[0]);
    }
}
