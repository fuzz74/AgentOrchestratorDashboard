using System.Text.Json;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.Support;

public sealed class SampleRunEnrichedTests
{
    private readonly RunSnapshot _created = SampleRun.Create();
    private readonly RunSnapshot _run = SampleRun.CreateEnriched();

    private Session AlphaWorker => _run.Sessions[0];
    private Session Gamma1 => _run.Sessions[1];
    private Session AlphaReview => _run.Sessions[2];
    private Session BetaWorker => _run.Sessions[3];
    private Session Gamma2 => _run.Sessions[4];

    [Fact]
    public void Every_call_returns_a_new_snapshot_with_equal_content()
    {
        var other = SampleRun.CreateEnriched();

        Assert.NotSame(_run, other);
        Assert.All(_run.Sessions.Zip(other.Sessions), pair =>
        {
            Assert.NotSame(pair.First, pair.Second);
            Assert.NotSame(pair.First.Content, pair.Second.Content);
        });
        Assert.NotSame(AlphaWorker.Stores, other.Sessions[0].Stores);
        Assert.NotSame(Gamma1.Stores, other.Sessions[1].Stores);
        Assert.Equivalent(_run, other, strict: true);
    }

    [Fact]
    public void Run_fields_are_those_of_Create()
    {
        Assert.Equal(_created.Version, _run.Version);
        Assert.Equal(_created.ReadAt, _run.ReadAt);
        Assert.Equal(_created.RepoPath, _run.RepoPath);
        Assert.Equal(_created.Run, _run.Run);
        Assert.Equivalent(_created.Plan, _run.Plan, strict: true);
        Assert.Equivalent(_created.Tasks, _run.Tasks, strict: true);
        Assert.Equal(_created.Progress, _run.Progress);
    }

    [Fact]
    public void Problems_add_the_Claude_version_line()
    {
        Assert.Equal([.. _created.Problems, "Claude Code 2.1.3: OrchDash was made for 2.1.285"], _run.Problems);
    }

    [Fact]
    public void Sessions_are_in_snapshot_order_with_their_keys()
    {
        Assert.Equal("gamma/20261003-120005/attempt-1-worker.json", SampleRun.GammaWorker1Key);
        Assert.Equal("gamma/20261003-120005/attempt-2-worker.json", SampleRun.GammaWorker2Key);
        Assert.Equal(
        [
            (SampleRun.AlphaWorkerKey, Provider.Claude, SessionState.Succeeded),
            (SampleRun.GammaWorker1Key, Provider.Claude, SessionState.Failed),
            (SampleRun.AlphaReviewKey, Provider.Copilot, SessionState.Succeeded),
            (SampleRun.BetaWorkerKey, Provider.Claude, SessionState.Running),
            (SampleRun.GammaWorker2Key, Provider.Claude, SessionState.Failed),
        ],
        _run.Sessions.Select(s => (s.Files.Key, s.Provider, s.State)));

        var ordered = _run.Sessions
            .OrderBy(s => s.StartedAt.HasValue ? 0 : 1)
            .ThenBy(s => s.StartedAt)
            .ThenBy(s => s.Files.Key, StringComparer.Ordinal);
        Assert.Equal(ordered, _run.Sessions);
    }

    [Fact]
    public void The_sessions_of_Create_differ_only_in_the_enriched_members()
    {
        var enriched = new[] { AlphaWorker, AlphaReview, BetaWorker };
        Assert.All(_created.Sessions.Zip(enriched), pair =>
        {
            var (created, session) = pair;
            Assert.Equal(created.Files, session.Files);
            Assert.Equal(created.Provider, session.Provider);
            Assert.Equal(created.State, session.State);
            Assert.Equal(created.Prompt, session.Prompt);
            Assert.Equal(created.StartedAt, session.StartedAt);
        });

        Assert.Equivalent(
            _created.Sessions[0].Content with { Calls = [] },
            AlphaWorker.Content with { Calls = [], RateLimit = null },
            strict: true);
        Assert.Equivalent(
            _created.Sessions[1].Content with { Calls = [] },
            AlphaReview.Content with { Calls = [], SentPrompt = null, Checkpoint = null },
            strict: true);
        Assert.Empty(AlphaWorker.Unavailable);
        Assert.Empty(AlphaReview.Unavailable);
    }

    [Fact]
    public void Alpha_worker_calls_get_exact_output_thinking_and_stop_reason()
    {
        var created = _created.Sessions[0].Content.Calls;
        var calls = AlphaWorker.Content.Calls;

        Assert.Equal(
            created.Select(c => (c.Id, c.Model, c.StartedAt, c.Usage!.Input, c.Usage.CacheRead, c.Usage.CacheWrite)),
            calls.Select(c => (c.Id, c.Model, c.StartedAt, c.Usage!.Input, c.Usage.CacheRead, c.Usage.CacheWrite)));
        Assert.Equal(
        [
            ("msg_01A7alpha", 19_200L, (long?)640, (long?)210, "tool_use"),
            ("msg_02B8alpha", 43_300L, (long?)2_460, (long?)1_180, "end_turn"),
        ],
        calls.Select(c => (c.Id, c.Usage!.Context, c.Usage.Output, c.ThinkingTokens, c.StopReason)));
        Assert.All(calls, c =>
        {
            Assert.Null(c.NanoAiu);
            Assert.Null(c.Duration);
        });
    }

    [Fact]
    public void Alpha_worker_has_a_rate_limit()
    {
        var limit = AlphaWorker.Content.RateLimit;

        Assert.NotNull(limit);
        Assert.Equal("allowed", limit.Status);
        Assert.Equal("five_hour", limit.LimitType);
        Assert.Equal(0.2, limit.FiveHourUsed);
        Assert.Equal(SampleRun.At(17, 0, 0), limit.FiveHourResetsAt);
        Assert.Equal(0.27, limit.SevenDayUsed);
        Assert.Equal(SampleRun.At(9, 0, 0).AddDays(4), limit.SevenDayResetsAt);
        Assert.Equal(SampleRun.At(12, 0, 11), limit.SeenAt);
    }

    [Fact]
    public void Alpha_worker_has_transcript_stores()
    {
        var stores = AlphaWorker.Stores;

        Assert.Equal("2.1.3", stores.CliVersion);
        Assert.Equal(2, stores.SystemPrompt.Length);
        Assert.All(stores.SystemPrompt, b => Assert.NotEmpty(b));
        Assert.Equal(["Read", "Bash"], stores.Tools.Select(t => t.Name));
        Assert.All(stores.Tools, t =>
        {
            Assert.False(string.IsNullOrEmpty(t.Description));
            Assert.NotNull(t.SchemaJson);
            using var schema = JsonDocument.Parse(t.SchemaJson);
            Assert.Equal(JsonValueKind.Object, schema.RootElement.ValueKind);
            Assert.Contains("\n  \"", t.SchemaJson.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        });
        Assert.Equal(0.25, stores.CostUsd);
        Assert.Equal(12, stores.LinesAdded);
        Assert.Equal(3, stores.LinesRemoved);
        Assert.Equal(0, stores.UnparsedLines);
    }

    [Fact]
    public void Alpha_worker_has_two_injected_items_before_the_first_call_and_one_between_the_calls()
    {
        var injected = AlphaWorker.Stores.Injected;
        var calls = AlphaWorker.Content.Calls;

        Assert.Equal(
        [
            ("skill_listing", SampleRun.At(12, 0, 8)),
            ("nested_memory", SampleRun.At(12, 0, 9)),
            ("total_tokens_reminder", SampleRun.At(12, 1, 30)),
        ],
        injected.Select(i => (i.Kind, i.Time!.Value)));
        Assert.All(injected, i =>
        {
            Assert.Equal("system", i.Role);
            Assert.NotEmpty(i.Text);
        });
        Assert.All(injected.Take(2), i => Assert.True(i.Time < calls[0].StartedAt));
        Assert.True(injected[2].Time > calls[0].StartedAt);
        Assert.True(injected[2].Time < calls[1].StartedAt);
    }

    [Fact]
    public void Claude_store_calls_match_the_calls_by_id()
    {
        var calls = AlphaWorker.Content.Calls;

        Assert.Equal(
            calls.Select(c => new CallFigures(c.Id, c.StartedAt, c.Usage!, c.ThinkingTokens, null, null, c.StopReason)),
            AlphaWorker.Stores.Calls);
    }

    [Fact]
    public void Alpha_review_calls_get_every_figure()
    {
        Assert.Equal(
        [
            ("0", new TokenUsage(20, 0, 14_180, 310), (long?)96, (long?)1_850_000_000, (TimeSpan?)TimeSpan.FromMilliseconds(4_200), "tool_calls"),
            ("1", new TokenUsage(10, 14_180, 2_760, 905), (long?)412, (long?)2_140_000_000, (TimeSpan?)TimeSpan.FromMilliseconds(11_800), "stop"),
        ],
        AlphaReview.Content.Calls.Select(c => (c.Id, c.Usage!, c.ThinkingTokens, c.NanoAiu, c.Duration, c.StopReason)));
        Assert.Equal([14_200L, 16_950L], AlphaReview.Content.Calls.Select(c => c.Usage!.Context));
        Assert.Equal(
            _created.Sessions[1].Content.Calls.Select(c => (c.Id, c.Model, c.StartedAt)),
            AlphaReview.Content.Calls.Select(c => (c.Id, c.Model, c.StartedAt)));
    }

    [Fact]
    public void Copilot_store_calls_match_the_calls_by_position()
    {
        var calls = AlphaReview.Content.Calls;

        Assert.Equal(
            calls.Select(c => new CallFigures(null, c.StartedAt, c.Usage!, c.ThinkingTokens, c.NanoAiu, c.Duration, c.StopReason)),
            AlphaReview.Stores.Calls);
    }

    [Fact]
    public void Alpha_review_has_session_folder_stores()
    {
        var stores = AlphaReview.Stores;

        Assert.Equal(TestedVersions.CopilotCli, stores.CliVersion);
        Assert.Equal(2, stores.SystemPrompt.Length);
        Assert.All(stores.SystemPrompt, b => Assert.NotEmpty(b));
        Assert.Empty(stores.Tools);
        Assert.Empty(stores.Injected);
        Assert.Null(stores.CostUsd);
        Assert.Null(stores.LinesAdded);
        Assert.Null(stores.LinesRemoved);
    }

    [Fact]
    public void Alpha_review_has_the_sent_prompt_and_a_checkpoint()
    {
        var content = AlphaReview.Content;

        Assert.Equal(
            "<current_datetime>2026-10-03T12:06:25+02:00</current_datetime>\n\n" + AlphaReview.Prompt,
            content.SentPrompt);

        var checkpoint = content.Checkpoint;
        Assert.NotNull(checkpoint);
        Assert.Equal(16_950, checkpoint.PromptTokens);
        Assert.Equal(content.Calls[1].Usage!.Context, checkpoint.PromptTokens);
        Assert.Equal(680, checkpoint.ToolTokens);
        Assert.Equal(["view", "powershell"], checkpoint.ToolNames);
        Assert.Equal(
            [new TokenPart("identity", 310), new TokenPart("tone_and_style", 227), new TokenPart("tool_instructions", 1_540)],
            checkpoint.SystemSegments);
        Assert.True(checkpoint.SystemSegments.Sum(s => s.Tokens) + checkpoint.ToolTokens < content.Calls[0].Usage!.Context / 2);
        Assert.Equal(3_990_000_000, checkpoint.NanoAiu);
        Assert.Equal(content.Calls.Sum(c => c.NanoAiu), checkpoint.NanoAiu);
        Assert.Equal(1, checkpoint.PremiumRequests);
    }

    [Fact]
    public void Beta_worker_has_no_transcript()
    {
        Assert.Equal(["no transcript"], BetaWorker.Unavailable);
        Assert.Same(StoreData.Empty, BetaWorker.Stores);
        Assert.Equivalent(_created.Sessions[2].Content, BetaWorker.Content, strict: true);
    }

    [Fact]
    public void Gamma_workers_are_two_failed_attempts_of_one_Claude_session()
    {
        Assert.All(new[] { (Gamma1, 1), (Gamma2, 2) }, pair =>
        {
            var (session, attempt) = pair;
            var files = session.Files;
            Assert.Equal("gamma", files.TaskId);
            Assert.Equal(AgentRole.Worker, files.Role);
            Assert.Equal("20261003-120005", files.StartFolder);
            Assert.Equal(attempt, files.Attempt);
            Assert.Equal(0, files.ReviewTry);
            Assert.False(files.IsNudge);
            var resultPath = SampleRun.LogsDir + files.Key.Replace('/', '\\');
            Assert.Equal(resultPath, files.ResultPath);
            Assert.Equal(resultPath + ".prompt.md", files.PromptPath);
            Assert.Equal(resultPath + ".events.jsonl", files.EventsPath);
            Assert.Equal(resultPath + ".stderr", files.StderrPath);
            Assert.True(files.HasResultFile);
            Assert.True(files.HasEventsFile);

            Assert.Equal(Provider.Claude, session.Provider);
            Assert.Equal(SessionState.Failed, session.State);
            Assert.NotEmpty(session.Prompt);
            Assert.Empty(session.Unavailable);

            var content = session.Content;
            Assert.Equal(_run.Tasks.Single(t => t.Id == "gamma").SessionId, content.SessionId);
            Assert.Equal("6e2d9a14-0c3b-4f57-8a2e-91b4c7d05f36", content.SessionId);
            Assert.Equal("claude-opus-4-5", content.Model);
            Assert.Equal("2.1.3", content.Init!.CliVersion);
            Assert.Equal(2, content.Calls.Length);
            Assert.All(content.Calls, c =>
            {
                Assert.NotNull(c.StartedAt);
                Assert.NotNull(c.Usage);
                Assert.Contains(content.Items, i => i.CallId == c.Id);
            });
            Assert.All(content.Items, i => Assert.Contains(content.Calls, c => c.Id == i.CallId));

            var result = content.Result;
            Assert.NotNull(result);
            Assert.True(result.IsError);
            Assert.NotNull(result.CostUsd);
            Assert.Equal(200_000, result.ContextWindow);
        });

        Assert.Equal([0.29, 0.33], new[] { Gamma1, Gamma2 }.Select(s => s.Content.Result!.CostUsd!.Value));
        Assert.Equal(_run.Tasks.Single(t => t.Id == "gamma").CostUsd, Gamma1.Content.Result!.CostUsd + Gamma2.Content.Result!.CostUsd);
    }

    [Fact]
    public void Gamma_calls_grow_across_the_attempts()
    {
        Assert.Equal(
        [
            ("msg_01E1gamma", 23_004L, (long?)420),
            ("msg_02F2gamma", 26_502L, (long?)1_850),
        ],
        Gamma1.Content.Calls.Select(c => (c.Id, c.Usage!.Context, c.Usage.Output)));
        Assert.Equal(
        [
            ("msg_03G3gamma", 30_703L, (long?)610),
            ("msg_04H4gamma", 33_501L, (long?)2_240),
        ],
        Gamma2.Content.Calls.Select(c => (c.Id, c.Usage!.Context, c.Usage.Output)));
        Assert.True(Gamma2.Content.Calls[0].Usage!.Context > Gamma1.Content.Calls[^1].Usage!.Context);
    }

    [Fact]
    public void Gamma_times_are_within_the_task_and_attempt_2_follows_attempt_1()
    {
        var times1 = Times(Gamma1);
        var times2 = Times(Gamma2);

        Assert.All(times1.Concat(times2), t => Assert.InRange(t, SampleRun.At(12, 0, 20), SampleRun.At(12, 20, 0)));
        Assert.Equal(Gamma1.Content.LastEventAt, times1.Max());
        Assert.True(times2.Min() > times1.Max());
    }

    [Fact]
    public void Gamma_workers_share_one_stores_instance()
    {
        var stores = Gamma1.Stores;

        Assert.Same(stores, Gamma2.Stores);
        Assert.Equal("2.1.3", stores.CliVersion);
        Assert.Single(stores.SystemPrompt);
        var tool = Assert.Single(stores.Tools);
        Assert.False(string.IsNullOrEmpty(tool.Description));
        Assert.NotNull(tool.SchemaJson);
        Assert.Empty(stores.Injected);
        Assert.Empty(stores.Calls);
        Assert.Null(stores.CostUsd);
        Assert.Null(stores.LinesAdded);
        Assert.Null(stores.LinesRemoved);
        Assert.Equal(0, stores.UnparsedLines);
    }

    [Fact]
    public void Context_limits_stay_per_model()
    {
        Assert.Equal(200_000, ContextLimit.For(_run, AlphaWorker));
        Assert.Equal(200_000, ContextLimit.For(_run, BetaWorker));
        Assert.Equal(200_000, ContextLimit.For(_run, Gamma1));
        Assert.Null(ContextLimit.For(_run, AlphaReview));
        Assert.NotEqual(AlphaWorker.Content.Model, Gamma1.Content.Model);
    }

    private static List<DateTimeOffset> Times(Session session)
    {
        var content = session.Content;
        var times = new List<DateTimeOffset?> { session.Files.PromptWrittenAt, session.StartedAt, content.FirstEventAt, content.LastEventAt };
        times.AddRange(content.Calls.Select(c => c.StartedAt));
        times.AddRange(content.Items.Select(i => i.Time));
        times.AddRange(content.Items.OfType<ToolCall>().Select(t => t.Result?.Time));
        Assert.All(times, t => Assert.NotNull(t));
        return [.. times.Select(t => t!.Value)];
    }
}
