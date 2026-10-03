namespace OrchDash.Core.Model;

public sealed record SessionFiles(
    string Key,                    // path of X.json relative to logs/, forward slashes; unique
    string? TaskId, AgentRole Role, string? StartFolder, int Attempt, int ReviewTry, bool IsNudge,
    string ResultPath, string PromptPath, string EventsPath, string StderrPath,   // absolute; may not exist
    bool HasResultFile, bool HasEventsFile,
    DateTimeOffset? PromptWrittenAt);   // last write time of the prompt file
