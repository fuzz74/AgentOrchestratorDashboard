using OrchDash.Contracts;
using OrchDash.Tests.Support;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Tests.Pages.ContextWindow;

/// <summary>
/// A stand-in for another page: it shows <c>selected key: &lt;key&gt;</c>, and its command <c>b</c> sets
/// <see cref="IAppContext.SelectedSessionKey"/> to the beta worker session and shows the Context page.
/// </summary>
internal sealed class KeyPickerPage : IPage
{
    public string Id => "picker";

    public string Title => "Picker";

    public Visual Build(IAppContext context)
    {
        var body = new ScrollViewer(new TextBlock(() => $"selected key: {context.SelectedSessionKey.Value ?? "none"}"), focusable: true);
        body.AddCommand(new Command
        {
            Id = "picker.beta",
            LabelMarkup = "Beta",
            Gesture = new KeyGesture('b'),
            Execute = _ =>
            {
                context.SelectedSessionKey.Value = SampleRun.BetaWorkerKey;
                context.ShowPage("context");
            },
        });
        return body;
    }
}
