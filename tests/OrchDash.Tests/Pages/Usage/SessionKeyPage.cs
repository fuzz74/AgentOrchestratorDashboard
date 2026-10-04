using OrchDash.Contracts;
using OrchDash.Tests.Support;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Tests.Pages.Usage;

/// <summary>
/// A stand-in for another page: it shows <c>selected key: &lt;key&gt;</c>, and its command <c>g</c> sets
/// <see cref="IAppContext.SelectedSessionKey"/> to the second gamma worker session and shows the Usage page.
/// </summary>
internal sealed class SessionKeyPage : IPage
{
    public string Id => "picker";

    public string Title => "Picker";

    public Visual Build(IAppContext context)
    {
        var body = new ScrollViewer(new TextBlock(() => $"selected key: {context.SelectedSessionKey.Value ?? "none"}"), focusable: true);
        body.AddCommand(new Command
        {
            Id = "picker.gamma",
            LabelMarkup = "Gamma",
            Gesture = new KeyGesture('g'),
            Execute = _ =>
            {
                context.SelectedSessionKey.Value = SampleRun.GammaWorker2Key;
                context.ShowPage("usage");
            },
        });
        return body;
    }
}
