using OrchDash.Core.Model;
using OrchDash.Pages.ContextWindow.Format;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Pages.ContextWindow.Format;

// The rows and lines of the Context page on SampleRun.CreateEnriched(); colours are checked on the markup.
public sealed class ContextTextTests
{
    private readonly RunSnapshot _run = SampleRun.CreateEnriched();

    private Session Get(string key) => _run.Sessions.Single(s => s.Files.Key == key);

    private ContextMakeup Makeup(string key) => ContextMakeup.Build(_run, Get(key));

    [Fact]
    public void Session_rows_of_each_provider_with_the_latest_context()
    {
        Assert.Equal("[success]✔[/] alpha  [cyan]worker[/]     #1     43.3k",
            ContextText.SessionRow(Get(SampleRun.AlphaWorkerKey), 5));
        Assert.Equal("[success]✔[/] alpha  [yellow]reviewer[/]   #1.1   17.0k",
            ContextText.SessionRow(Get(SampleRun.AlphaReviewKey), 5));
        Assert.Equal("[primary]▶[/] beta   [cyan]worker[/]     #1     34.5k",
            ContextText.SessionRow(Get(SampleRun.BetaWorkerKey), 5));
        Assert.Equal("[error]✖[/] gamma  [cyan]worker[/]     #2     33.5k",
            ContextText.SessionRow(Get(SampleRun.GammaWorker2Key), 5));
    }

    [Fact]
    public void Session_row_without_usage_and_of_a_planner()
    {
        var review = SampleRun.Create().Sessions.Single(s => s.Files.Key == SampleRun.AlphaReviewKey);
        var planner = review with { Files = review.Files with { TaskId = null, Role = AgentRole.Planner } };

        Assert.Equal("[success]✔[/] alpha  [yellow]reviewer[/]   #1.1       -", ContextText.SessionRow(review));
        Assert.Equal("[success]✔[/] planner  [blue]planner[/]    #1         -", ContextText.SessionRow(planner));
        Assert.Equal("planner", ContextText.SessionName(planner));
    }

    [Fact]
    public void Header_of_a_claude_worker_with_the_context_of_the_selected_call()
    {
        var worker = Get(SampleRun.AlphaWorkerKey);
        var makeup = Makeup(SampleRun.AlphaWorkerKey);

        Assert.Equal(
            ["Claude · [cyan]worker[/] · alpha · claude-sonnet-4-5 · 2 calls · peak 43.3k · context 19.2k of 200.0k (10 %)"],
            ContextText.Header(_run, worker, makeup, 0));
        Assert.Equal(
            ["Claude · [cyan]worker[/] · alpha · claude-sonnet-4-5 · 2 calls · peak 43.3k"],
            ContextText.Header(_run, worker, makeup, -1));
    }

    [Fact]
    public void Header_of_the_gamma_chain_counts_the_calls_of_both_attempts()
    {
        Assert.Equal(
            ["Claude · [cyan]worker[/] · gamma · claude-opus-4-5 · 4 calls · peak 33.5k · context 33.5k of 200.0k (17 %)"],
            ContextText.Header(_run, Get(SampleRun.GammaWorker1Key), Makeup(SampleRun.GammaWorker1Key), 3));
    }

    [Fact]
    public void Header_of_a_copilot_review_has_no_limit_and_leaves_out_a_call_without_usage()
    {
        var review = Get(SampleRun.AlphaReviewKey);
        var makeup = Makeup(SampleRun.AlphaReviewKey);
        var withoutUsage = makeup with
        {
            Calls = makeup.Calls.SetItem(1, makeup.Calls[1] with { Call = makeup.Calls[1].Call with { Usage = null } }),
        };

        Assert.Equal(["Copilot · [yellow]reviewer[/] · alpha · gpt-5.1 · 2 calls · peak 17.0k · context 17.0k"],
            ContextText.Header(_run, review, makeup, 1));
        Assert.Equal(["Copilot · [yellow]reviewer[/] · alpha · gpt-5.1 · 2 calls · peak 14.2k"],
            ContextText.Header(_run, review, withoutUsage, 1));
    }

    [Fact]
    public void Header_shows_the_reasons_and_the_unparsed_lines_in_the_warning_colour()
    {
        var beta = Get(SampleRun.BetaWorkerKey);
        var makeup = Makeup(SampleRun.BetaWorkerKey);
        var withMore = beta with
        {
            Unavailable = ["no transcript", "session id not known yet"],
            Stores = beta.Stores with { UnparsedLines = 3 },
        };
        var withOnlyUnparsed = beta with { Unavailable = [], Stores = beta.Stores with { UnparsedLines = 1 } };

        const string first = "Claude · [cyan]worker[/] · beta · claude-sonnet-4-5 · 2 calls · peak 34.5k · context 34.5k of 200.0k (17 %)";
        Assert.Equal([first, "[warning]unavailable: no transcript[/]"], ContextText.Header(_run, beta, makeup, 1));
        Assert.Equal(
        [
            first,
            "[warning]unavailable: no transcript, session id not known yet[/]",
            "[warning]3 lines not understood[/]",
        ], ContextText.Header(_run, withMore, makeup, 1));
        Assert.Equal([first, "[warning]1 lines not understood[/]"], ContextText.Header(_run, withOnlyUnparsed, makeup, 1));
    }

    [Fact]
    public void Header_escapes_model_text()
    {
        var beta = Get(SampleRun.BetaWorkerKey);
        var odd = beta with { Content = beta.Content with { Model = "model[x]" }, Unavailable = [] };

        var header = ContextText.Header(_run, odd, ContextMakeup.Build(_run, odd), -1);

        Assert.Equal(["Claude · [cyan]worker[/] · beta · model[[x]] · 2 calls · peak 34.5k"], header);
    }

    [Fact]
    public void Chart_values_are_the_contexts_of_the_calls_with_usage()
    {
        Assert.Equal([19_200.0, 43_300.0], ContextText.ChartValues(Makeup(SampleRun.AlphaWorkerKey)));
        Assert.Equal([23_004.0, 26_502.0, 30_703.0, 33_501.0], ContextText.ChartValues(Makeup(SampleRun.GammaWorker2Key)));

        var run = WithUsage(SampleRun.GammaWorker1Key, 1, null);
        var makeup = ContextMakeup.Build(run, run.Sessions.Single(s => s.Files.Key == SampleRun.GammaWorker2Key));
        Assert.Equal([23_004.0, 30_703.0, 33_501.0], ContextText.ChartValues(makeup));

        Assert.Empty(ContextText.ChartValues(ContextMakeup.Build(SampleRun.Create(),
            SampleRun.Create().Sessions.Single(s => s.Files.Key == SampleRun.AlphaReviewKey))));
    }

    [Fact]
    public void Call_rows_of_the_alpha_worker_have_no_attempt()
    {
        var makeup = Makeup(SampleRun.AlphaWorkerKey);

        Assert.Equal("call 1  12:00:10   19.2k   +19.2k  out 640     think 210     tool_use", ContextText.CallRow(makeup, 0));
        Assert.Equal("call 2  12:03:00   43.3k   +24.1k  out 2.5k    think 1.2k    end_turn", ContextText.CallRow(makeup, 1));
    }

    [Fact]
    public void Call_rows_of_the_gamma_chain_show_each_attempt()
    {
        var makeup = Makeup(SampleRun.GammaWorker2Key);

        Assert.Equal(
        [
            "call 1  #1  12:00:30   23.0k   +23.0k  out 420     think -       -",
            "call 2  #1  12:02:40   26.5k    +3.5k  out 1.9k    think -       -",
            "call 3  #2  12:11:00   30.7k    +4.2k  out 610     think -       -",
            "call 4  #2  12:14:30   33.5k    +2.8k  out 2.2k    think -       -",
        ], Enumerable.Range(0, 4).Select(i => ContextText.CallRow(makeup, i)));
    }

    [Fact]
    public void Call_row_without_usage_and_the_step_after_it()
    {
        var run = WithUsage(SampleRun.GammaWorker1Key, 1, null);
        var makeup = ContextMakeup.Build(run, run.Sessions.Single(s => s.Files.Key == SampleRun.GammaWorker1Key));

        Assert.Equal("call 2  #1  12:02:40       -        -  out -       think -       -", ContextText.CallRow(makeup, 1));
        Assert.Equal("call 3  #2  12:11:00   30.7k    +7.7k  out 610     think -       -", ContextText.CallRow(makeup, 2));
    }

    [Fact]
    public void Call_row_with_a_smaller_context_shows_a_negative_step()
    {
        var run = WithUsage(SampleRun.GammaWorker2Key, 0, new TokenUsage(3, 20_000, 0, 610));
        var makeup = ContextMakeup.Build(run, run.Sessions.Single(s => s.Files.Key == SampleRun.GammaWorker2Key));

        Assert.Equal("call 3  #2  12:11:00   20.0k    -6.5k  out 610     think -       -", ContextText.CallRow(makeup, 2));
    }

    [Fact]
    public void Call_row_escapes_the_stop_reason_and_shows_a_missing_start()
    {
        var makeup = Makeup(SampleRun.AlphaWorkerKey);
        var call = makeup.Calls[0];
        makeup = makeup with
        {
            Calls = makeup.Calls.SetItem(0, call with { Call = call.Call with { StartedAt = null, StopReason = "[odd]" } }),
        };

        Assert.Equal("call 1  -          19.2k   +19.2k  out 640     think 210     [[odd]]", ContextText.CallRow(makeup, 0));
    }

    [Fact]
    public void Category_lines_with_exact_tokens_and_with_estimates()
    {
        var totals = Makeup(SampleRun.AlphaReviewKey).TotalsAt(0);

        Assert.Equal(
        [
            "[blue]System prompt[/]       2.1k   15 %       0 chars  3 parts",
            "[magenta]Tool definitions[/]     680    5 %       0 chars  1 part",
            "[green]Prompt[/]             11.4k   81 %     166 chars  1 part    est.",
        ], totals.Select(ContextText.CategoryLine));
    }

    [Fact]
    public void Category_tip_shows_the_category_its_tokens_and_its_share()
    {
        var totals = Makeup(SampleRun.AlphaReviewKey).TotalsAt(0);

        Assert.Equal("[blue]System prompt[/]\n2.1k tokens · 15 %", ContextText.CategoryTip(totals[0]));
        Assert.Equal("[green]Prompt[/]\n11.4k tokens · 81 %", ContextText.CategoryTip(totals[2]));
    }

    [Fact]
    public void Category_lines_with_other_and_at_a_call_without_usage()
    {
        var review = Get(SampleRun.AlphaReviewKey);
        review = review with { Content = review.Content with { Items = [] } };
        var other = ContextMakeup.Build(_run, review).TotalsAt(1).Single(t => t.Category == PartCategory.Other);

        var run = WithUsage(SampleRun.GammaWorker1Key, 1, null);
        var totals = ContextMakeup.Build(run, run.Sessions.Single(s => s.Files.Key == SampleRun.GammaWorker1Key))
            .TotalsAt(1);

        Assert.Equal("[gray]Other[/]               2.8k   16 %       0 chars  0 parts", ContextText.CategoryLine(other));
        Assert.Equal("[blue]System prompt[/]          -      -      57 chars  1 part", ContextText.CategoryLine(totals[0]));
    }

    [Fact]
    public void Category_segments_are_the_categories_with_tokens()
    {
        var segments = ContextText.CategorySegments(Makeup(SampleRun.AlphaReviewKey).TotalsAt(0));
        var beta = ContextText.CategorySegments(Makeup(SampleRun.BetaWorkerKey).TotalsAt(0));

        Assert.Equal(
        [
            new CategorySegment(PartCategory.SystemPrompt, "System prompt", "blue", 2_077),
            new CategorySegment(PartCategory.ToolDefinitions, "Tool definitions", "magenta", 680),
            new CategorySegment(PartCategory.Prompt, "Prompt", "green", 14_200 - 2_077 - 680),
        ], segments);
        Assert.Equal([new CategorySegment(PartCategory.Prompt, "Prompt", "green", 18_800)], beta);
    }

    [Fact]
    public void Category_names_and_colours()
    {
        Assert.Equal(
            ["System prompt", "Tool definitions", "Injected", "Prompt", "Conversation", "Other"],
            Enum.GetValues<PartCategory>().Select(ContextText.CategoryName));
        Assert.Equal(
            ["blue", "magenta", "yellow", "green", "cyan", "gray"],
            Enum.GetValues<PartCategory>().Select(ContextText.CategoryColor));
    }

    [Fact]
    public void Part_rows_of_the_checkpoint_parts_and_the_review_conversation()
    {
        var parts = Makeup(SampleRun.AlphaReviewKey).Parts;

        Assert.Equal(
        [
            "[blue]System prompt[/]     identity      310       0 chars  call 1",
            "[blue]System prompt[/]     tone_and_style      227       0 chars  call 1",
            "[blue]System prompt[/]     tool_instructions     1.5k       0 chars  call 1",
            "[magenta]Tool definitions[/]  2 tools      680       0 chars  call 1",
            "[green]Prompt[/]            prompt #1.1   ~11.4k     166 chars  call 1",
            "[cyan]Conversation[/]      thinking     ~723      40 chars  call 2",
            "[cyan]Conversation[/]      view src/Alpha/Parser.cs    ~2.0k     112 chars  call 2",
        ], parts.Select(p => ContextText.PartRow(p)));
    }

    [Fact]
    public void Part_rows_of_blocks_tools_and_injected_items()
    {
        var parts = Makeup(SampleRun.AlphaWorkerKey).Parts;

        // Call 1 shares 19,200 tokens over 1,239 characters, call 2 shares 24,100 over 485.
        Assert.Equal(
        [
            "[blue]System prompt[/]     block 1                    ~883      57 chars  call 1",
            "[blue]System prompt[/]     block 2                   ~2.4k     156 chars  call 1",
            "[magenta]Tool definitions[/]  Read                      ~3.8k     242 chars  call 1",
            "[magenta]Tool definitions[/]  Bash                      ~5.2k     338 chars  call 1",
            "[yellow]Injected[/]          skill_listing             ~1.5k      94 chars  call 1",
            "[yellow]Injected[/]          nested_memory             ~2.0k     129 chars  call 1",
            "[green]Prompt[/]            prompt #1                 ~3.5k     223 chars  call 1",
            "[yellow]Injected[/]          total_tokens_reminder     ~4.0k      80 chars  call 2",
            "[cyan]Conversation[/]      thinking                  ~7.4k     149 chars  call 2",
            "[cyan]Conversation[/]      text                      ~2.7k      54 chars  call 2",
            "[cyan]Conversation[/]      Edit src/Alpha/Pars...   ~10.0k     202 chars  call 2",
        ], parts.Select(p => ContextText.PartRow(p, 22)));
    }

    [Fact]
    public void Part_row_cuts_a_long_label_and_shows_unknown_tokens()
    {
        var run = WithUsage(SampleRun.GammaWorker2Key, 1, null);
        var part = ContextMakeup.Build(run, run.Sessions.Single(s => s.Files.Key == SampleRun.GammaWorker1Key))
            .Parts.Single(p => p.Label == "Edit src/Gamma/Formatter.cs");

        Assert.Equal("[cyan]Conversation[/]      Edit sr...        -     234 chars  call 4",
            ContextText.PartRow(part, 10));
    }

    private RunSnapshot WithUsage(string key, int call, TokenUsage? usage) => _run with
    {
        Sessions =
        [
            .. _run.Sessions.Select(s => s.Files.Key != key ? s : s with
            {
                Content = s.Content with
                {
                    Calls = s.Content.Calls.SetItem(call, s.Content.Calls[call] with { Usage = usage }),
                },
            }),
        ],
    };
}
