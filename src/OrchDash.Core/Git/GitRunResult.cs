namespace OrchDash.Core.Git;

/// <summary>
/// The outcome of one git command. <c>Problem</c> is null on success, else the first line of stderr, <c>timed out</c>
/// or the exception message (20.4); <c>CouldNotStart</c> is set when git itself could not be started (20.5).
/// </summary>
internal sealed record GitRunResult(int? ExitCode, string Stdout, string Stderr, string? Problem, bool CouldNotStart);
