namespace OrchDash.Core.Model;

public sealed record GitCommit(string Sha, string ShortSha, DateTimeOffset Time, GitCommitKind Kind,
    string Subject, string Body, int? Attempt, string? TaskId);
