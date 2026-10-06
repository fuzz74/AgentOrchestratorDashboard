using OrchDash.Contracts;
using XenoAtom.Terminal.UI;

namespace OrchDash.Pages.GitView;

/// <summary>
/// The Git page (25.1-25.7): the git header on top, the task table, and the commits and files of the selected task
/// below it.
/// </summary>
public sealed class GitPage : IPage
{
    public string Id => "git";

    public string Title => "Git";

    public Visual Build(IAppContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new GitView(context).Root;
    }
}
