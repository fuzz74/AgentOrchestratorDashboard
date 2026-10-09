using OrchDash.Contracts;
using OrchDash.Core.Model;

namespace OrchDash.Pages.ContextWindow;

/// <summary>
/// The agent of a row of the session list (42.1): the session itself while <see cref="AgentId"/> is null, else the
/// sub-agent of the session with that id.
/// </summary>
internal readonly record struct ListAgent(Session Session, string? AgentId)
{
    /// <summary>The <see cref="IAppContext.SelectedSessionKey"/> that selects the row.</summary>
    public string Key => AgentKey.Of(Session, AgentId);

    /// <summary>Whether the agent runs: the session by its state, a sub-agent by its state within the session.</summary>
    public bool IsRunning => AgentId is null
        ? Session.State == SessionState.Running
        : SubAgents.Find(Session.Content, AgentId) is { } sub && SubAgents.StateOf(Session, sub) == SessionState.Running;
}
