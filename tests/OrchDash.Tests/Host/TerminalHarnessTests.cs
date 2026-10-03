using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;
using Xunit;

namespace OrchDash.Tests.Host;

public sealed class TerminalHarnessTests
{
    private static TerminalLoopResult Continue() => TerminalLoopResult.Continue;

    private static string[] Lines(TerminalHarness harness) => harness.Frame().Split('\n');

    [Fact]
    public void Frame_shows_text_at_its_cell_and_has_the_requested_size()
    {
        var root = new VStack(
            new TextBlock("first line"),
            new HStack(new TextBlock("abc "), new TextBlock("second")),
            new Border(new TextBlock("✔ ▶ · ✖ ⊘ ◌")));

        using var harness = TerminalHarness.Start(root, Continue, width: 60, height: 12);
        var lines = Lines(harness);

        Assert.Equal(12, lines.Length);
        Assert.All(lines, line => Assert.True(line.Length <= 60));
        Assert.Equal("first line", lines[0]);
        Assert.Equal(4, lines[1].IndexOf("second", StringComparison.Ordinal));
        Assert.Contains("✔ ▶ · ✖ ⊘ ◌", lines[3], StringComparison.Ordinal);
        Assert.StartsWith("┌", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("└", lines[4], StringComparison.Ordinal);
        Assert.All(lines, line => Assert.DoesNotContain(" \n", line + "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void Press_and_Type_reach_a_KeyDown_handler_and_a_command()
    {
        var lastKey = new State<string>("none");
        var commandRuns = new State<int>(0);
        var keyTarget = new ScrollViewer(new TextBlock(() => $"key {lastKey.Value}"), focusable: true)
            .KeyDown((_, e) => lastKey.Value = $"{e.Key} {e.Modifiers}");
        var root = new VStack(keyTarget, new TextBlock(() => $"command {commandRuns.Value}"));
        root.AddCommand(new Command
        {
            Id = "test.increment",
            LabelMarkup = "Increment",
            Gesture = new KeyGesture('x'),
            Execute = _ => commandRuns.Value++,
        });

        using var harness = TerminalHarness.Start(root, Continue, width: 60, height: 10);
        Assert.Contains("key none", harness.Frame(), StringComparison.Ordinal);

        harness.Press(TerminalKey.F5);
        Assert.Contains("key F5 None", harness.Frame(), StringComparison.Ordinal);

        harness.Press(TerminalKey.F6, TerminalModifiers.Shift);
        Assert.Contains("key F6 Shift", harness.Frame(), StringComparison.Ordinal);

        harness.Type('x');
        Assert.Contains("command 1", harness.Frame(), StringComparison.Ordinal);

        harness.Type('x');
        Assert.Contains("command 2", harness.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void Click_and_ClickText_reach_a_PointerPressed_handler()
    {
        var clicks = new State<string>("no click");
        var root = new VStack(
            new TextBlock("header"),
            new HStack(
                new TextBlock("🙂 wide "),
                new TextBlock("[target]").PointerPressed((_, e) => clicks.Value = $"clicked {e.X},{e.Y}")),
            new TextBlock(() => clicks.Value));

        using var harness = TerminalHarness.Start(root, Continue, width: 60, height: 10);
        Assert.Contains("no click", harness.Frame(), StringComparison.Ordinal);

        harness.Click(9, 1);
        Assert.Contains("clicked 9,1", harness.Frame(), StringComparison.Ordinal);

        // The emoji takes two cells, so the target starts at cell column 8, one more than its index in the text.
        harness.ClickText("[target]");
        Assert.Contains("clicked 8,1", harness.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClickText_throws_with_the_frame_when_the_text_is_absent()
    {
        using var harness = TerminalHarness.Start(new TextBlock("only this"), Continue, width: 40, height: 5);

        var error = Assert.Throws<InvalidOperationException>(() => harness.ClickText("missing"));

        Assert.Contains("missing", error.Message, StringComparison.Ordinal);
        Assert.Contains("only this", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wheel_scrolls_a_ScrollViewer_down_for_a_negative_delta_and_up_for_a_positive_one()
    {
        var items = Enumerable.Range(0, 100).Select(i => (Visual)new TextBlock($"Item {i:D3}")).ToArray();
        var root = new ScrollViewer(new VStack(items));

        using var harness = TerminalHarness.Start(root, Continue, width: 40, height: 10);
        Assert.StartsWith("Item 000", Lines(harness)[0], StringComparison.Ordinal);

        harness.Wheel(5, 3, -3);
        Assert.StartsWith("Item 003", Lines(harness)[0], StringComparison.Ordinal);

        harness.Wheel(5, 3, 1);
        Assert.StartsWith("Item 002", Lines(harness)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void State_changed_in_onUpdate_shows_after_Pump()
    {
        var shown = new State<int>(0);
        var source = 0;
        var root = new TextBlock(() => $"value {shown.Value}");

        using var harness = TerminalHarness.Start(root, () =>
        {
            shown.Value = Volatile.Read(ref source);
            return TerminalLoopResult.Continue;
        }, width: 40, height: 5);
        Assert.Equal("value 0", Lines(harness)[0]);

        Volatile.Write(ref source, 42);
        harness.Pump();

        Assert.Equal("value 42", Lines(harness)[0]);
    }

    [Fact]
    public void onUpdate_returning_Stop_sets_Exited_and_keeps_the_last_frame()
    {
        var stop = false;
        using var harness = TerminalHarness.Start(
            new TextBlock("still here"),
            () => Volatile.Read(ref stop) ? TerminalLoopResult.Stop : TerminalLoopResult.Continue,
            width: 40,
            height: 5);
        Assert.False(harness.Exited);

        Volatile.Write(ref stop, true);
        harness.Pump();

        Assert.True(harness.Exited);
        Assert.Equal("still here", Lines(harness)[0]);
        harness.Press(TerminalKey.Enter);
        harness.ClickText("absent text is ignored after exit");
        Assert.Equal("still here", Lines(harness)[0]);
    }

    [Fact]
    public void Ctrl_Q_sets_Exited()
    {
        using var harness = TerminalHarness.Start(new TextBlock("app"), Continue, width: 40, height: 5);

        harness.Type('q', TerminalModifiers.Ctrl);

        Assert.True(harness.Exited);
    }

    [Fact]
    public void A_modal_Dialog_opened_from_a_handler_appears_and_closes_on_Escape()
    {
        var keyTarget = new ScrollViewer(new TextBlock("main page"), focusable: true);
        keyTarget.KeyDown((_, e) =>
        {
            if (e.Char == 'd')
            {
                Dialog? dialog = null;
                dialog = new Dialog()
                    .Title("Details")
                    .IsModal(true)
                    .Width(30)
                    .Height(8)
                    .Content(new ScrollViewer(new TextBlock("dialog body"), focusable: true)
                        .KeyDown((_, k) =>
                        {
                            if (k.Key == TerminalKey.Escape)
                            {
                                dialog!.Close();
                                k.Handled = true;
                            }
                        }));
                dialog.Show();
                e.Handled = true;
            }
        });

        using var harness = TerminalHarness.Start(keyTarget, Continue, width: 60, height: 20);
        Assert.DoesNotContain("dialog body", harness.Frame(), StringComparison.Ordinal);

        harness.Type('d');
        Assert.Contains("dialog body", harness.Frame(), StringComparison.Ordinal);
        Assert.Contains("Details", harness.Frame(), StringComparison.Ordinal);

        harness.Press(TerminalKey.Escape);
        Assert.DoesNotContain("dialog body", harness.Frame(), StringComparison.Ordinal);
        Assert.Contains("main page", harness.Frame(), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveSvg_writes_a_non_empty_file()
    {
        using var harness = TerminalHarness.Start(new TextBlock("svg content"), Continue, width: 40, height: 5);

        harness.SaveSvg("terminal-harness-test");

        var path = Path.Combine(AppContext.BaseDirectory, "frames", "terminal-harness-test.svg");
        Assert.True(new FileInfo(path).Length > 0);
        Assert.Contains("<svg", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Two_harnesses_one_after_the_other_both_work()
    {
        foreach (var name in new[] { "first app", "second app" })
        {
            var clicked = new State<bool>(false);
            var root = new VStack(
                new TextBlock(name).PointerPressed(() => clicked.Value = true),
                new TextBlock(() => clicked.Value ? "clicked" : "waiting"));

            using var harness = TerminalHarness.Start(root, Continue, width: 40, height: 5);
            Assert.Equal(name, Lines(harness)[0]);

            harness.ClickText(name);

            Assert.Equal("clicked", Lines(harness)[1]);
        }
    }
}
