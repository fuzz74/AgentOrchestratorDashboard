using OrchDash.Contracts;
using OrchDash.Core.Model;

namespace OrchDash.Shell;

/// <summary>Builds the markup of the header line.</summary>
public static class HeaderText
{
    /// <summary>
    /// The header's three parts: <c>Run</c> is the folder name of the repo root, <c> · &lt;stamp&gt;</c> for an archived
    /// run (32.11), the phase and the elapsed run time (none before the run starts); <c>Problems</c> is
    /// <c>&lt;n&gt; problems</c>, or <c>""</c> when there are none; <c>Quit</c> is the quit label.
    /// </summary>
    public static (string Run, string Problems, string Quit) Build(RunSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var run = snapshot.Run;
        var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(RunPaths.RepoRoot(snapshot.RepoPath)));
        var markup = Look.Tag("bold", folder);
        if (RunPaths.Stamp(snapshot.RepoPath) is { } stamp)
        {
            markup += Look.Tag("muted", $" · {stamp}");
        }
        markup += $"  {Look.Tag("accent", run.Phase.ToString())}";
        if (run.StartedAt is { } started)
        {
            markup += "  " + Look.Tag("", Look.Span((run.FinishedAt ?? now) - started));
        }
        var problems = snapshot.Problems.IsEmpty ? "" : Look.Tag("warning", $"{snapshot.Problems.Length} problems");
        return (markup, problems, Look.Tag("bold", "quit"));
    }
}
