using OrchDash.Core.Model;
using OrchDash.Core.Store;
using Xunit;
using static OrchDash.Core.Tests.Store.StoreData;

namespace OrchDash.Core.Tests.Store;

// Spec 5.2: a new Version only when the content differs, compared element by element.
public sealed class RunStoreVersionTests : IDisposable
{
    private readonly TempRepo _repo = new();
    private readonly FakeParserFactory _factory = new();
    private readonly SessionFiles _session;

    public RunStoreVersionTests()
    {
        _session = _repo.Session("alpha/20261003-120000/attempt-1-worker.json", "alpha", promptWrittenAt: At(12, 0, 5));
        _repo.WritePrompt(_session, "Build alpha.");
        _repo.Append(_session, ClaudeLine("hello", At(12, 0, 10)) + "\n"
            + """{"session_id":"s1","result":"ok","ts":"2026-10-03T12:01:00+00:00"}""" + "\n");
    }

    public void Dispose() => _repo.Dispose();

    // The same content on every call, in new arrays; `change` alters one part.
    private RunFolderData Sample(string? change = null) => Data(
        run: change == "run" ? Run(maxParallel: 3) : Run(),
        plan: change == "plan" ? Plan(model: "\"opus\"") : Plan(),
        tasks:
        [
            change == "task-owns" ? TaskOf("alpha", owns: ["src/other/**"])
                : change == "task-status" ? TaskOf("alpha", status: TaskState.Running)
                : TaskOf("alpha"),
            TaskOf("beta", 2, deps: change == "task-deps" ? ["alpha", "gamma"] : ["alpha"]),
        ],
        progress: change == "progress" ? [Progress("Run started"), Progress("Run finished", 5)] : [Progress("Run started")],
        sessions: [change == "session" ? _session with { HasResultFile = true } : _session with { }],
        problems: [change == "problems" ? "progress.md: in use" : "state.json: in use"]);

    [Fact]
    public void Equal_content_in_new_arrays_keeps_the_version()
    {
        var reader = new FakeRunFolderReader(() => Sample());
        using var store = new RunStore(_repo.RepoPath, reader, _factory.Create);
        store.Poll();
        var first = store.Current;

        store.Poll();
        store.Poll();

        Assert.Same(first, store.Current);
        Assert.Equal(1, store.Current.Version);
    }

    [Fact]
    public void A_new_build_with_equal_content_keeps_the_version()
    {
        var reader = new FakeRunFolderReader(() => Sample());
        using var store = new RunStore(_repo.RepoPath, reader, _factory.Create);
        store.Poll();
        var parser = Assert.Single(_factory.Parsers);
        var builds = parser.BuildCount;

        _repo.Append(_session, "\n");   // the fake parser reports nothing for an empty line
        store.Poll();

        Assert.Equal(builds + 1, parser.BuildCount);
        Assert.Equal(1, store.Current.Version);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("plan")]
    [InlineData("task-deps")]
    [InlineData("task-owns")]
    [InlineData("task-status")]
    [InlineData("progress")]
    [InlineData("problems")]
    [InlineData("session")]
    public void A_change_raises_the_version_by_one(string change)
    {
        var reader = new FakeRunFolderReader(() => Sample());
        using var store = new RunStore(_repo.RepoPath, reader, _factory.Create);
        store.Poll();
        store.Poll();

        reader.Next = () => Sample(change);
        store.Poll();
        store.Poll();

        Assert.Equal(2, store.Current.Version);
    }

    [Fact]
    public void A_new_line_in_a_session_raises_the_version_by_one()
    {
        var reader = new FakeRunFolderReader(() => Sample());
        using var store = new RunStore(_repo.RepoPath, reader, _factory.Create);
        store.Poll();

        _repo.Append(_session, ClaudeLine("more") + "\n");
        store.Poll();
        store.Poll();

        Assert.Equal(2, store.Current.Version);
        Assert.Contains(ClaudeLine("more"), Texts(Assert.Single(store.Current.Sessions)));
    }

    [Fact]
    public void A_changed_prompt_raises_the_version_by_one()
    {
        var reader = new FakeRunFolderReader(() => Sample());
        using var store = new RunStore(_repo.RepoPath, reader, _factory.Create);
        store.Poll();

        _repo.WritePrompt(_session, "Build alpha, and test it.");
        store.Poll();

        Assert.Equal(2, store.Current.Version);
        Assert.Equal("Build alpha, and test it.", Assert.Single(store.Current.Sessions).Prompt);
    }
}
