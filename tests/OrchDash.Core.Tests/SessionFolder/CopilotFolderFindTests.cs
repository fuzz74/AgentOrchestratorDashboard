using OrchDash.Core.SessionFolder;
using Xunit;

namespace OrchDash.Core.Tests.SessionFolder;

/// <summary>Find on workspace.yaml files the tests write (spec 12.3, 12.4).</summary>
public sealed class CopilotFolderFindTests : IDisposable
{
    private const string Name = "orch:task";
    private const string WorkDir = @"C:\Work\Repo.worktrees\task";
    private const string CreatedAt = "2026-10-03T10:00:00.000Z";

    private static readonly DateTimeOffset Created = new(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly HashSet<string> NoIds = [];

    private readonly TempSessionState _temp = new();

    public void Dispose() => _temp.Dispose();

    private CopilotFolderStore Store() => new(_temp.Dir);

    [Fact]
    public void Quoted_values_lose_one_pair_of_quotes_and_values_keep_their_colons()
    {
        _temp.WriteWorkspace("folder", """
            # written by a test
            id: "abc"
            cwd: 'C:\Work\Repo.worktrees\task'
            name: "orch:task"
            created_at: '2026-10-03T10:00:00.000Z'
            a line without a colon
            """);

        Assert.Equal("abc", Store().Find(Name, WorkDir, Created, NoIds));
    }

    [Fact]
    public void Only_one_pair_of_quotes_is_removed()
    {
        _temp.WriteWorkspace("folder", $"id: \"'abc'\"\ncwd: {WorkDir}\nname: {Name}\ncreated_at: {CreatedAt}\n");

        Assert.Equal("'abc'", Store().Find(Name, WorkDir, Created, NoIds));
    }

    [Fact]
    public void Quotes_that_do_not_match_stay()
    {
        _temp.WriteWorkspace("folder", $"id: abc\ncwd: {WorkDir}\nname: \"orch:task'\ncreated_at: {CreatedAt}\n");

        Assert.Null(Store().Find(Name, WorkDir, Created, NoIds));
        Assert.Equal("abc", Store().Find("\"orch:task'", WorkDir, Created, NoIds));
    }

    [Fact]
    public void Values_are_trimmed_and_crlf_lines_are_read()
    {
        _temp.WriteWorkspace("folder", $"id:   abc   \r\ncwd:\t{WorkDir} \r\nname: {Name}\r\ncreated_at: {CreatedAt}\r\n");

        Assert.Equal("abc", Store().Find(Name, WorkDir, Created, NoIds));
    }

    [Theory]
    [InlineData("c:/work/repo.worktrees/task/")]
    [InlineData(@"C:\WORK\Repo.worktrees\Task\")]
    [InlineData("C:/Work/Repo.worktrees/task")]
    public void The_folder_is_compared_with_either_slash_without_a_trailing_one_and_ignoring_case(string workDir)
    {
        _temp.WriteWorkspace("abc", Name, WorkDir, CreatedAt);

        Assert.Equal("abc", Store().Find(Name, workDir, Created, NoIds));
    }

    [Fact]
    public void A_trailing_slash_in_the_yaml_is_removed_too()
    {
        _temp.WriteWorkspace("abc", Name, "C:/Work/Repo.worktrees/task/", CreatedAt);

        Assert.Equal("abc", Store().Find(Name, WorkDir, Created, NoIds));
    }

    [Fact]
    public void Another_folder_gives_null()
    {
        _temp.WriteWorkspace("abc", Name, WorkDir, CreatedAt);

        Assert.Null(Store().Find(Name, @"C:\Work\Repo.worktrees\other", Created, NoIds));
    }

    [Fact]
    public void The_nearer_created_at_wins()
    {
        _temp.WriteWorkspace("far", Name, WorkDir, "2026-10-03T10:00:00.000Z");
        _temp.WriteWorkspace("near", Name, WorkDir, "2026-10-03T10:00:20.000Z");

        Assert.Equal("near", Store().Find(Name, WorkDir, Created.AddSeconds(12), NoIds));
        Assert.Equal("far", Store().Find(Name, WorkDir, Created.AddSeconds(8), NoIds));
    }

    [Fact]
    public void With_the_nearer_one_known_the_other_wins()
    {
        _temp.WriteWorkspace("far", Name, WorkDir, "2026-10-03T10:00:00.000Z");
        _temp.WriteWorkspace("near", Name, WorkDir, "2026-10-03T10:00:20.000Z");

        Assert.Equal("far", Store().Find(Name, WorkDir, Created.AddSeconds(12), new HashSet<string> { "near" }));
        Assert.Null(Store().Find(Name, WorkDir, Created.AddSeconds(12), new HashSet<string> { "near", "far" }));
    }

    [Fact]
    public void The_id_comes_from_the_yaml_not_from_the_folder_name()
    {
        _temp.WriteWorkspace("folder-name", $"id: real-id\ncwd: {WorkDir}\nname: {Name}\ncreated_at: {CreatedAt}\n");

        Assert.Equal("real-id", Store().Find(Name, WorkDir, Created, NoIds));
        Assert.Null(Store().Find(Name, WorkDir, Created, new HashSet<string> { "real-id" }));
    }

    [Theory]
    [InlineData(30.0, true)]
    [InlineData(-30.0, true)]
    [InlineData(30.001, false)]
    [InlineData(-30.001, false)]
    public void A_distance_of_exactly_30_seconds_is_accepted_and_more_is_not(double seconds, bool found)
    {
        _temp.WriteWorkspace("abc", Name, WorkDir, CreatedAt);

        var id = Store().Find(Name, WorkDir, Created.AddMilliseconds(seconds * 1000), NoIds);

        Assert.Equal(found ? "abc" : null, id);
    }

    [Fact]
    public void Created_at_is_utc_also_without_an_offset()
    {
        _temp.WriteWorkspace("abc", Name, WorkDir, "2026-10-03T10:00:00");

        Assert.Equal("abc", Store().Find(Name, WorkDir, new DateTimeOffset(2026, 10, 3, 12, 0, 10, TimeSpan.FromHours(2)), NoIds));
    }

    [Theory]
    [InlineData("cwd: C:\\Work\\Repo.worktrees\\task\nname: orch:task\ncreated_at: 2026-10-03T10:00:00.000Z\n")]
    [InlineData("id: \ncwd: C:\\Work\\Repo.worktrees\\task\nname: orch:task\ncreated_at: 2026-10-03T10:00:00.000Z\n")]
    [InlineData("id: abc\nname: orch:task\ncreated_at: 2026-10-03T10:00:00.000Z\n")]
    [InlineData("id: abc\ncwd: C:\\Work\\Repo.worktrees\\task\ncreated_at: 2026-10-03T10:00:00.000Z\n")]
    [InlineData("id: abc\ncwd: C:\\Work\\Repo.worktrees\\task\nname: orch:task\n")]
    [InlineData("id: abc\ncwd: C:\\Work\\Repo.worktrees\\task\nname: orch:task\ncreated_at: yesterday\n")]
    [InlineData("")]
    public void A_file_without_a_usable_id_name_cwd_or_created_at_is_no_candidate(string yaml)
    {
        _temp.WriteWorkspace("abc", yaml);

        Assert.Null(Store().Find(Name, WorkDir, Created, NoIds));
    }

    [Fact]
    public void A_folder_without_workspace_yaml_and_a_file_beside_the_folders_are_skipped()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Dir, "empty"));
        File.WriteAllText(Path.Combine(_temp.Dir, "workspace.yaml"), $"id: top\ncwd: {WorkDir}\nname: {Name}\ncreated_at: {CreatedAt}\n");
        _temp.WriteWorkspace("abc", Name, WorkDir, CreatedAt);

        Assert.Equal("abc", Store().Find(Name, WorkDir, Created, NoIds));
    }

    [Fact]
    public void An_unchanged_file_is_not_read_again()
    {
        _temp.WriteWorkspace("abc", Name, WorkDir, CreatedAt);
        var store = Store();
        Assert.Equal("abc", store.Find(Name, WorkDir, Created, NoIds));

        using var locked = new FileStream(_temp.WorkspacePath("abc"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Equal("abc", store.Find(Name, WorkDir, Created, NoIds));
    }

    [Fact]
    public void A_changed_file_is_read_again()
    {
        _temp.WriteWorkspace("abc", Name, WorkDir, CreatedAt);
        var store = Store();
        Assert.Equal("abc", store.Find(Name, WorkDir, Created, NoIds));

        _temp.WriteWorkspace("abc", "orch:renamed", WorkDir, CreatedAt);

        Assert.Null(store.Find(Name, WorkDir, Created, NoIds));
        Assert.Equal("abc", store.Find("orch:renamed", WorkDir, Created, NoIds));
    }

    [Fact]
    public void A_locked_file_that_was_never_read_is_no_candidate()
    {
        _temp.WriteWorkspace("abc", Name, WorkDir, CreatedAt);

        using var locked = new FileStream(_temp.WorkspacePath("abc"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Null(Store().Find(Name, WorkDir, Created, NoIds));
    }

    [Fact]
    public void A_deleted_folder_is_no_candidate_any_more()
    {
        _temp.WriteWorkspace("abc", Name, WorkDir, CreatedAt);
        var store = Store();
        Assert.Equal("abc", store.Find(Name, WorkDir, Created, NoIds));

        Directory.Delete(Path.Combine(_temp.Dir, "abc"), recursive: true);

        Assert.Null(store.Find(Name, WorkDir, Created, NoIds));
    }

    [Fact]
    public void A_missing_session_state_folder_gives_null()
    {
        var store = new CopilotFolderStore(Path.Combine(_temp.Dir, "missing"));

        Assert.Null(store.Find(Name, WorkDir, Created, NoIds));
    }
}
