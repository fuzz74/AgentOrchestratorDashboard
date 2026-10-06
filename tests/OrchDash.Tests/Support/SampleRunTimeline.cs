using OrchDash.Core.Model;

namespace OrchDash.Tests.Support;

// CreateInsight() with the whole orchestrator log of the run, for the timeline and replay.
public static partial class SampleRun
{
    public static RunSnapshot CreateTimeline()
    {
        var run = CreateInsight();
        var created = Create().Progress;
        return run with
        {
            Progress =
            [
                created[0],
                Info(12, 0, 0, null, @"Claude: C:\Users\sample\.local\bin\claude.exe"),
                Info(12, 0, 5, "alpha", @"started (fresh) in C:\Work\SampleRepo.worktrees\alpha"),
                Info(12, 0, 5, "gamma", @"started (fresh) in C:\Work\SampleRepo.worktrees\gamma"),
                Info(12, 0, 8, "alpha", "setup: dotnet restore Sample.slnx"),
                Info(12, 0, 9, "alpha", "attempt 1/3: worker started (sonnet)"),
                Info(12, 0, 10, "gamma", "setup: dotnet restore Sample.slnx"),
                Info(12, 0, 28, "gamma", "attempt 1/3: worker started (opus)"),
                Info(12, 4, 25, "gamma", "acceptance: dotnet test tests/Gamma"),
                Failure(12, 4, 31, "gamma", "acceptance failed (exit 1)"),
                Info(12, 6, 0, "alpha", "acceptance: dotnet test tests/Alpha"),
                Info(12, 6, 25, "alpha", "review started (sonnet)"),
                new ProgressEntry(At(12, 9, 0), "alpha", "review passed", ProgressKind.Success),
                created[1],
                Info(12, 10, 0, "beta", @"started (fresh) in C:\Work\SampleRepo.worktrees\beta"),
                Info(12, 10, 3, "beta", "attempt 1/3: worker started (sonnet)"),
                Info(12, 10, 55, "gamma", "attempt 2/3: worker started (opus)"),
                Info(12, 15, 35, "gamma", "acceptance: dotnet test tests/Gamma"),
                Failure(12, 15, 41, "gamma", "acceptance failed (exit 1)"),
                Info(12, 17, 0, "gamma", "attempt 3/3: worker started (opus)"),
                Info(12, 19, 45, "gamma", "acceptance: dotnet test tests/Gamma"),
                Failure(12, 19, 51, "gamma", "acceptance failed (exit 1)"),
                created[2],
            ],
        };
    }

    private static ProgressEntry Info(int hour, int minute, int second, string? source, string message) =>
        new(At(hour, minute, second), source, message, ProgressKind.Info);

    private static ProgressEntry Failure(int hour, int minute, int second, string? source, string message) =>
        new(At(hour, minute, second), source, message, ProgressKind.Failure);
}
