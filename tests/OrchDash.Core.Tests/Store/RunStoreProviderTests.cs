using OrchDash.Core.Model;
using OrchDash.Core.Store;
using Xunit;
using static OrchDash.Core.Tests.Store.RunData;

namespace OrchDash.Core.Tests.Store;

// Spec 5.3 and 5.4: choosing the provider, replaying buffered lines, the workDir, and a shorter file.
public sealed class RunStoreProviderTests : IDisposable
{
    private readonly TempRepo _repo = new();
    private readonly FakeParserFactory _factory = new();

    public void Dispose() => _repo.Dispose();

    private RunStore NewStore(params SessionFiles[] sessions) =>
        new(_repo.RepoPath, new FakeRunFolderReader(() => Data(sessions: sessions)), _factory.Create);

    [Theory]
    [InlineData("""{"session_id":"abc","type":"system"}""", Provider.Claude)]
    [InlineData("""{"type":"session.start","data":{},"id":"1"}""", Provider.Copilot)]
    [InlineData("""{"type":"x","parentId":null}""", Provider.Copilot)]
    public void The_first_deciding_line_chooses_the_provider(string decidingLine, Provider expected)
    {
        var session = _repo.Session("alpha/20261003-120000/attempt-1-worker.json", "alpha");
        _repo.Append(session, decidingLine + "\n" + """{"session_id":"later"}""" + "\n");
        using var store = NewStore(session);

        store.Poll();

        Assert.Equal(expected, Assert.Single(store.Current.Sessions).Provider);
        Assert.Equal(expected, Assert.Single(_factory.Calls).Provider);
    }

    [Fact]
    public void Lines_before_the_deciding_line_are_buffered_and_replayed_in_order()
    {
        var session = _repo.Session("alpha/20261003-120000/attempt-1-worker.json", "alpha");
        string[] undecided = ["not json", "[1, 2]", """{"type":"banner"}""", """{"note":"session_id"}"""];
        _repo.Append(session, string.Join("\n", undecided) + "\n");
        using var store = NewStore(session);

        store.Poll();

        var unknown = Assert.Single(store.Current.Sessions);
        Assert.Equal(Provider.Unknown, unknown.Provider);
        Assert.Same(SessionContent.Empty, unknown.Content);
        Assert.Empty(_factory.Calls);

        _repo.Append(session, ClaudeLine("decides") + "\n");
        store.Poll();
        _repo.Append(session, ClaudeLine("after") + "\n");
        store.Poll();

        var parser = Assert.Single(_factory.Parsers);
        Assert.Equal([.. undecided, ClaudeLine("decides"), ClaudeLine("after")], parser.Lines);
        Assert.Equal(Provider.Claude, Assert.Single(store.Current.Sessions).Provider);
        Assert.Equal(parser.Lines, Texts(Assert.Single(store.Current.Sessions)));
    }

    [Fact]
    public void The_factory_is_never_called_for_an_unknown_provider()
    {
        var undecided = _repo.Session("planner-20261003-110111-1.json", role: AgentRole.Planner);
        var decided = _repo.Session("alpha/20261003-120000/attempt-1-worker.json", "alpha");
        _repo.Append(undecided, "plain text\n");
        _repo.Append(decided, ClaudeLine("one") + "\n");
        using var store = NewStore(undecided, decided);

        store.Poll();
        store.Poll();

        Assert.Equal([Provider.Claude], _factory.Calls.Select(c => c.Provider));
    }

    [Fact]
    public void The_work_dir_is_the_task_worktree_or_the_repo()
    {
        var worker = _repo.Session("alpha/20261003-120000/attempt-1-worker.json", "alpha");
        var bootstrap = _repo.Session("bootstrap-20261003-110028/attempt-1.json", role: AgentRole.Bootstrap);
        var planner = _repo.Session("planner-20261003-110111-1.json", role: AgentRole.Planner);
        foreach (var session in new[] { worker, bootstrap, planner })
            _repo.Append(session, ClaudeLine("one") + "\n");
        var reader = new FakeRunFolderReader(() => Data(sessions: [worker, bootstrap, planner]));
        using var store = new RunStore(_repo.RepoPath + Path.DirectorySeparatorChar, reader, _factory.Create);

        store.Poll();

        var repoPath = _repo.RepoPath + Path.DirectorySeparatorChar;
        Assert.Equal(Path.Combine(_repo.RepoPath + ".worktrees", "alpha"), WorkDirOf(store, worker));
        Assert.Equal(repoPath, WorkDirOf(store, bootstrap));
        Assert.Equal(repoPath, WorkDirOf(store, planner));
        Assert.Equal(3, _factory.Calls.Count);
    }

    [Fact]
    public void A_shorter_file_gives_a_new_parser_fed_from_the_start()
    {
        var session = _repo.Session("alpha/20261003-120000/attempt-1-worker.json", "alpha");
        _repo.Append(session, ClaudeLine("one") + "\n" + ClaudeLine("two") + "\n" + ClaudeLine("three") + "\n");
        using var store = NewStore(session);
        store.Poll();

        _repo.Overwrite(session, "{\"data\":{}}\n");
        store.Poll();

        Assert.Equal(2, _factory.Parsers.Count);
        var fresh = _factory.Parsers[1];
        Assert.Equal(Provider.Copilot, fresh.Provider);
        Assert.Equal(["{\"data\":{}}"], fresh.Lines);
        var current = Assert.Single(store.Current.Sessions);
        Assert.Equal(Provider.Copilot, current.Provider);
        Assert.Equal(["{\"data\":{}}"], Texts(current));
    }

    [Fact]
    public void A_shorter_file_without_a_deciding_line_is_unknown_again()
    {
        var session = _repo.Session("alpha/20261003-120000/attempt-1-worker.json", "alpha");
        _repo.Append(session, ClaudeLine("one") + "\n" + ClaudeLine("two") + "\n");
        using var store = NewStore(session);
        store.Poll();

        _repo.Overwrite(session, "x\n");
        store.Poll();

        var current = Assert.Single(store.Current.Sessions);
        Assert.Equal(Provider.Unknown, current.Provider);
        Assert.Same(SessionContent.Empty, current.Content);
    }

    // The fake parser reports its workDir as SessionInit.Cwd.
    private static string? WorkDirOf(RunStore store, SessionFiles files) =>
        store.Current.Sessions.Single(s => s.Files.Key == files.Key).Content.Init?.Cwd;
}
