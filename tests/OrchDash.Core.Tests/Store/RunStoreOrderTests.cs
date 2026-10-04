using OrchDash.Core.Model;
using OrchDash.Core.Store;
using Xunit;
using static OrchDash.Core.Tests.Store.RunData;

namespace OrchDash.Core.Tests.Store;

// Spec 5.9: the snapshot order does not depend on the reader's order.
public sealed class RunStoreOrderTests : IDisposable
{
    private readonly TempRepo _repo = new();

    public void Dispose() => _repo.Dispose();

    [Fact]
    public void Tasks_are_ordered_by_wave_then_id_ordinal()
    {
        var reader = new FakeRunFolderReader(() => Data(plan: Plan(),
            tasks: [TaskOf("b", 2), TaskOf("a", 2), TaskOf("z", 1), TaskOf("B", 2), TaskOf("c", 3)]));
        using var store = new RunStore(_repo.RepoPath, reader, new FakeParserFactory().Create);

        store.Poll();

        Assert.Equal(["z", "B", "a", "b", "c"], store.Current.Tasks.Select(t => t.Id));
    }

    [Fact]
    public void Sessions_are_ordered_by_start_with_null_last_then_key_ordinal()
    {
        var noStart = _repo.Session("a/20261003-120000/attempt-9-worker.json", "a");
        var late = _repo.Session("a/20261003-120000/attempt-3-worker.json", "a", promptWrittenAt: At(12, 30, 0));
        var earlyB = _repo.Session("b/20261003-120000/attempt-1-worker.json", "b", promptWrittenAt: At(12, 10, 0));
        var earlyA = _repo.Session("a/20261003-120000/attempt-1-worker.json", "a", promptWrittenAt: At(12, 10, 0));
        var earliestOtherOffset = _repo.Session("c/20261003-120000/attempt-1-worker.json", "c",
            promptWrittenAt: new DateTimeOffset(2026, 10, 3, 13, 0, 0, TimeSpan.FromHours(2)));   // 11:00 UTC
        var reader = new FakeRunFolderReader(() => Data(sessions: [noStart, late, earlyB, earlyA, earliestOtherOffset]));
        using var store = new RunStore(_repo.RepoPath, reader, new FakeParserFactory().Create);

        store.Poll();

        Assert.Equal([earliestOtherOffset.Key, earlyA.Key, earlyB.Key, late.Key, noStart.Key],
            store.Current.Sessions.Select(s => s.Files.Key));
    }

    [Fact]
    public void Progress_keeps_the_reader_order()
    {
        var reader = new FakeRunFolderReader(() => Data(progress: [Progress("later", 5), Progress("earlier", 1)]));
        using var store = new RunStore(_repo.RepoPath, reader, new FakeParserFactory().Create);

        store.Poll();

        Assert.Equal(["later", "earlier"], store.Current.Progress.Select(p => p.Message));
    }
}
