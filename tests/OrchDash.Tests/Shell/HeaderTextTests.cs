using OrchDash.Core.Model;
using OrchDash.Shell;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Shell;

public sealed class HeaderTextTests
{
    [Fact]
    public void A_running_run_shows_the_folder_name_the_phase_and_the_time_since_the_start()
    {
        var header = HeaderText.Build(SampleRun.Create(), SampleRun.At(12, 30, 0));

        Assert.Equal("[bold]SampleRepo[/]  [accent]Running[/]  30m00s", header.Run);
        Assert.Equal("[warning]1 problems[/]", header.Problems);
        Assert.Equal("[bold]quit[/]", header.Quit);
    }

    [Fact]
    public void A_finished_run_shows_the_time_from_start_to_finish()
    {
        var sample = SampleRun.Create();
        var snapshot = sample with { Run = sample.Run with { Phase = RunPhase.Finished, FinishedAt = SampleRun.At(12, 41, 5) } };

        var header = HeaderText.Build(snapshot, SampleRun.At(13, 0, 0));

        Assert.Equal("[bold]SampleRepo[/]  [accent]Finished[/]  41m05s", header.Run);
    }

    [Fact]
    public void A_run_that_has_not_started_has_no_time_and_no_problems_have_no_count()
    {
        var header = HeaderText.Build(RunSnapshot.Empty(@"C:\Work\Fresh\"), SampleRun.At(12, 0, 0));

        Assert.Equal("[bold]Fresh[/]  [accent]NotStarted[/]", header.Run);
        Assert.Equal("", header.Problems);
    }

    [Fact]
    public void An_archived_run_shows_the_repo_folder_name_and_its_stamp_in_muted()
    {
        var snapshot = SampleRun.Create() with { RepoPath = @"C:\Work\SampleRepo.runs\20261003-120000" };

        var header = HeaderText.Build(snapshot, SampleRun.At(12, 30, 0));

        Assert.Equal("[bold]SampleRepo[/][muted] · 20261003-120000[/]  [accent]Running[/]  30m00s", header.Run);
    }

    [Fact]
    public void A_folder_named_runs_alone_is_not_an_archive()
    {
        var header = HeaderText.Build(RunSnapshot.Empty(@"C:\Work\.runs\20261003-120000"), SampleRun.At(12, 0, 0));

        Assert.Equal("[bold]20261003-120000[/]  [accent]NotStarted[/]", header.Run);
    }

    [Fact]
    public void Markup_characters_in_the_folder_name_are_escaped()
    {
        var header = HeaderText.Build(RunSnapshot.Empty(@"C:\Work\[odd]"), SampleRun.At(12, 0, 0));

        Assert.StartsWith("[bold][[odd]][/]", header.Run, StringComparison.Ordinal);
    }
}
