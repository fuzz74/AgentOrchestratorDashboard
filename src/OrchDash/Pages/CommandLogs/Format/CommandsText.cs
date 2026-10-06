using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation.Format;

namespace OrchDash.Pages.CommandLogs.Format;

// The text of the Commands page (26.1, 26.2, 26.4): log rows, the output header and text, and the pop-up.
// Lines are markup; every piece of model text goes through Look.Tag. Pop-up titles and texts are plain text.
public static class CommandsText
{
    public const string NoLogs = "No command logs yet";

    private const string StderrSeparator = "\n── stderr ──\n";

    public static string KindWord(CommandKind kind) => kind switch
    {
        CommandKind.Setup => "setup",
        CommandKind.Acceptance => "acceptance",
        CommandKind.IntegrationSetup => "integration setup",
        CommandKind.IntegrationCheck => "integration check",
        CommandKind.BootstrapSetup => "bootstrap setup",
        CommandKind.BootstrapCheck => "bootstrap check",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static string OutcomeIcon(CommandOutcome outcome) => outcome switch
    {
        CommandOutcome.Passed => "✔",
        CommandOutcome.Failed => "✖",
        CommandOutcome.Running => "▶",
        CommandOutcome.Unknown => "·",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    public static string OutcomeColor(CommandOutcome outcome) => outcome switch
    {
        CommandOutcome.Passed => "success",
        CommandOutcome.Failed => "error",
        CommandOutcome.Running => "primary",
        CommandOutcome.Unknown => "muted",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    public static string OutcomeWord(CommandOutcome outcome) => outcome switch
    {
        CommandOutcome.Passed => "passed",
        CommandOutcome.Failed => "failed",
        CommandOutcome.Running => "running",
        CommandOutcome.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    // The task the log belongs to, or the bootstrap.
    public static string Owner(CommandLog log) => log.TaskId ?? "bootstrap";

    // 26.1: "<icon> <clock> <owner> <kind word> #<attempt> <bytes>", "#<attempt>" only when known.
    public static string Row(CommandLog log)
    {
        var parts = new List<string>
        {
            Look.Tag(OutcomeColor(log.Outcome), OutcomeIcon(log.Outcome)),
            Look.Clock(log.WrittenAt),
            Look.Tag("", Owner(log)),
            KindWord(log.Kind),
        };
        if (log.Attempt is { } attempt)
            parts.Add("#" + Words.Number(attempt));
        parts.Add(Look.Bytes(log.Length));
        return string.Join(' ', parts);
    }

    // 26.2: the command, the outcome with its exit code, the key and the size.
    public static ImmutableArray<string> HeaderLines(CommandLog log) =>
    [
        Look.Tag("bold", log.Command ?? "command unknown"),
        Look.Tag(OutcomeColor(log.Outcome), OutcomeLine(log)),
        Look.Tag("", log.Key),
        Look.Bytes(log.Length),
    ];

    // 26.2: the output, then the stderr output under a separator line while there is any. Plain text.
    public static string OutputText(CommandLog log) =>
        string.IsNullOrEmpty(log.StderrText) ? log.Text : log.Text + StderrSeparator + log.StderrText;

    // 26.4: "<kind word> <owner> #<attempt>", "#<attempt>" only when known.
    public static string PopupTitle(CommandLog log)
    {
        var title = $"{KindWord(log.Kind)} {Owner(log)}";
        return log.Attempt is { } attempt ? $"{title} #{Words.Number(attempt)}" : title;
    }

    // 26.4: Command, Outcome, Output and Stderr, each left out when its text is empty or white space.
    public static ImmutableArray<PopupSection> Popup(CommandLog log)
    {
        var sections = ImmutableArray.CreateBuilder<PopupSection>(4);
        Add("Command", log.Command);
        Add("Outcome", OutcomeLine(log));
        Add("Output", log.Text);
        Add("Stderr", log.StderrText);
        return sections.ToImmutable();

        void Add(string heading, string? text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                sections.Add(new PopupSection(heading, text));
        }
    }

    // "failed (exit 1)", or the outcome word alone without an exit code.
    private static string OutcomeLine(CommandLog log)
    {
        var word = OutcomeWord(log.Outcome);
        return log.ExitCode is { } exit ? $"{word} (exit {Words.Number(exit)})" : word;
    }
}
