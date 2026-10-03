using System.Diagnostics;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;

namespace OrchDash.Tests.Host;

/// <summary>
/// Runs a visual tree headless, exactly as production does with <c>Terminal.Run(root, onUpdate)</c>, on an
/// in-memory terminal, and gives tests the rendered screen as text.
/// </summary>
/// <remarks>
/// <para>The app runs on a dedicated UI thread; <c>onUpdate</c> is called there once per loop turn (about every
/// 15 ms). Visuals and <see cref="State{T}"/> belong to that thread while the app runs: a test must not touch
/// them directly, but may change plain data that <c>onUpdate</c> reads and then call <see cref="Pump"/>.</para>
/// <para>Every method returns once the app has handled the input, <c>onUpdate</c> has run after it and the
/// resulting frame is rendered. Frames are taken from the terminal output at the start of an <c>onUpdate</c>
/// call, when no render is in progress.</para>
/// <para>Only one harness can run at a time, because the library's terminal and dispatcher are process-wide.</para>
/// </remarks>
public sealed class TerminalHarness : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Loop turns to wait for a turn without output before a frame of a constantly changing UI is taken anyway.</summary>
    private const int MaxSettleTurns = 20;

    private readonly Visual _root;
    private readonly Func<TerminalLoopResult> _onUpdate;
    private readonly AnsiScreen _screen;
    private readonly HarnessTerminalBackend _backend;
    private readonly TerminalSession _session;
    private readonly Thread _uiThread;
    private readonly object _gate = new();
    private readonly Queue<Action> _uiActions = new();

    // All fields below are guarded by _gate.
    private long _turn;
    private long _reads;
    private long _captureTurn = long.MaxValue;
    private long _frameTurn;
    private long _writesAtLastTurn;
    private string[] _frame;
    private bool _stopRequested;
    private bool _exited;
    private Exception? _failure;
    private bool _disposed;

    private TerminalHarness(Visual root, Func<TerminalLoopResult> onUpdate, int width, int height)
    {
        _root = root;
        _onUpdate = onUpdate;
        _screen = new AnsiScreen(width, height);
        _frame = _screen.Snapshot();
        _backend = new HarnessTerminalBackend(_screen, OnBackendRead);
        _session = Terminal.Open(_backend, new TerminalOptions { PreferUtf8Output = false }, force: true);
        _uiThread = new Thread(RunApp) { IsBackground = true, Name = nameof(TerminalHarness) };
    }

    /// <summary>True once the UI loop has ended: <c>onUpdate</c> returned Stop, or the built-in Ctrl+Q exit ran.</summary>
    public bool Exited
    {
        get
        {
            lock (_gate)
            {
                return _exited;
            }
        }
    }

    /// <summary>Starts the app and returns after its first frame is rendered.</summary>
    public static TerminalHarness Start(Visual root, Func<TerminalLoopResult> onUpdate, int width = 160, int height = 45)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(onUpdate);
        var harness = new TerminalHarness(root, onUpdate, width, height);
        try
        {
            harness._uiThread.Start();
            lock (harness._gate)
            {
                // The input relay must be waiting for events before the first input is sent.
                harness.WaitUntil(() => harness._reads > 0, "start reading input");
                // The first loop turn renders after its onUpdate; that frame is on screen when the next turn starts.
                harness.WaitForFrameAfter(harness._turn + 2);
            }
            harness.ThrowIfFailed();
            return harness;
        }
        catch
        {
            harness.Dispose();
            throw;
        }
    }

    /// <summary>Runs <c>onUpdate</c> again and waits for the frame rendered after it.</summary>
    public void Pump()
    {
        lock (_gate)
        {
            ThrowIfFailed();
            if (_exited)
            {
                return;
            }
            // The next onUpdate starts after this call; its frame is on screen when the one after starts.
            WaitForFrameAfter(_turn + 2);
        }
        ThrowIfFailed();
    }

    /// <summary>Presses a key, as a terminal reports it.</summary>
    public void Press(TerminalKey key, TerminalModifiers modifiers = default)
    {
        char? c = key switch
        {
            TerminalKey.Enter => '\r',
            TerminalKey.Tab => '\t',
            TerminalKey.Escape => '\u001b',
            TerminalKey.Backspace => '\b',
            TerminalKey.Space => ' ',
            _ => null,
        };
        var keyEvent = new TerminalKeyEvent { Key = key, Char = c, Modifiers = modifiers };
        if (key == TerminalKey.Space)
        {
            Send(keyEvent, new TerminalTextEvent { Text = " " });
        }
        else
        {
            Send(keyEvent);
        }
    }

    /// <summary>Types a character. With <see cref="TerminalModifiers.Ctrl"/> a letter becomes its control character, so <c>Type('q', Ctrl)</c> is Ctrl+Q.</summary>
    public void Type(char c, TerminalModifiers modifiers = default)
    {
        var ctrl = (modifiers & TerminalModifiers.Ctrl) != 0;
        if (ctrl && char.IsAsciiLetter(c))
        {
            c = (char)(char.ToUpperInvariant(c) - 'A' + 1);
        }
        var keyEvent = new TerminalKeyEvent { Key = c == ' ' ? TerminalKey.Space : TerminalKey.Unknown, Char = c, Modifiers = modifiers };
        if (char.IsControl(c) || ctrl)
        {
            Send(keyEvent);
        }
        else
        {
            Send(keyEvent, new TerminalTextEvent { Text = c.ToString() });
        }
    }

    /// <summary>Presses and releases the left mouse button on cell (<paramref name="x"/>, <paramref name="y"/>), 0-based.</summary>
    public void Click(int x, int y) => Send(
        new TerminalMouseEvent { X = x, Y = y, Button = TerminalMouseButton.Left, Kind = TerminalMouseKind.Down },
        new TerminalMouseEvent { X = x, Y = y, Button = TerminalMouseButton.Left, Kind = TerminalMouseKind.Up });

    /// <summary>Turns the mouse wheel over cell (<paramref name="x"/>, <paramref name="y"/>) by <paramref name="delta"/> notches; negative scrolls down, positive scrolls up.</summary>
    public void Wheel(int x, int y, int delta)
    {
        var events = new TerminalEvent[Math.Abs(delta)];
        for (var i = 0; i < events.Length; i++)
        {
            events[i] = new TerminalMouseEvent
            {
                X = x,
                Y = y,
                Button = TerminalMouseButton.Wheel,
                Kind = TerminalMouseKind.Wheel,
                WheelDelta = Math.Sign(delta),
            };
        }
        Send(events);
    }

    /// <summary>Clicks the first cell of the first occurrence of <paramref name="text"/> on screen, searching top to bottom.</summary>
    /// <exception cref="InvalidOperationException">The text is not on screen.</exception>
    public void ClickText(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        string[] rows;
        lock (_gate)
        {
            if (_exited)
            {
                return;
            }
            rows = _frame;
        }
        for (var y = 0; y < rows.Length; y++)
        {
            var index = rows[y].IndexOf(text, StringComparison.Ordinal);
            if (index >= 0)
            {
                Click(AnsiScreen.CellColumn(rows[y], index), y);
                return;
            }
        }
        throw new InvalidOperationException($"Text \"{text}\" is not on screen. Frame:\n{string.Join('\n', rows)}");
    }

    /// <summary>The last rendered frame: one line per screen row joined with '\n', trailing spaces trimmed.</summary>
    /// <remarks>A wide character occupies two cells, so a column in a line is a cell column when measured in cells.</remarks>
    public string Frame()
    {
        lock (_gate)
        {
            return string.Join('\n', _frame);
        }
    }

    /// <summary>Writes the current frame as SVG to <c>frames/&lt;name&gt;.svg</c> in the test output folder.</summary>
    public void SaveSvg(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var svg = InvokeOnUiThread(() => _root.App?.CaptureSvg() ?? throw new InvalidOperationException("The root visual is not attached to the app."));
        var folder = Path.Combine(AppContext.BaseDirectory, "frames");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name + ".svg"), svg);
    }

    /// <summary>Ends the app if it still runs and frees the process-wide terminal.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _stopRequested = true;
        }
        try
        {
            if (_uiThread.IsAlive && !_uiThread.Join(Timeout))
            {
                throw new TimeoutException($"The UI loop did not stop within {Timeout.TotalSeconds} s.");
            }
        }
        finally
        {
            _session.Dispose();
            _backend.Dispose();
            _screen.Dispose();
        }
    }

    private void RunApp()
    {
        try
        {
            Terminal.Run(_root, OnUpdate);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _failure = ex;
            }
        }
        finally
        {
            lock (_gate)
            {
                _exited = true;
                _frame = _screen.Snapshot();
                _uiActions.Clear();
                Monitor.PulseAll(_gate);
            }
        }
    }

    /// <summary>Wraps the test's <c>onUpdate</c>; runs on the UI thread between two renders.</summary>
    private TerminalLoopResult OnUpdate()
    {
        Action[] actions;
        bool stop;
        lock (_gate)
        {
            _turn++;
            var writes = _screen.Writes;
            var quiet = writes == _writesAtLastTurn;
            _writesAtLastTurn = writes;
            // Wait until the previous turn rendered nothing, so that a layout that needs several passes has settled.
            if (_turn >= _captureTurn && (quiet || _turn >= _captureTurn + MaxSettleTurns))
            {
                _frame = _screen.Snapshot();
                _frameTurn = _turn;
            }
            actions = [.. _uiActions];
            _uiActions.Clear();
            stop = _stopRequested;
            Monitor.PulseAll(_gate);
        }
        foreach (var action in actions)
        {
            action();
        }
        return stop ? TerminalLoopResult.Stop : _onUpdate();
    }

    private void OnBackendRead()
    {
        lock (_gate)
        {
            _reads++;
            Monitor.PulseAll(_gate);
        }
    }

    private void Send(params TerminalEvent[] events)
    {
        long reads;
        lock (_gate)
        {
            ThrowIfFailed();
            if (_exited)
            {
                return;
            }
            reads = _reads;
        }
        foreach (var e in events)
        {
            _backend.PushEvent(e);
        }
        lock (_gate)
        {
            // The relay reads once more after handing each event to the app.
            WaitUntil(() => _reads >= reads + events.Length, "take the input");
            // The loop turn after the next one starts after the events were queued, so it handles them,
            // runs onUpdate and renders; its frame is on screen when the following turn starts.
            WaitForFrameAfter(_turn + 3);
        }
        ThrowIfFailed();
    }

    private T InvokeOnUiThread<T>(Func<T> func)
    {
        var done = false;
        T result = default!;
        Exception? error = null;
        lock (_gate)
        {
            ThrowIfFailed();
            if (_exited)
            {
                throw new InvalidOperationException("The UI loop has ended.");
            }
            _uiActions.Enqueue(() =>
            {
                try
                {
                    result = func();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                lock (_gate)
                {
                    done = true;
                    Monitor.PulseAll(_gate);
                }
            });
            WaitUntil(() => done, "run an action on the UI thread");
            if (!done)
            {
                throw new InvalidOperationException("The UI loop has ended.");
            }
        }
        if (error is not null)
        {
            throw new InvalidOperationException("The action on the UI thread failed.", error);
        }
        return result;
    }

    /// <summary>Waits (holding <see cref="_gate"/>) until the frame of loop turn <paramref name="turn"/> or later is captured.</summary>
    private void WaitForFrameAfter(long turn)
    {
        _captureTurn = turn;
        try
        {
            WaitUntil(() => _frameTurn >= turn, "render a frame");
        }
        finally
        {
            _captureTurn = long.MaxValue;
        }
    }

    /// <summary>Waits (holding <see cref="_gate"/>) for <paramref name="condition"/>; also returns when the app has ended.</summary>
    private void WaitUntil(Func<bool> condition, string what)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(Timeout.TotalSeconds * Stopwatch.Frequency);
        while (!condition() && !_exited)
        {
            var remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), deadline);
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException($"The UI loop did not {what} within {Timeout.TotalSeconds} s. Frame:\n{string.Join('\n', _frame)}");
            }
            Monitor.Wait(_gate, remaining);
        }
    }

    private void ThrowIfFailed()
    {
        lock (_gate)
        {
            if (_failure is not null)
            {
                throw new InvalidOperationException("The UI loop failed.", _failure);
            }
        }
    }
}
