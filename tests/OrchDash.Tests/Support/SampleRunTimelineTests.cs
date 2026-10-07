using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.Support;

public sealed class SampleRunTimelineTests
{
    private const ProgressKind Info = ProgressKind.Info;
    private const ProgressKind Success = ProgressKind.Success;
    private const ProgressKind Failure = ProgressKind.Failure;

    private readonly RunSnapshot _run = SampleRun.CreateTimeline();

    [Fact]
    public void Progress_is_the_23_entries_of_the_run()
    {
        Assert.Equal(
        [
            ("12:00:00", null, "Run started: 5 tasks, max 2 in parallel", Info),
            ("12:00:00", null, @"Claude: C:\Users\sample\.local\bin\claude.exe", Info),
            ("12:00:05", "alpha", @"started (fresh) in C:\Work\SampleRepo.worktrees\alpha", Info),
            ("12:00:05", "gamma", @"started (fresh) in C:\Work\SampleRepo.worktrees\gamma", Info),
            ("12:00:08", "alpha", "setup: dotnet restore Sample.slnx", Info),
            ("12:00:09", "alpha", "attempt 1/3: worker started (sonnet)", Info),
            ("12:00:10", "gamma", "setup: dotnet restore Sample.slnx", Info),
            ("12:00:28", "gamma", "attempt 1/3: worker started (opus)", Info),
            ("12:04:25", "gamma", "acceptance: dotnet test tests/Gamma", Info),
            ("12:04:31", "gamma", "acceptance failed (exit 1)", Failure),
            ("12:06:00", "alpha", "acceptance: dotnet test tests/Alpha", Info),
            ("12:06:25", "alpha", "review started (sonnet)", Info),
            ("12:09:00", "alpha", "review passed", Success),
            ("12:09:30", "alpha", "DONE in 9m25s, 0.25 USD\nmerged as 3f9c2e1", Success),
            ("12:10:00", "beta", @"started (fresh) in C:\Work\SampleRepo.worktrees\beta", Info),
            ("12:10:03", "beta", "attempt 1/3: worker started (sonnet)", Info),
            ("12:10:55", "gamma", "attempt 2/3: worker started (opus)", Info),
            ("12:15:35", "gamma", "acceptance: dotnet test tests/Gamma", Info),
            ("12:15:41", "gamma", "acceptance failed (exit 1)", Failure),
            ("12:17:00", "gamma", "attempt 3/3: worker started (opus)", Info),
            ("12:19:45", "gamma", "acceptance: dotnet test tests/Gamma", Info),
            ("12:19:51", "gamma", "acceptance failed (exit 1)", Failure),
            ("12:20:00", "gamma", "FAILED after 3 attempts: acceptance failed", Failure),
        ],
        _run.Progress.Select(p => (Clock(p.Time), p.Source, p.Message, p.Kind)));
    }

    [Fact]
    public void Every_entry_is_on_the_run_day()
    {
        Assert.All(_run.Progress, p =>
        {
            Assert.Equal(SampleRun.At(0, 0, 0).Date, p.Time.Date);
            Assert.Equal(SampleRun.At(p.Time.Hour, p.Time.Minute, p.Time.Second), p.Time);
        });
    }

    [Fact]
    public void The_1st_14th_and_23rd_entries_are_those_of_Create()
    {
        Assert.Equal(SampleRun.Create().Progress, [_run.Progress[0], _run.Progress[13], _run.Progress[22]]);
    }

    [Fact]
    public void Every_other_member_equals_CreateInsight()
    {
        var insight = SampleRun.CreateInsight();

        Assert.Equivalent(insight, _run with { Progress = insight.Progress }, strict: true);
    }

    private static string Clock(DateTimeOffset time) => time.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
}
