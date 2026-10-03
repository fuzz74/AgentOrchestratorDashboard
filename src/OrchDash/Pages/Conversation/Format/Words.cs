using System.Globalization;
using OrchDash.Core.Model;

namespace OrchDash.Pages.Conversation.Format;

internal static class Words
{
    public static string Role(AgentRole role) => role switch
    {
        AgentRole.Bootstrap => "bootstrap",
        AgentRole.Planner => "planner",
        AgentRole.Worker => "worker",
        AgentRole.Reviewer => "reviewer",
        AgentRole.Resolver => "resolver",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    public static string State(SessionState state) => state switch
    {
        SessionState.Running => "running",
        SessionState.Succeeded => "succeeded",
        SessionState.Failed => "failed",
        SessionState.Aborted => "aborted",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static string Number(long n) => n.ToString(CultureInfo.InvariantCulture);

    // "1 tool call", "2 tool calls"
    public static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{Number(n)} {noun}s";

    // "#1", "#1.2" for a reviewer, followed by " nudge" for a nudge
    public static string Attempt(SessionFiles files)
    {
        var text = "#" + Number(files.Attempt);
        if (files.Role == AgentRole.Reviewer)
            text += "." + Number(files.ReviewTry);
        return files.IsNudge ? text + " nudge" : text;
    }
}
