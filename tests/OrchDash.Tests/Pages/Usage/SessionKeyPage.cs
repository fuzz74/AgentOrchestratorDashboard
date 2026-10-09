using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Tests.Support;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Tests.Pages.Usage;

/// <summary>
/// A stand-in for another page: it shows <c>selected key: &lt;key&gt;</c>, and its commands set
/// <see cref="IAppContext.SelectedSessionKey"/> and show the Usage page: <c>g</c> to the second gamma worker session,
/// <c>s</c> to the alpha worker's sub-agent "Survey the parser module", and <c>u</c> to a sub-agent that the alpha
/// review does not have.
/// </summary>
internal sealed class SessionKeyPage : IPage
{
    public const string UnknownAgentId = "toolu_unknown";

    public string Id => "picker";

    public string Title => "Picker";

    public Visual Build(IAppContext context)
    {
        var body = new ScrollViewer(new TextBlock(() => $"selected key: {context.SelectedSessionKey.Value ?? "none"}"), focusable: true);
        Add('g', "gamma", "Gamma", () => SampleRun.GammaWorker2Key);
        Add('s', "subagent", "Sub-agent", () => AgentKey.Of(Session(SampleRun.AlphaWorkerKey), SampleRun.AlphaSub1Id));
        Add('u', "unknown", "Unknown sub-agent", () => AgentKey.Of(Session(SampleRun.AlphaReviewKey), UnknownAgentId));
        return body;

        Session Session(string key) => context.Snapshot.Value.Sessions.Single(s => s.Files.Key == key);

        void Add(char key, string id, string label, Func<string> selectedKey) => body.AddCommand(new Command
        {
            Id = $"picker.{id}",
            LabelMarkup = label,
            Gesture = new KeyGesture(key),
            Execute = _ =>
            {
                context.SelectedSessionKey.Value = selectedKey();
                context.ShowPage("usage");
            },
        });
    }
}
