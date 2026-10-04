using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Shell;
using XenoAtom.Terminal;

namespace OrchDash.Tests.Host;

/// <summary>Runs <see cref="AppShell"/> on an in-memory terminal, with the time fixed at the snapshot's <c>ReadAt</c>.</summary>
/// <remarks>Every method returns after the app has handled the input and rendered a frame. See <see cref="TerminalHarness"/>.</remarks>
public sealed class UiTestHost : IDisposable
{
    private readonly TerminalHarness _harness;
    private readonly FixedTimeProvider _time;
    private RunSnapshot _snapshot;

    private UiTestHost(IReadOnlyList<IPage> pages, RunSnapshot snapshot, int width, int height)
    {
        _snapshot = snapshot;
        _time = new FixedTimeProvider(snapshot.ReadAt);
        var shell = new AppShell(pages, () => Volatile.Read(ref _snapshot), _time);
        _harness = TerminalHarness.Start(shell.Root, shell.OnUpdate, width, height);
    }

    /// <summary>True once the app has ended: <c>OnUpdate</c> returned Stop, or Ctrl+Q was pressed.</summary>
    public bool Exited => _harness.Exited;

    /// <summary>Starts the shell with the pages and the snapshot and returns after its first frame.</summary>
    public static UiTestHost Start(IReadOnlyList<IPage> pages, RunSnapshot snapshot, int width = 160, int height = 45)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(snapshot);
        return new UiTestHost(pages, snapshot, width, height);
    }

    /// <summary>Makes the shell's <c>latest</c> return <paramref name="snapshot"/>, moves the clock to its <c>ReadAt</c> and waits for the next frame.</summary>
    public void SetSnapshot(RunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Volatile.Write(ref _snapshot, snapshot);
        _time.Set(snapshot.ReadAt);
        _harness.Pump();
    }

    public void Press(TerminalKey key) => _harness.Press(key);

    public void Type(char c) => _harness.Type(c);

    /// <summary>Presses Ctrl with a letter, for example <c>PressCtrl('q')</c> for the built-in exit.</summary>
    public void PressCtrl(char c) => _harness.Type(c, TerminalModifiers.Ctrl);

    public void Click(int x, int y) => _harness.Click(x, y);

    /// <summary>Turns the wheel by <paramref name="delta"/> notches; negative scrolls down, positive scrolls up.</summary>
    public void Wheel(int x, int y, int delta) => _harness.Wheel(x, y, delta);

    /// <summary>Clicks the first occurrence of <paramref name="text"/> on screen; throws when it is absent.</summary>
    public void ClickText(string text) => _harness.ClickText(text);

    /// <summary>Screen rows joined with '\n', trailing spaces trimmed.</summary>
    public string Frame() => _harness.Frame();

    /// <summary>Writes the current frame to <c>&lt;test output folder&gt;/frames/&lt;name&gt;.svg</c>.</summary>
    public void SaveSvg(string name) => _harness.SaveSvg(name);

    public void Dispose() => _harness.Dispose();
}
