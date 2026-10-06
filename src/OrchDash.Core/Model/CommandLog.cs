namespace OrchDash.Core.Model;

public sealed record CommandLog(CommandKind Kind, string? TaskId, string? StartFolder, int? Attempt,
    string Key,                                       // path under logs/, forward slashes, as SessionFiles.Key; unique
    string Path, string StderrPath,                   // absolute: <runDir>\logs\<Key> with backslashes, and <Path>.stderr
    DateTimeOffset WrittenAt, long Length, string Text, string StderrText,
    string? Command, CommandOutcome Outcome, int? ExitCode);
