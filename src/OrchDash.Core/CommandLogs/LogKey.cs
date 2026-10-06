using System.Globalization;
using System.Text.RegularExpressions;
using OrchDash.Core.Model;

namespace OrchDash.Core.CommandLogs;

// What the key of a command log (its path under logs/, forward slashes) says: one row of the command-log table
// (spec 4.3, 22.1).
internal readonly partial record struct LogKey(CommandKind Kind, string? TaskId, string? StartFolder, int? Attempt)
{
    // Null when the key matches no row, or its attempt number does not fit an int.
    public static LogKey? Parse(string key)
    {
        if (TaskLog().Match(key) is { Success: true } task)
        {
            var id = task.Groups["id"].Value;
            var ts = task.Groups["ts"].Value;
            if (!task.Groups["n"].Success)
                return new LogKey(CommandKind.Setup, id, ts, null);
            return ParseAttempt(task.Groups["n"].Value) is { } n ? new LogKey(CommandKind.Acceptance, id, ts, n) : null;
        }

        if (IntegrationLog().Match(key) is { Success: true } integration)
        {
            var kind = integration.Groups["setup"].Success ? CommandKind.IntegrationSetup : CommandKind.IntegrationCheck;
            return new LogKey(kind, integration.Groups["id"].Value, null, null);
        }

        if (BootstrapLog().Match(key) is { Success: true } bootstrap)
        {
            var kind = bootstrap.Groups["setup"].Success ? CommandKind.BootstrapSetup : CommandKind.BootstrapCheck;
            return ParseAttempt(bootstrap.Groups["n"].Value) is { } n
                ? new LogKey(kind, null, bootstrap.Groups["folder"].Value, n)
                : null;
        }

        return null;
    }

    private static int? ParseAttempt(string digits) =>
        int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    // <id>/<ts>/setup.log and <id>/<ts>/attempt-<n>-acceptance.log
    [GeneratedRegex(@"\A(?<id>[^/]+)/(?<ts>[0-9]{8}-[0-9]{6})/(?:setup|attempt-(?<n>[0-9]+)-acceptance)\.log\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex TaskLog();

    // <id>-integration-setup.log and <id>-integration-check.log
    [GeneratedRegex(@"\A(?<id>[^/]+)-integration-(?:(?<setup>setup)|check)\.log\z", RegexOptions.CultureInvariant)]
    private static partial Regex IntegrationLog();

    // bootstrap-<ts>/attempt-<n>-setup.log and bootstrap-<ts>/attempt-<n>-integration-check.log
    [GeneratedRegex(@"\A(?<folder>bootstrap-[0-9]{8}-[0-9]{6})/attempt-(?<n>[0-9]+)-(?:(?<setup>setup)|integration-check)\.log\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex BootstrapLog();
}
