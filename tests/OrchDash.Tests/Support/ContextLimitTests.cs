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
}
