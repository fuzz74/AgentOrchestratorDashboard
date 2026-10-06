using OrchDash.Core.Model;
using OrchDash.Core.Store;
using Xunit;
using static OrchDash.Core.Tests.Store.RunData;

namespace OrchDash.Core.Tests.Store;

// Spec 31.2: the work dirs of an archived run are those of the repo the archive belongs to.
public sealed class RunStoreWorkDirTests : IDisposable
{
    private readonly TempRepo _repo = TempRepo.Archive("20261006-120000");
    private readonly FakeParserFactory _factory = new();

    public void Dispose() => _repo.Dispose();

    [Fact]
    public void An_archived_run_uses_the_work_dirs_of_its_repo()
    {
        var worker = _repo.Session("alpha/20261006-120000/attempt-1-worker.json", "alpha");
        var bootstrap = _repo.Session("bootstrap-20261006-110028/attempt-1.json", role: AgentRole.Bootstrap);
        foreach (var session in new[] { worker, bootstrap })
            _repo.Append(session, ClaudeLine("one") + "\n");
        var reader = new FakeRunFolderReader(() => Data(sessions: [worker, bootstrap]));
        using var store = new RunStore(_repo.RepoPath, reader, _factory.Create);

        store.Poll();

        Assert.Equal(Path.Combine(_repo.Root, "Repo.worktrees", "alpha"), WorkDirOf(store, worker));
        Assert.Equal(Path.Combine(_repo.Root, "Repo"), WorkDirOf(store, bootstrap));
        Assert.Equal(
            [(Provider.Claude, Path.Combine(_repo.Root, "Repo")), (Provider.Claude, Path.Combine(_repo.Root, "Repo.worktrees", "alpha"))],
            _factory.Calls.OrderBy(c => c.WorkDir, StringComparer.Ordinal));
    }

    // The fake parser reports its workDir as SessionInit.Cwd.
    private static string? WorkDirOf(RunStore store, SessionFiles files) =>
        store.Current.Sessions.Single(s => s.Files.Key == files.Key).Content.Init?.Cwd;
}
