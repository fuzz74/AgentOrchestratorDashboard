using OrchDash.Core.Model;
using OrchDash.Core.Store;
using Xunit;
using static OrchDash.Core.Tests.Store.StoreData;

namespace OrchDash.Core.Tests.Store;

// Spec 5.5: session state, prompt and start time.
public sealed class RunStoreSessionTests : IDisposable
{
    private const string OkResult = """{"session_id":"s1","result":"ok"}""";
    private const string ErrorResult = """{"session_id":"s1","result":"error"}""";

    private readonly TempRepo _repo = new();
    private readonly FakeParserFactory _factory = new();

    public void Dispose() => _repo.Dispose();

    private RunSnapshot PollOnce(RunPhase phase, params SessionFiles[] sessions)
    {
        var reader = new FakeRunFolderReader(() => Data(run: Run(phase), sessions: sessions));
        using var store = new RunStore(_repo.RepoPath, reader, _factory.Create);
        store.Poll();
        return store.Current;
    }

    private static Session Find(RunSnapshot snapshot, SessionFiles files) =>
        snapshot.Sessions.Single(s => s.Files.Key == files.Key);

    private SessionFiles Worker(string task, int minute, bool hasResultFile = false) =>
        _repo.Session($"{task}/20261003-120000/attempt-{minute}-worker.json", task, hasResultFile: hasResultFile,
            promptWrittenAt: At(12, minute, 0));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_result_that_is_not_an_error_is_succeeded(bool hasResultFile)
    {
        var session = Worker("alpha", 1, hasResultFile);
        _repo.Append(session, OkResult + "\n");

        Assert.Equal(SessionState.Succeeded, Find(PollOnce(RunPhase.Running, session), session).State);
    }

    [Fact]
    public void An_error_result_is_failed()
    {
        var session = Worker("alpha", 1, hasResultFile: true);
        _repo.Append(session, ErrorResult + "\n");

        Assert.Equal(SessionState.Failed, Find(PollOnce(RunPhase.Running, session), session).State);
    }

    [Fact]
    public void A_result_file_without_a_result_is_failed()
    {
        var session = Worker("alpha", 1, hasResultFile: true);
        _repo.Append(session, ClaudeLine("working") + "\n");

        Assert.Equal(SessionState.Failed, Find(PollOnce(RunPhase.Running, session), session).State);
    }

    [Theory]
    [InlineData(RunPhase.Planning)]
    [InlineData(RunPhase.Running)]
    [InlineData(RunPhase.Stopping)]
    public void An_unfinished_session_of_a_live_run_is_running(RunPhase phase)
    {
        var session = Worker("alpha", 1);
        _repo.Append(session, ClaudeLine("working") + "\n");

        Assert.Equal(SessionState.Running, Find(PollOnce(phase, session), session).State);
    }

    [Theory]
    [InlineData(RunPhase.NotStarted)]
    [InlineData(RunPhase.Finished)]
    [InlineData(RunPhase.Interrupted)]
    public void An_unfinished_session_of_a_run_that_is_not_live_is_aborted(RunPhase phase)
    {
        var session = Worker("alpha", 1);

        Assert.Equal(SessionState.Aborted, Find(PollOnce(phase, session), session).State);
    }

    [Fact]
    public void A_later_session_of_the_same_task_aborts_an_unfinished_one()
    {
        var earlier = Worker("alpha", 1);
        var later = Worker("alpha", 2);
        var otherTask = Worker("beta", 3);

        var snapshot = PollOnce(RunPhase.Running, earlier, later, otherTask);

        Assert.Equal(SessionState.Aborted, Find(snapshot, earlier).State);
        Assert.Equal(SessionState.Running, Find(snapshot, later).State);
        Assert.Equal(SessionState.Running, Find(snapshot, otherTask).State);
    }

    [Fact]
    public void Bootstrap_and_planner_are_aborted_by_any_later_session()
    {
        var bootstrap = _repo.Session("bootstrap-20261003-110028/attempt-1.json", role: AgentRole.Bootstrap, promptWrittenAt: At(11, 0, 28));
        var planner = _repo.Session("planner-20261003-110111-1.json", role: AgentRole.Planner, promptWrittenAt: At(11, 1, 11));

        Assert.Equal(SessionState.Aborted, Find(PollOnce(RunPhase.Planning, bootstrap, planner), bootstrap).State);
        Assert.Equal(SessionState.Running, Find(PollOnce(RunPhase.Planning, bootstrap, planner), planner).State);

        var worker = Worker("alpha", 5);
        Assert.Equal(SessionState.Aborted, Find(PollOnce(RunPhase.Running, planner, worker), planner).State);
    }

    [Fact]
    public void The_prompt_is_the_text_of_the_prompt_file()
    {
        var session = Worker("alpha", 1);
        _repo.WritePrompt(session, "Build alpha.\nLine two æøå.");

        Assert.Equal("Build alpha.\nLine two æøå.", Find(PollOnce(RunPhase.Running, session), session).Prompt);
    }

    [Fact]
    public void The_prompt_is_empty_without_a_prompt_file()
    {
        var session = Worker("alpha", 1);

        Assert.Equal("", Find(PollOnce(RunPhase.Running, session), session).Prompt);
    }

    [Fact]
    public void StartedAt_is_the_first_event_time_when_there_is_one()
    {
        var session = Worker("alpha", 1);
        _repo.Append(session, ClaudeLine("no time") + "\n" + ClaudeLine("first", At(12, 1, 30)) + "\n" + ClaudeLine("second", At(12, 2, 0)) + "\n");

        Assert.Equal(At(12, 1, 30), Find(PollOnce(RunPhase.Running, session), session).StartedAt);
    }

    [Fact]
    public void StartedAt_is_the_prompt_write_time_without_an_event_time()
    {
        var session = Worker("alpha", 1);
        _repo.Append(session, ClaudeLine("no time") + "\n");
        var withoutPrompt = _repo.Session("beta/20261003-120000/attempt-1-worker.json", "beta");

        var snapshot = PollOnce(RunPhase.Running, session, withoutPrompt);

        Assert.Equal(At(12, 1, 0), Find(snapshot, session).StartedAt);
        Assert.Null(Find(snapshot, withoutPrompt).StartedAt);
    }
}
