using OrchDash.Contracts;
using XenoAtom.Terminal.UI;

namespace OrchDash.Pages.CommandLogs;

/// <summary>
/// The Commands page (26.1-26.5): the command logs on the left and the output of the selected log on the right.
/// </summary>
public sealed class CommandsPage : IPage
{
    public string Id => "commands";

    public string Title => "Commands";

    public Visual Build(IAppContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new CommandsView(context).Root;
    }
}
