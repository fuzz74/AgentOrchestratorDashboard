using OrchDash.Contracts;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Tests.Shell;

/// <summary>
/// A test page that shows <c>&lt;text&gt; version &lt;n&gt;</c> and <c>&lt;text&gt; keys [...]</c>, the characters that
/// reached its focusable area. Its root is not focusable, as in a real page. With <paramref name="withCommands"/> it has
/// the commands <c>o</c> (opens <see cref="PopupSections"/>), <c>r</c> (opens two pop-ups in a row) and <c>g</c>
/// (shows the page <paramref name="goTo"/>).
/// </summary>
internal sealed class StubPage(string id, string title, string text, bool withCommands = false, string goTo = "") : IPage
{
    public const string PopupTitle = "Stub details";

    public static readonly IReadOnlyList<PopupSection> PopupSections =
    [
        new("Plain heading", "literal [bold]x[/] text\n" + string.Join('\n', Enumerable.Range(1, 60).Select(i => $"plain line {i:D2}"))),
        new("Json heading", """{"name":"stub","items":[1,2]}""", TextKind.Json),
        new("Diff heading", "@@ -1,2 +1,2 @@\n-old line\n+new line\n same line", TextKind.Diff),
    ];

    private readonly State<string> _keys = new("");

    public string Id => id;

    public string Title => title;

    public Visual Build(IAppContext context)
    {
        var body = new ScrollViewer(
                new VStack(
                    new TextBlock(() => $"{text} version {context.Snapshot.Value.Version}"),
                    new TextBlock(() => $"{text} keys [{_keys.Value}]")),
                focusable: true)
            .KeyDown((_, e) =>
            {
                if (e.Char is { } c && !char.IsControl(c))
                {
                    _keys.Value += c;
                }
            });
        if (withCommands)
        {
            body.AddCommand(new Command
            {
                Id = "stub.open",
                LabelMarkup = "Open",
                Gesture = new KeyGesture('o'),
                Execute = _ => context.ShowPopup(PopupTitle, PopupSections),
            });
            body.AddCommand(new Command
            {
                Id = "stub.replace",
                LabelMarkup = "Replace",
                Gesture = new KeyGesture('r'),
                Execute = _ =>
                {
                    context.ShowPopup("Replaced popup", [new PopupSection("Old heading", "old text")]);
                    context.ShowPopup("Replacing popup", [new PopupSection("New heading", "new text")]);
                },
            });
            body.AddCommand(new Command
            {
                Id = "stub.goto",
                LabelMarkup = "Go to",
                Gesture = new KeyGesture('g'),
                Execute = _ => context.ShowPage(goTo),
            });
        }
        return new VStack(new TextBlock($"{text} top"), body);
    }
}
