using OrchDash.Contracts;
using OrchDash.Tests.Support;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Tests.Shell;

/// <summary>
/// A test page that shows the selected session key and the time of the snapshot it is given, in a focusable scroll
/// viewer whose content is wider than the screen, so that Left and Right could scroll it. Its commands call
/// <see cref="IAppContext.Replay"/>: <c>a</c> at 12:05:00, <c>b</c> with null, <c>c</c> at 13:00:00 (after the end) and
/// <c>d</c> at 11:00:00 (before the first event); <c>k</c> sets the selected session key to <see cref="SessionKey"/>.
/// </summary>
internal sealed class ReplayStubPage : IPage
{
    public const string SessionKey = "stub-session";

    public string Id => "replay";

    public string Title => "Replay";

    public Visual Build(IAppContext context)
    {
        var body = new ScrollViewer(
                new VStack(
                    new TextBlock(() => $"session [{context.SelectedSessionKey.Value}]"),
                    new TextBlock(() => $"snapshot at {Look.Clock(context.Snapshot.Value.ReadAt)} now {Look.Clock(context.Now.Value)}"),
                    new TextBlock(new string('w', 400))),
                focusable: true);
        Add('a', "Replay 12:05", () => context.Replay(SampleRun.At(12, 5, 0)));
        Add('b', "Replay null", () => context.Replay(null));
        Add('c', "Replay 13:00", () => context.Replay(SampleRun.At(13, 0, 0)));
        Add('d', "Replay 11:00", () => context.Replay(SampleRun.At(11, 0, 0)));
        Add('k', "Session", () => context.SelectedSessionKey.Value = SessionKey);
        return body;

        void Add(char key, string label, Action execute) => body.AddCommand(new Command
        {
            Id = $"replay-stub.{key}",
            LabelMarkup = label,
            Gesture = new KeyGesture(key),
            Presentation = CommandPresentation.None,
            Execute = _ => execute(),
        });
    }
}
