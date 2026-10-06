namespace OrchDash.Core.Model;

public sealed record InsightSources(IGitReader? Git, IProcessLister? Processes, ICommandLogReader? Commands)
{
    // three nulls
    public static InsightSources None { get; } = new(null, null, null);
}
