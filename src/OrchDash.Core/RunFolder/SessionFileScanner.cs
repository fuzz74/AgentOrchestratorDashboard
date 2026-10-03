using System.Collections.Immutable;
using System.Globalization;
using System.Security;
using System.Text.RegularExpressions;
using OrchDash.Core.Model;

namespace OrchDash.Core.RunFolder;

/// <summary>
/// Finds the agent sessions under <c>logs/</c> by the file-name grammar of spec 4.3. Uses only directory
/// listings and the file metadata they return: no file is opened.
/// </summary>
public static partial class SessionFileScanner
{
    private const string PromptSuffix = ".prompt.md";
    private const string EventsSuffix = ".events.jsonl";
    private const string StderrSuffix = ".stderr";
    private const string NudgeSuffix = ".nudge.json";

    // logsDir is <runDir>/logs. Never throws. A missing folder gives an empty array and no problem.
    // A folder that cannot be listed adds one line that names it to problems.
    public static ImmutableArray<SessionFiles> Scan(string logsDir, ICollection<string> problems)
    {
        DirectoryInfo logs;
        try
        {
            logs = new DirectoryInfo(logsDir);
        }
        catch (Exception e) when (e is ArgumentException or PathTooLongException)
        {
            problems.Add($"{logsDir}: {e.Message}");
            return [];
        }

        var sessions = new Dictionary<string, SessionBuilder>(StringComparer.Ordinal);
        foreach (var entry in List(logs, "logs", problems))
        {
            if (entry is FileInfo file)
            {
                AddFile(sessions, "", file, Level.Root, null, null);
            }
            else if (entry is DirectoryInfo dir && BootstrapFolder().IsMatch(dir.Name))
            {
                foreach (var child in List(dir, "logs/" + dir.Name, problems))
                {
                    if (child is FileInfo childFile)
                        AddFile(sessions, dir.Name + "/", childFile, Level.Bootstrap, null, dir.Name);
                }
            }
            else if (entry is DirectoryInfo taskDir)
            {
                var taskId = taskDir.Name;
                foreach (var child in List(taskDir, "logs/" + taskId, problems))
                {
                    if (child is not DirectoryInfo startDir || !StartFolder().IsMatch(startDir.Name))
                        continue;

                    var folder = taskId + "/" + startDir.Name;
                    foreach (var grandChild in List(startDir, "logs/" + folder, problems))
                    {
                        if (grandChild is FileInfo sessionFile)
                            AddFile(sessions, folder + "/", sessionFile, Level.Start, taskId, startDir.Name);
                    }
                }
            }
        }

        return [.. sessions.Values.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => s.Build())];
    }

    private static FileSystemInfo[] List(DirectoryInfo dir, string name, ICollection<string> problems)
    {
        try
        {
            return dir.GetFileSystemInfos();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            problems.Add($"{name}: {e.Message}");
            return [];
        }
    }

    private static void AddFile(Dictionary<string, SessionBuilder> sessions, string keyPrefix, FileInfo file,
        Level level, string? taskId, string? startFolder)
    {
        var name = file.Name;
        var kind = FileKind.Result;
        if (name.EndsWith(PromptSuffix, StringComparison.Ordinal))
            (kind, name) = (FileKind.Prompt, name[..^PromptSuffix.Length]);
        else if (name.EndsWith(EventsSuffix, StringComparison.Ordinal))
            (kind, name) = (FileKind.Events, name[..^EventsSuffix.Length]);
        else if (name.EndsWith(StderrSuffix, StringComparison.Ordinal))
            (kind, name) = (FileKind.Stderr, name[..^StderrSuffix.Length]);

        // name is now the session's X.json; a nudge is "<X.json>.nudge.json" and takes its values from X.
        var isNudge = name.EndsWith(".json" + NudgeSuffix, StringComparison.Ordinal);
        var baseName = isNudge ? name[..^NudgeSuffix.Length] : name;
        if (!TryParseName(level, baseName, out var role, out var attempt, out var reviewTry))
            return;

        var key = keyPrefix + name;
        if (!sessions.TryGetValue(key, out var session))
        {
            session = new SessionBuilder(key, Path.Combine(file.DirectoryName!, name),
                taskId, role, startFolder, attempt, reviewTry, isNudge);
            sessions.Add(key, session);
        }

        switch (kind)
        {
            case FileKind.Result:
                session.HasResultFile = true;
                break;
            case FileKind.Events:
                session.HasEventsFile = true;
                break;
            case FileKind.Prompt:
                session.PromptWrittenAt = new DateTimeOffset(file.LastWriteTime);
                break;
            case FileKind.Stderr:
                break;
        }
    }

    private static bool TryParseName(Level level, string name, out AgentRole role, out int attempt, out int reviewTry)
    {
        role = default;
        attempt = 0;
        reviewTry = 0;
        switch (level)
        {
            case Level.Root:
                var planner = PlannerSession().Match(name);
                role = AgentRole.Planner;
                return planner.Success && TryParseNumber(planner.Groups["n"], out attempt);

            case Level.Bootstrap:
                var bootstrap = BootstrapSession().Match(name);
                role = AgentRole.Bootstrap;
                return bootstrap.Success && TryParseNumber(bootstrap.Groups["n"], out attempt);

            default:
                var task = TaskSession().Match(name);
                if (!task.Success || !TryParseNumber(task.Groups["n"], out attempt))
                    return false;
                if (task.Groups["worker"].Success)
                {
                    role = AgentRole.Worker;
                    return true;
                }
                if (task.Groups["resolver"].Success)
                {
                    role = AgentRole.Resolver;
                    return true;
                }
                role = AgentRole.Reviewer;
                return TryParseNumber(task.Groups["t"], out reviewTry);
        }
    }

    private static bool TryParseNumber(Group group, out int value) =>
        int.TryParse(group.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    [GeneratedRegex(@"\Abootstrap-[0-9]{8}-[0-9]{6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex BootstrapFolder();

    [GeneratedRegex(@"\A[0-9]{8}-[0-9]{6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex StartFolder();

    [GeneratedRegex(@"\Aplanner-[0-9]{8}-[0-9]{6}-(?<n>[0-9]+)\.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex PlannerSession();

    [GeneratedRegex(@"\Aattempt-(?<n>[0-9]+)\.json\z", RegexOptions.CultureInvariant)]
    private static partial Regex BootstrapSession();

    [GeneratedRegex(@"\Aattempt-(?<n>[0-9]+)-(?:(?<worker>worker)|(?<resolver>resolver)|review-(?<t>[0-9]+))\.json\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex TaskSession();

    /// <summary>Where a folder sits in the tree, which decides the grammar row its files can match.</summary>
    private enum Level { Root, Bootstrap, Start }

    private enum FileKind { Result, Prompt, Events, Stderr }

    private sealed class SessionBuilder(string key, string resultPath, string? taskId, AgentRole role,
        string? startFolder, int attempt, int reviewTry, bool isNudge)
    {
        public string Key { get; } = key;
        public bool HasResultFile { get; set; }
        public bool HasEventsFile { get; set; }
        public DateTimeOffset? PromptWrittenAt { get; set; }

        public SessionFiles Build() => new(
            Key, taskId, role, startFolder, attempt, reviewTry, isNudge,
            resultPath, resultPath + PromptSuffix, resultPath + EventsSuffix, resultPath + StderrSuffix,
            HasResultFile, HasEventsFile, PromptWrittenAt);
    }
}
