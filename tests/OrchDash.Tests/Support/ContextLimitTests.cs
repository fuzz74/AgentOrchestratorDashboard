using System.Collections.Immutable;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.Support;

public sealed class ContextLimitTests
{
    private readonly RunSnapshot _run = SampleRun.Create();

    private Session Get(string key) => _run.Sessions.Single(s => s.Files.Key == key);

    private static Session WithModelAndWindow(Session session, string? model, long? window) =>
        session with
        {
            Content = session.Content with
            {
                Model = model,
                Result = window is null ? null : session.Content.Result! with { ContextWindow = window },
            },
        };

    [Fact]
    public void A_session_with_a_window_in_its_result_gets_it()
    {
        Assert.Equal(200_000, ContextLimit.For(_run, Get(SampleRun.AlphaWorkerKey)));
    }

    [Fact]
    public void A_session_without_a_result_gets_the_window_of_a_session_with_the_same_model()
    {
        Assert.Equal(200_000, ContextLimit.For(_run, Get(SampleRun.BetaWorkerKey)));
    }

    [Fact]
    public void A_session_whose_model_has_no_window_anywhere_gets_null()
    {
        Assert.Null(ContextLimit.For(_run, Get(SampleRun.AlphaReviewKey)));
    }

    [Fact]
    public void A_session_without_a_model_gets_null_even_when_another_session_without_a_model_has_a_window()
    {
        var alpha = Get(SampleRun.AlphaWorkerKey);
        var withWindow = WithModelAndWindow(alpha, null, 100_000);
        var withoutWindow = Get(SampleRun.BetaWorkerKey) with
        {
            Content = Get(SampleRun.BetaWorkerKey).Content with { Model = null },
        };
        var run = _run with { Sessions = [withWindow, withoutWindow] };

        Assert.Null(ContextLimit.For(run, withoutWindow));
    }

    [Fact]
    public void Models_are_compared_ordinally()
    {
        var beta = Get(SampleRun.BetaWorkerKey);
        var upper = beta with { Content = beta.Content with { Model = "Claude-Sonnet-4-5" } };
        var run = _run with { Sessions = [Get(SampleRun.AlphaWorkerKey), upper] };

        Assert.Null(ContextLimit.For(run, upper));
    }

    [Fact]
    public void The_largest_window_of_the_same_model_wins()
    {
        var alpha = Get(SampleRun.AlphaWorkerKey);
        var beta = Get(SampleRun.BetaWorkerKey);
        var large = WithModelAndWindow(alpha, alpha.Content.Model, 1_000_000);
        var run = _run with { Sessions = [large, alpha, beta] };

        Assert.Equal(1_000_000, ContextLimit.For(run, beta));
        Assert.Equal(200_000, ContextLimit.For(run, alpha));
    }

    [Fact]
    public void An_empty_snapshot_does_not_throw()
    {
        var run = RunSnapshot.Empty(SampleRun.RepoPath);

        Assert.Null(ContextLimit.For(run, Get(SampleRun.BetaWorkerKey)));
        Assert.Equal(200_000, ContextLimit.For(run, Get(SampleRun.AlphaWorkerKey)));
    }

    [Fact]
    public void A_snapshot_with_a_default_sessions_array_does_not_throw()
    {
        var run = _run with { Sessions = default(ImmutableArray<Session>) };

        Assert.Null(ContextLimit.For(run, Get(SampleRun.BetaWorkerKey)));
        Assert.Equal(200_000, ContextLimit.For(run, Get(SampleRun.AlphaWorkerKey)));
    }

    [Fact]
    public void ForModel_gets_the_window_of_a_session_with_that_model()
    {
        // (42.3) a sub-agent's limit
        Assert.Equal(200_000, ContextLimit.ForModel(_run, Get(SampleRun.AlphaWorkerKey).Content.Model));
    }

    [Fact]
    public void ForModel_of_a_null_model_is_null_even_when_a_session_without_a_model_has_a_window()
    {
        var run = _run with { Sessions = [WithModelAndWindow(Get(SampleRun.AlphaWorkerKey), null, 100_000)] };

        Assert.Null(ContextLimit.ForModel(run, null));
    }

    [Fact]
    public void ForModel_of_a_model_without_a_window_anywhere_is_null()
    {
        var reviewModel = Get(SampleRun.AlphaReviewKey).Content.Model;

        Assert.NotNull(reviewModel);
        Assert.Null(ContextLimit.ForModel(_run, reviewModel));
        Assert.Null(ContextLimit.ForModel(_run, "claude-haiku-4-5"));
        Assert.Null(ContextLimit.ForModel(_run, Get(SampleRun.AlphaWorkerKey).Content.Model!.ToUpperInvariant()));
    }

    [Fact]
    public void ForModel_gets_the_largest_window_of_the_model()
    {
        var alpha = Get(SampleRun.AlphaWorkerKey);
        var model = alpha.Content.Model;
        var run = _run with
        {
            Sessions =
            [
                WithModelAndWindow(alpha, model, 100_000),
                WithModelAndWindow(alpha, model, 1_000_000),
                alpha,
                WithModelAndWindow(alpha, "other-model", 2_000_000),
            ],
        };

        Assert.Equal(1_000_000, ContextLimit.ForModel(run, model));
    }

    [Fact]
    public void ForModel_with_a_default_sessions_array_is_null()
    {
        var run = _run with { Sessions = default(ImmutableArray<Session>) };

        Assert.Null(ContextLimit.ForModel(run, Get(SampleRun.AlphaWorkerKey).Content.Model));
        Assert.Null(ContextLimit.ForModel(RunSnapshot.Empty(SampleRun.RepoPath), Get(SampleRun.AlphaWorkerKey).Content.Model));
    }
}
