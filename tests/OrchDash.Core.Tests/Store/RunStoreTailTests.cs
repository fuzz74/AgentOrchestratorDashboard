using System.Text;
using OrchDash.Core.Model;
using OrchDash.Core.Store;
using Xunit;
using static OrchDash.Core.Tests.Store.StoreData;

namespace OrchDash.Core.Tests.Store;

// Spec 5.1 and 5.2: the first poll reads everything, later polls only the appended complete lines.
public sealed class RunStoreTailTests : IDisposable
{
    private readonly TempRepo _repo = new();
    private readonly FakeParserFactory _factory = new();
    private readonly SessionFiles _session;
    private readonly FakeRunFolderReader _reader;

    public RunStoreTailTests()
    {
        _session = _repo.Session("alpha/20261003-120000/attempt-1-worker.json", "alpha");
        _reader = new FakeRunFolderReader(() => Data(sessions: [_session]));
    }

    public void Dispose() => _repo.Dispose();

    private RunStore NewStore(TimeProvider? time = null) => new(_repo.RepoPath, _reader, _factory.Create, time: time);

    [Fact]
    public void Current_before_the_first_poll_is_the_empty_snapshot()
    {
        using var store = NewStore();

        var current = store.Current;
        Assert.Equal(0, current.Version);
        Assert.Equal(_repo.RepoPath, current.RepoPath);
        Assert.Equal(RunPhase.NotStarted, current.Run.Phase);
        Assert.Null(current.Plan);
        Assert.Empty(current.Tasks);
        Assert.Empty(current.Sessions);
        Assert.Empty(current.Progress);
        Assert.Empty(current.Problems);
        Assert.Equal(0, _reader.Reads);
    }

    [Fact]
    public void First_poll_reads_the_whole_file_and_publishes_version_1()
    {
        var clock = new ManualClock(At(12, 30, 0));
        string[] lines = [ClaudeLine("one"), ClaudeLine("two"), ClaudeLine("three")];
        _repo.Append(_session, string.Join("\n", lines) + "\n");
        using var store = NewStore(clock);

        store.Poll();

        var current = store.Current;
        Assert.Equal(1, current.Version);
        Assert.Equal(clock.GetLocalNow(), current.ReadAt);
        Assert.Equal(Path.Combine(_repo.RepoPath, ".orchestrator"), _reader.LastRunDir);
        Assert.Equal(clock.GetLocalNow(), _reader.LastNow);
        Assert.Equal(lines, Texts(Assert.Single(current.Sessions)));
    }

    [Fact]
    public void Later_polls_pass_only_the_appended_lines()
    {
        _repo.Append(_session, ClaudeLine("one") + "\n" + ClaudeLine("two") + "\n");
        using var store = NewStore();
        store.Poll();

        _repo.Append(_session, ClaudeLine("three") + "\n");
        store.Poll();

        var parser = Assert.Single(_factory.Parsers);
        Assert.Equal([ClaudeLine("one"), ClaudeLine("two"), ClaudeLine("three")], parser.Lines);
        Assert.Equal(2, store.Current.Version);
        Assert.Equal(parser.Lines, Texts(Assert.Single(store.Current.Sessions)));
    }

    [Fact]
    public void A_line_without_its_newline_is_held_back_until_it_is_complete()
    {
        var complete = ClaudeLine("complete");
        var partial = ClaudeLine("partial");
        _repo.Append(_session, complete + "\n" + partial[..10]);
        using var store = NewStore();

        store.Poll();
        Assert.Equal([complete], Texts(Assert.Single(store.Current.Sessions)));

        _repo.Append(_session, partial[10..]);
        store.Poll();
        Assert.Equal([complete], Texts(Assert.Single(store.Current.Sessions)));
        Assert.Equal(1, store.Current.Version);

        _repo.Append(_session, "\n");
        store.Poll();
        Assert.Equal([complete, partial], Texts(Assert.Single(store.Current.Sessions)));
        Assert.Equal(2, store.Current.Version);
    }

    [Fact]
    public void Carriage_returns_and_the_byte_order_mark_are_removed()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        _repo.AppendBytes(_session, [.. bom, .. Encoding.UTF8.GetBytes(ClaudeLine("one") + "\r\n" + ClaudeLine("two") + "\r\n")]);
        using var store = NewStore();

        store.Poll();

        Assert.Equal([ClaudeLine("one"), ClaudeLine("two")], Assert.Single(_factory.Parsers).Lines);
    }

    [Fact]
    public void A_multi_byte_character_split_over_two_polls_survives()
    {
        var line = ClaudeLine("blåbærsyltetøy € 𝄞");
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        // ClaudeLine("blåbærsyltet") ends with the two bytes "}, so this cuts after the first byte of "ø".
        var split = Encoding.UTF8.GetByteCount(ClaudeLine("blåbærsyltet")) - 2 + 1;
        _repo.Append(_session, ClaudeLine("first") + "\n");
        _repo.AppendBytes(_session, bytes[..split]);
        using var store = NewStore();

        store.Poll();
        _repo.AppendBytes(_session, bytes[split..]);
        store.Poll();

        Assert.Equal([ClaudeLine("first"), line], Assert.Single(_factory.Parsers).Lines);
    }

    [Fact]
    public void A_missing_events_file_is_not_a_problem()
    {
        using var store = NewStore();

        store.Poll();

        var session = Assert.Single(store.Current.Sessions);
        Assert.Equal(Provider.Unknown, session.Provider);
        Assert.Same(SessionContent.Empty, session.Content);
        Assert.Empty(store.Current.Problems);
    }

    [Fact]
    public void An_events_file_that_disappears_keeps_what_was_read()
    {
        _repo.Append(_session, ClaudeLine("one") + "\n");
        using var store = NewStore();
        store.Poll();

        File.Delete(_session.EventsPath);
        store.Poll();

        Assert.Equal([ClaudeLine("one")], Texts(Assert.Single(store.Current.Sessions)));
        Assert.Empty(store.Current.Problems);
        Assert.Equal(1, store.Current.Version);
    }
}
