using OrchDash.Contracts;
using OrchDash.Tests.Support;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Tests.Pages.ContextWindow;

/// <summary>
/// A stand-in for another page: it shows <c>selected key: &lt;key&gt;</c>, and its commands <c>a</c> and <c>b</c> set
/// <see cref="IAppContext.SelectedSessionKey"/> to the alpha or the beta worker session and show the Context page.
/// </summary>
internal sealed class KeyPickerPage : IPage
{
    public string Id => "picker";

    public string Title => "Picker";

    public Visual Build(IAppContext context)
    {
        var body = new ScrollViewer(new TextBlock(() => $"selected key: {context.SelectedSessionKey.Value ?? "none"}"), focusable: true);
        AddPick('a', "Alpha", SampleRun.AlphaWorkerKey);
        AddPick('b', "Beta", SampleRun.BetaWorkerKey);
        return body;

        void AddPick(char key, string label, string sessionKey) =>
            body.AddCommand(new Command
            {
                Id = "picker." + label.ToLowerInvariant(),
                LabelMarkup = label,
                Gesture = new KeyGesture(key),
                Execute = _ =>
                {
                    context.SelectedSessionKey.Value = sessionKey;
                    context.ShowPage("context");
                },
            });
    }
}
