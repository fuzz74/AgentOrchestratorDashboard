using OrchDash.Core.Model;
using OrchDash.Core.Store;
using Xunit;
using static OrchDash.Core.Tests.Store.StoreData;

namespace OrchDash.Core.Tests.Store;

// Spec 5.6 (a lost plan is kept) and 5.7 (exceptions become problem lines).
public sealed class RunStoreProblemTests : IDisposable
{
    private readonly TempRepo _repo = new();
    private readonly FakeParserFactory _factory = new();

    public void Dispose() => _repo.Dispose();

    [Fact]
    public void A_poll_without_a_plan_keeps_the_current_plan_and_tasks()
    {
        var reader = new FakeRunFolderReader(() => Data(plan: Plan(), tasks: [TaskOf("alpha")]));
        using var store = new RunStore(_repo.RepoPath, reader, _factory.Create);
        store.Poll();
        var before = store.Current;

        reader.Next = () => Data(problems: ["tasks.json: unexpected end of data"]);
        store.Poll();

        var after = store.Current;
        Assert.Equal(2, after.Version);
        Assert.Same(before.Plan, after.Plan);
        Assert.Equal(before.Tasks, after.Tasks);
        Assert.Equal(2, after.Problems.Length);
        Assert.Equal("tasks.json: unexpected end of data", after.Problems[0]);
        Assert.Contains("plan", after.Problems[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_current_plan_no_plan_is_not_a_problem()
    {
        var reader = new FakeRunFolderReader(() => Data());
        using var store = new RunStore(_repo.RepoPath, reader, _factory.Create);

        store.Poll();

        Assert.Null(store.Current.Plan);
        Assert.Empty(store.Current.Problems);
    }

    [Fact]
    public void A_reader_that_throws_once_gives_a_problem_and_the_next_poll_works()
    {
        var reader = new FakeRunFolderReader(() => Data(plan: Plan(), tasks: [TaskOf("alpha")], problems: ["state.json: in use"]));
        using var store = new RunStore(_repo.RepoPath, reader, _factory.Create);
        store.Poll();
        var before = store.Current;

        reader.ThrowOnce(new IOException("disk on fire"));
        store.Poll();

        var failed = store.Current;
        Assert.Equal(2, failed.Version);
        Assert.Equal(["state.json: in use", "disk on fire"], failed.Problems);
        Assert.Same(before.Plan, failed.Plan);
        Assert.Equal(before.Tasks, failed.Tasks);

        store.Poll();

        Assert.Equal(3, store.Current.Version);
        Assert.Equal(["state.json: in use"], store.Current.Problems);
    }

    [Fact]
    public void A_failing_first_poll_publishes_version_1_with_the_problem()
    {
        var reader = new FakeRunFolderReader(() => Data(plan: Plan()));
        using var store = new RunStore(_repo.RepoPath, reader, _factory.Create);

        reader.ThrowOnce(new InvalidOperationException("reader broke"));
        store.Poll();

        Assert.Equal(1, store.Current.Version);
        Assert.Equal(["reader broke"], store.Current.Problems);

        store.Poll();

        Assert.Equal(2, store.Current.Version);
        Assert.Empty(store.Current.Problems);
        Assert.NotNull(store.Current.Plan);
    }

    [Fact]
    public void The_same_failure_twice_publishes_once()
    {
        var reader = new FakeRunFolderReader(() => Data());
        using var store = new RunStore(_repo.RepoPath, reader, _factory.Create);
        store.Poll();

        reader.ThrowOnce(new IOException("disk on fire"));
        store.Poll();
        reader.ThrowOnce(new IOException("disk on fire"));
        store.Poll();

        Assert.Equal(2, store.Current.Version);
        Assert.Equal(["disk on fire"], store.Current.Problems);
    }

    [Fact]
    public void A_parser_that_throws_gives_a_problem_and_the_session_is_read_again()
    {
        var session = _repo.Session("alpha/20261003-120000/attempt-1-worker.json", "alpha");
        var reader = new FakeRunFolderReader(() => Data(sessions: [session]));
        using var store = new RunStore(_repo.RepoPath, reader, _factory.Create);
        _repo.Append(session, Claude("one") + "\n");
        store.Poll();

        _factory.ThrowOnceOn(Claude("boom"));
        _repo.Append(session, Claude("boom") + "\n" + Claude("after") + "\n");
        store.Poll();

        Assert.Equal(2, store.Current.Version);
        Assert.Equal(["fake parser failed on " + Claude("boom")], store.Current.Problems);
        Assert.Equal([Claude("one")], Texts(Assert.Single(store.Current.Sessions)));

        store.Poll();

        Assert.Equal(3, store.Current.Version);
        Assert.Empty(store.Current.Problems);
        Assert.Equal(2, _factory.Parsers.Count);
        Assert.Equal([Claude("one"), Claude("boom"), Claude("after")], _factory.Parsers[1].Lines);
        Assert.Equal(_factory.Parsers[1].Lines, Texts(Assert.Single(store.Current.Sessions)));
    }
}
