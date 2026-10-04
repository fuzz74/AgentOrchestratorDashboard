using System.Diagnostics;
using OrchDash.Core.Model;
using OrchDash.Core.Store;
using Xunit;
using static OrchDash.Core.Tests.Store.RunData;

namespace OrchDash.Core.Tests.Store;

// Spec 5.8 and N.2: the background thread follows the files until the store is disposed.
public sealed class RunStoreThreadTests : IDisposable
{
    private readonly TempRepo _repo = new();
    private readonly FakeParserFactory _factory = new();
    private readonly SessionFiles _session;
    private readonly FakeRunFolderReader _reader;

    public RunStoreThreadTests()
    {
        _session = _repo.Session("alpha/20261003-120000/attempt-1-worker.json", "alpha");
        _reader = new FakeRunFolderReader(() => Data(sessions: [_session]));
    }

    public void Dispose() => _repo.Dispose();

    [Fact]
    public async Task An_appended_line_is_published_within_two_seconds()
    {
        _repo.Append(_session, ClaudeLine("one") + "\n");
        using var store = new RunStore(_repo.RepoPath, _reader, _factory.Create);

        store.Start();
        Assert.Equal(1, store.Current.Version);
        Assert.Equal([ClaudeLine("one")], Texts(Assert.Single(store.Current.Sessions)));

        _repo.Append(_session, ClaudeLine("two") + "\n");
        var watch = Stopwatch.StartNew();
        while (Texts(Assert.Single(store.Current.Sessions)).Length < 2 && watch.Elapsed < TimeSpan.FromSeconds(3))
            await Task.Delay(20, TestContext.Current.CancellationToken);
        watch.Stop();

        Assert.Equal([ClaudeLine("one"), ClaudeLine("two")], Texts(Assert.Single(store.Current.Sessions)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task Nothing_changes_after_dispose()
    {
        _repo.Append(_session, ClaudeLine("one") + "\n");
        var store = new RunStore(_repo.RepoPath, _reader, _factory.Create, pollInterval: TimeSpan.FromMilliseconds(50));
        store.Start();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        store.Dispose();
        var last = store.Current;
        var reads = _reader.Reads;
        _repo.Append(_session, ClaudeLine("two") + "\n");
        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.Same(last, store.Current);
        Assert.Equal(reads, _reader.Reads);
        store.Dispose();
    }

    [Fact]
    public void Dispose_stops_the_thread_without_waiting_for_the_interval()
    {
        var store = new RunStore(_repo.RepoPath, _reader, _factory.Create, pollInterval: TimeSpan.FromMinutes(5));
        store.Start();

        var watch = Stopwatch.StartNew();
        store.Dispose();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
        Assert.Equal(1, _reader.Reads);
    }

    [Fact]
    public void Poll_and_Start_after_dispose_throw()
    {
        var store = new RunStore(_repo.RepoPath, _reader, _factory.Create);
        store.Dispose();

        Assert.Throws<ObjectDisposedException>(store.Poll);
        Assert.Throws<ObjectDisposedException>(store.Start);
    }

    [Fact]
    public void Start_twice_throws()
    {
        using var store = new RunStore(_repo.RepoPath, _reader, _factory.Create);
        store.Start();

        Assert.Throws<InvalidOperationException>(store.Start);
    }
}
