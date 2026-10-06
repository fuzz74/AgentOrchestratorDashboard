using OrchDash.Core.Model;
using OrchDash.Shell;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Shell;

public sealed class RunsDialogTextTests
{
    private const string ArchivePath = @"C:\Work\SampleRepo.runs\20261003-110000";

    private static readonly RunEntry Current = new(
        SampleRun.RepoPath, null, "spec.md", SampleRun.At(12, 0, 0), null, 5, 1, 1, Provider.Claude, null, null);

    private static readonly RunEntry Archived = new(
        ArchivePath, "20261003-110000", null, SampleRun.At(11, 0, 0), SampleRun.At(11, 53, 38), 5, 5, 0, Provider.Copilot, "gpt-5", null);

    private static readonly RunEntry ModelOnly = new(
        @"C:\Work\SampleRepo.runs\20261002-090000", "20261002-090000", null, null, null, 0, 0, 0, Provider.Unknown, "opus", null);

    private static readonly RunEntry Unknown = new(
        @"C:\Work\SampleRepo.runs\20261001-080000", "20261001-080000", null, null, null, 12, 0, 0, Provider.Unknown, null, null);

    [Fact]
    public void Without_a_catalog_the_only_row_is_loading()
    {
        var rows = RunsDialogText.Rows(null, SampleRun.RepoPath);

        Assert.Equal([new RunsRow("[muted]loading…[/]", null, false)], rows);
    }

    [Fact]
    public void The_header_and_each_run_are_padded_to_the_longest_value_of_their_column()
    {
        var rows = RunsDialogText.Rows(new RunCatalog([Current, Archived, ModelOnly, Unknown], null), SampleRun.RepoPath);

        Assert.Equal(
            [
                "[muted]   run              spec     started           finished          tasks                         provider[/]",
                "●  current          spec.md  2026-10-03 12:00  -                 1 done · 1 failed · 5 tasks   Claude",
                "   20261003-110000  -        2026-10-03 11:00  2026-10-03 11:53  5 done · 0 failed · 5 tasks   Copilot",
                "   20261002-090000  -        -                 -                 0 done · 0 failed · 0 tasks   opus",
                "   20261001-080000  -        -                 -                 0 done · 0 failed · 12 tasks  -",
            ],
            rows.Select(row => row.Markup));
        Assert.Equal([null, Current, Archived, ModelOnly, Unknown], rows.Select(row => row.Entry));
        Assert.Equal([false, true, false, false, false], rows.Select(row => row.Shown));
    }

    [Fact]
    public void The_marker_follows_the_shown_run_by_path_ignoring_case_and_a_trailing_separator()
    {
        var rows = RunsDialogText.Rows(new RunCatalog([Current, Archived], null), ArchivePath.ToUpperInvariant() + @"\");

        Assert.Equal([false, false, true], rows.Select(row => row.Shown));
        Assert.StartsWith("   current", rows[1].Markup, StringComparison.Ordinal);
        Assert.StartsWith("●  20261003-110000", rows[2].Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_problem_is_appended_in_warning_and_a_catalog_problem_is_the_last_row()
    {
        var broken = Archived with { Problem = "tasks.json: not JSON\nstate.json: [locked]" };
        var rows = RunsDialogText.Rows(new RunCatalog([Current, broken], @"C:\Work\SampleRepo.runs: Access denied."), SampleRun.RepoPath);

        Assert.Equal(4, rows.Length);
        Assert.Equal(
            "   20261003-110000  -        2026-10-03 11:00  2026-10-03 11:53  5 done · 0 failed · 5 tasks  Copilot"
                + "[warning] · tasks.json: not JSON · state.json: [[locked]][/]",
            rows[2].Markup);
        Assert.Equal(new RunsRow(@"[warning]C:\Work\SampleRepo.runs: Access denied.[/]", null, false), rows[3]);
    }

    [Fact]
    public void An_empty_catalog_has_the_header_row_only_and_markup_in_values_is_escaped()
    {
        Assert.Equal(
            ["[muted]   run  spec  started  finished  tasks  provider[/]"],
            RunsDialogText.Rows(new RunCatalog([], null), SampleRun.RepoPath).Select(row => row.Markup));

        var odd = Current with { Spec = "[odd].md" };
        Assert.Contains("[[odd]].md", RunsDialogText.Rows(new RunCatalog([odd], null), SampleRun.RepoPath)[1].Markup, StringComparison.Ordinal);
    }
}
