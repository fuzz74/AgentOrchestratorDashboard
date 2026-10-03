using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;

namespace OrchDash.Tests.Host;

/// <summary>
/// An in-memory terminal backend that writes its output to an <see cref="AnsiScreen"/> and reports every
/// asynchronous event read.
/// </summary>
/// <remarks>
/// <see cref="XenoAtom.Terminal.UI.TerminalApp"/> relays input with a background loop that hands each event to the UI thread and
/// only then asks the backend for the next one. A read that starts after an event was pushed therefore proves
/// that the event has reached the app's queue.
/// </remarks>
internal sealed class HarnessTerminalBackend(AnsiScreen screen, Action onRead)
    : VirtualTerminalBackend(screen, TextWriter.Null, new TerminalSize(screen.Width, screen.Height), capabilities: null, disposeWriters: false),
      ITerminalBackend
{
    ValueTask<TerminalEvent> ITerminalBackend.ReadEventAsync(CancellationToken cancellationToken)
    {
        onRead();
        return ReadEventAsync(cancellationToken);
    }
}
