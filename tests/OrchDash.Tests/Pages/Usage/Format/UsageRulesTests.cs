using OrchDash.Core.Model;
using OrchDash.Pages.Usage.Format;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Pages.Usage.Format;

public sealed class UsageRulesTests
{
    private readonly RunSnapshot _run = SampleRun.CreateEnriched();

    private Session AlphaWorker => Session(SampleRun.AlphaWorkerKey);
    private Session AlphaReview => Session(SampleRun.AlphaReviewKey);
    private Session BetaWorker => Session(SampleRun.BetaWorkerKey);
    private Session GammaWorker1 => Session(SampleRun.GammaWorker1Key);
    private Session GammaWorker2 => Session(SampleRun.GammaWorker2Key);

    private Session Session(string key) => _run.Sessions.Single(s => s.Files.Key == key);

    [Fact]
    public void Alpha_worker_takes_output_from_the_calls_cost_from_the_result_and_lines_from_its_own_transcript()
    {
        Assert.Equal(
            new UsageFigures(1, 2, 2_000, 56_000, 4_500, 3_100, 1_390, 43_300, 0.25, null, null, 12, 3),
            UsageRules.Of(_run, AlphaWorker));
    }

    [Fact]
    public void Alpha_review_takes_aiu_from_the_calls_and_premium_requests_and_lines_from_the_result()
    {
        Assert.Equal(
            new UsageFigures(1, 2, 30, 14_180, 16_940, 1_215, 508, 16_950, null, 1, 3_990_000_000, 0, 0),
            UsageRules.Of(_run, AlphaReview));
    }

    [Fact]
    public void Running_beta_worker_without_output_or_result_has_unknown_output_and_cost()
    {
        Assert.Equal(
            new UsageFigures(1, 2, 1_700, 47_800, 3_800, null, null, 34_500, null, null, null, null, null),
            UsageRules.Of(_run, BetaWorker));
    }

    [Fact]
    public void Gamma_attempts_take_cost_from_their_results()
    {
        Assert.Equal(
            new UsageFigures(1, 2, 6, 32_000, 17_500, 2_270, null, 26_502, 0.29, null, null, null, null),
            UsageRules.Of(_run, GammaWorker1));
        Assert.Equal(
            new UsageFigures(1, 2, 4, 57_200, 7_000, 2_850, null, 33_501, 0.33, null, null, null, null),
            UsageRules.Of(_run, GammaWorker2));
    }

    [Fact]
    public void Gamma_attempts_share_a_session_id_so_nothing_comes_from_their_transcript()
    {
        var stores = GammaWorker1.Stores with { CostUsd = 0.9, LinesAdded = 40, LinesRemoved = 7 };
        var gamma1 = WithResult(GammaWorker1, r => r with { CostUsd = null }) with { Stores = stores };
        var run = Replace(_run, gamma1, GammaWorker2 with { Stores = stores });

        var figures = UsageRules.Of(run, gamma1);

        Assert.Null(figures.CostUsd);
        Assert.Null(figures.LinesAdded);
        Assert.Null(figures.LinesRemoved);
    }

    [Fact]
    public void Without_calls_with_usage_the_token_figures_come_from_the_result()
    {
        var worker = WithCalls(AlphaWorker, AlphaWorker.Content.Calls.Select(c => c with { Usage = null }));

        var figures = UsageRules.Of(Replace(_run, worker), worker);

        Assert.Equal((2_000L, 56_000L, 4_500L, 3_100L), (figures.Input, figures.CacheRead, figures.CacheWrite, figures.Output));
        Assert.Null(figures.PeakContext);
        Assert.Equal(1_390, figures.Thinking);
        Assert.Equal(2, figures.Calls);
    }

    [Fact]
    public void A_call_with_usage_but_without_output_takes_the_output_from_the_result()
    {
        var calls = AlphaWorker.Content.Calls;
        var worker = WithResult(
            WithCalls(AlphaWorker, [calls[0], calls[1] with { Usage = calls[1].Usage! with { Output = null } }]),
            r => r with { Usage = new TokenUsage(1, 2, 3, 3_333) });

        var figures = UsageRules.Of(Replace(_run, worker), worker);

        Assert.Equal((2_000L, 56_000L, 4_500L), (figures.Input, figures.CacheRead, figures.CacheWrite));
        Assert.Equal(3_333, figures.Output);
        Assert.Equal(43_300, figures.PeakContext);
    }

    [Fact]
    public void A_call_without_output_and_no_result_usage_leaves_the_output_unknown()
    {
        var calls = AlphaReview.Content.Calls;
        var review = WithCalls(AlphaReview, [calls[0], calls[1] with { Usage = calls[1].Usage! with { Output = null } }]);

        Assert.Null(UsageRules.Of(Replace(_run, review), review).Output);
    }

    [Fact]
    public void Without_result_the_cost_and_lines_come_from_a_transcript_of_its_own()
    {
        var worker = AlphaWorker with
        {
            Content = AlphaWorker.Content with { Result = null },
            Stores = AlphaWorker.Stores with { CostUsd = 0.4 },
        };

        var figures = UsageRules.Of(Replace(_run, worker), worker);

        Assert.Equal(0.4, figures.CostUsd);
        Assert.Equal((12, 3), (figures.LinesAdded, figures.LinesRemoved));
    }

    [Fact]
    public void Without_result_a_transcript_shared_with_another_session_gives_no_cost_or_lines()
    {
        var worker = AlphaWorker with
        {
            Content = AlphaWorker.Content with { Result = null },
            Stores = AlphaWorker.Stores with { CostUsd = 0.4 },
        };
        var second = worker with
        {
            Files = worker.Files with { Key = "alpha/20261003-120005/attempt-2-worker.json", Attempt = 2 },
        };
        var replaced = Replace(_run, worker);
        var run = replaced with { Sessions = [.. replaced.Sessions, second] };

        foreach (var session in new[] { worker, second })
        {
            var figures = UsageRules.Of(run, session);
            Assert.Null(figures.CostUsd);
            Assert.Null(figures.LinesAdded);
            Assert.Null(figures.LinesRemoved);
        }
    }

    [Fact]
    public void Aiu_and_premium_requests_come_from_the_checkpoint_only_when_the_calls_and_result_have_none()
    {
        var checkpoint = AlphaReview.Content.Checkpoint! with { NanoAiu = 5_000_000_000, PremiumRequests = 2 };
        var review = AlphaReview with { Content = AlphaReview.Content with { Checkpoint = checkpoint } };
        var bare = WithResult(
            WithCalls(review, review.Content.Calls.Select(c => c with { NanoAiu = null })),
            r => r with { PremiumRequests = null });

        var withCalls = UsageRules.Of(Replace(_run, review), review);
        var fromCheckpoint = UsageRules.Of(Replace(_run, bare), bare);

        Assert.Equal((3_990_000_000L, 1.0), (withCalls.NanoAiu, withCalls.PremiumRequests));
        Assert.Equal((5_000_000_000L, 2.0), (fromCheckpoint.NanoAiu, fromCheckpoint.PremiumRequests));
    }

    [Fact]
    public void Group_of_a_session_is_its_task_else_its_role()
    {
        Assert.Equal("alpha", UsageRules.GroupOf(AlphaReview));
        Assert.Equal("bootstrap", UsageRules.GroupOf(WithFiles(AlphaWorker, "bootstrap-20261003-115900/attempt-1.json", null, AgentRole.Bootstrap)));
        Assert.Equal("planner", UsageRules.GroupOf(WithFiles(AlphaWorker, "planner-20261003-115930-1.json", null, AgentRole.Planner)));
    }

    [Fact]
    public void Groups_of_the_sample_follow_the_task_order_and_leave_out_tasks_without_sessions()
    {
        var groups = UsageRules.Groups(_run);

        Assert.Equal(["alpha", "gamma", "beta"], groups.Select(g => g.Name));
        Assert.Equal([SampleRun.AlphaWorkerKey, SampleRun.AlphaReviewKey], groups[0].Sessions.Select(s => s.Files.Key));
        Assert.Equal([SampleRun.GammaWorker1Key, SampleRun.GammaWorker2Key], groups[1].Sessions.Select(s => s.Files.Key));
        Assert.Equal([SampleRun.BetaWorkerKey], groups[2].Sessions.Select(s => s.Files.Key));
    }

    [Fact]
    public void Bootstrap_and_planner_come_first_and_unknown_tasks_last_in_order_of_first_appearance()
    {
        var bootstrap = WithId(WithFiles(AlphaWorker, "bootstrap-20261003-115900/attempt-1.json", null, AgentRole.Bootstrap), "boot");
        var planner = WithId(WithFiles(AlphaReview, "planner-20261003-115930-1.json", null, AgentRole.Planner), "plan");
        var omega = WithId(WithFiles(BetaWorker, "omega/20261003-121500/attempt-1-worker.json", "omega", AgentRole.Worker), "omega");
        var zeta = WithId(WithFiles(BetaWorker, "zeta/20261003-121400/attempt-1-worker.json", "zeta", AgentRole.Worker), "zeta");
        var run = _run with { Sessions = [omega, .. _run.Sessions, zeta, planner, bootstrap] };

        Assert.Equal(["bootstrap", "planner", "alpha", "gamma", "beta", "omega", "zeta"],
            UsageRules.Groups(run).Select(g => g.Name));
    }

    [Fact]
    public void Group_figures_are_the_sums_of_their_sessions()
    {
        var groups = UsageRules.Groups(_run);

        Assert.Equal(
            new UsageFigures(2, 4, 2_030, 70_180, 21_440, 4_315, 1_898, 43_300, 0.25, 1, 3_990_000_000, 12, 3),
            groups[0].Figures);
        Assert.Equal(
            new UsageFigures(1, 2, 1_700, 47_800, 3_800, null, null, 34_500, null, null, null, null, null),
            groups[2].Figures);
    }

    [Fact]
    public void Group_figures_take_the_largest_peak_and_stay_unknown_when_no_session_knows_them()
    {
        var gamma = UsageRules.Groups(_run)[1].Figures;

        Assert.Equal((2, 4), (gamma.Sessions, gamma.Calls));
        Assert.Equal((10L, 89_200L, 24_500L, 5_120L), (gamma.Input, gamma.CacheRead, gamma.CacheWrite, gamma.Output));
        Assert.Equal(33_501, gamma.PeakContext);
        Assert.Equal(0.62, gamma.CostUsd!.Value, 9);
        Assert.Null(gamma.Thinking);
        Assert.Null(gamma.PremiumRequests);
        Assert.Null(gamma.NanoAiu);
        Assert.Null(gamma.LinesAdded);
        Assert.Null(gamma.LinesRemoved);
    }

    [Fact]
    public void Run_figures_are_the_sums_over_all_sessions()
    {
        var total = UsageRules.Total(_run);

        Assert.Equal((5, 10), (total.Sessions, total.Calls));
        Assert.Equal((3_740L, 207_180L, 49_740L, 9_435L, 1_898L),
            (total.Input, total.CacheRead, total.CacheWrite, total.Output, total.Thinking));
        Assert.Equal(43_300, total.PeakContext);
        Assert.Equal(0.87, total.CostUsd!.Value, 9);
        Assert.Equal((1.0, 3_990_000_000L, 12, 3), (total.PremiumRequests, total.NanoAiu, total.LinesAdded, total.LinesRemoved));
    }

    [Fact]
    public void Sum_of_nothing_knows_no_figure()
    {
        Assert.Equal(new UsageFigures(0, 0, null, null, null, null, null, null, null, null, null, null, null),
            UsageRules.Sum([]));
    }

    [Fact]
    public void Tokens_count_an_unknown_figure_as_zero()
    {
        Assert.Equal(53_300, UsageRules.Of(_run, BetaWorker).Tokens);
    }

    [Fact]
    public void Latest_rate_limit_is_the_one_with_the_latest_seen_at()
    {
        var beta = new RateLimit("allowed_warning", "seven_day", 0.5, null, 0.9, null, SampleRun.At(12, 10, 6));
        var early = new RateLimit("allowed", "five_hour", 0.1, null, 0.1, null, null);
        var run = Replace(_run,
            BetaWorker with { Content = BetaWorker.Content with { RateLimit = beta } },
            GammaWorker2 with { Content = GammaWorker2.Content with { RateLimit = early } });

        Assert.Same(AlphaWorker.Content.RateLimit, UsageRules.LatestRateLimit(_run));
        Assert.Same(beta, UsageRules.LatestRateLimit(run));
        Assert.Null(UsageRules.LatestRateLimit(SampleRun.Create()));
    }

    [Fact]
    public void Versions_of_each_provider_are_distinct_and_in_session_order()
    {
        var gamma1 = GammaWorker1 with
        {
            Content = GammaWorker1.Content with { Init = null },
            Stores = GammaWorker1.Stores with { CliVersion = "2.1.285" },
        };
        var run = Replace(_run, gamma1);

        Assert.Equal(["2.1.3"], UsageRules.Versions(_run, Provider.Claude));
        Assert.Equal(["1.0.91"], UsageRules.Versions(_run, Provider.Copilot));
        Assert.Equal(["2.1.3", "2.1.285"], UsageRules.Versions(run, Provider.Claude));
    }

    // The snapshot with each given session put in place of the session with its key.
    internal static RunSnapshot Replace(RunSnapshot run, params Session[] sessions) => run with
    {
        Sessions = [.. run.Sessions.Select(s => sessions.FirstOrDefault(n => n.Files.Key == s.Files.Key) ?? s)],
    };

    internal static Session WithFiles(Session session, string key, string? taskId, AgentRole role) =>
        session with { Files = session.Files with { Key = key, TaskId = taskId, Role = role } };

    private static Session WithId(Session session, string sessionId) =>
        session with { Content = session.Content with { SessionId = sessionId } };

    private static Session WithCalls(Session session, IEnumerable<ModelCall> calls) =>
        session with { Content = session.Content with { Calls = [.. calls] } };

    private static Session WithResult(Session session, Func<SessionResult, SessionResult> change) =>
        session with { Content = session.Content with { Result = change(session.Content.Result!) } };
}
