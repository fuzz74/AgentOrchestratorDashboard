using OrchDash.Core.Transcript;
using Xunit;
using static OrchDash.Core.Tests.Transcript.TranscriptLines;

namespace OrchDash.Core.Tests.Transcript;

/// <summary>Where the transcript of a session is found (spec 11.1 and 11.2).</summary>
public sealed class ClaudeTranscriptLookupTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly TempProjects _projects = new();

    public void Dispose() => _projects.Dispose();

    private static string WithVersion(string version) => Lines($$"""{"type":"user","version":"{{version}}"}""");

    [Theory]
    [InlineData(@"C:\Data\AI\AnsiDemo.worktrees\audio-synth", "C--Data-AI-AnsiDemo-worktrees-audio-synth")]
    [InlineData(@"C:\Data\AI\AnsiDemo", "C--Data-AI-AnsiDemo")]
    [InlineData("/home/me/my_repo", "-home-me-my-repo")]
    [InlineData(@"D:\Prøve 2\x", "D--Pr-ve-2-x")]
    public void Folder_name_replaces_every_char_that_is_not_an_ascii_letter_or_digit(string workDir, string folder)
    {
        Assert.Equal(folder, ClaudeTranscriptStore.FolderName(workDir));
    }

    [Fact]
    public void The_work_dir_folder_is_read_before_any_other()
    {
        _projects.Write("A-first", SessionId, WithVersion("search"));
        _projects.Write(Folder, SessionId, WithVersion("work dir"));

        var data = new ClaudeTranscriptStore(_projects.ProjectsDir).Read(SessionId, WorkDir);

        Assert.Equal("work dir", data?.CliVersion);
    }

    [Fact]
    public void Without_a_work_dir_the_first_match_in_ordinal_path_order_is_read()
    {
        _projects.Write("C--b", SessionId, WithVersion("b"));
        _projects.Write("C--a", SessionId, WithVersion("a"));
        _projects.Write("C--0", "other-session", WithVersion("other"));

        var data = new ClaudeTranscriptStore(_projects.ProjectsDir).Read(SessionId, null);

        Assert.Equal("a", data?.CliVersion);
    }

    [Fact]
    public void Ordinal_path_order_compares_whole_paths_not_folder_names()
    {
        // "x-y\<id>.jsonl" comes before "x\<id>.jsonl" because '-' sorts before the separator.
        _projects.Write("x", SessionId, WithVersion("x"));
        _projects.Write("x-y", SessionId, WithVersion("x-y"));

        var data = new ClaudeTranscriptStore(_projects.ProjectsDir).Read(SessionId, null);

        Assert.Equal("x-y", data?.CliVersion);
    }

    [Fact]
    public void When_the_work_dir_folder_has_no_file_the_folders_are_searched()
    {
        _projects.Write("C--elsewhere", SessionId, WithVersion("found"));

        var data = new ClaudeTranscriptStore(_projects.ProjectsDir).Read(SessionId, WorkDir);

        Assert.Equal("found", data?.CliVersion);
    }

    [Fact]
    public void Nothing_found_gives_null()
    {
        _projects.Write(Folder, "other-session", WithVersion("other"));

        var store = new ClaudeTranscriptStore(_projects.ProjectsDir);

        Assert.Null(store.Read(SessionId, WorkDir));
        Assert.Null(store.Read(SessionId, null));
    }

    [Fact]
    public void A_missing_projects_folder_gives_null()
    {
        var store = new ClaudeTranscriptStore(_projects.MissingDir);

        Assert.Null(store.Read(SessionId, WorkDir));
        Assert.Null(store.Read(SessionId, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("a/b")]
    public void A_session_id_that_is_not_a_file_name_gives_null(string sessionId)
    {
        Assert.Null(new ClaudeTranscriptStore(_projects.ProjectsDir).Read(sessionId, null));
    }

    [Fact]
    public void After_a_failed_search_the_id_is_searched_again_only_once_30_seconds_have_passed()
    {
        var time = new ManualTime(Start);
        var store = new ClaudeTranscriptStore(_projects.ProjectsDir, time);
        Assert.Null(store.Read(SessionId, null));

        _projects.Write("C--late", SessionId, WithVersion("late"));
        Assert.Null(store.Read(SessionId, null));
        time.Advance(TimeSpan.FromSeconds(29.9));
        Assert.Null(store.Read(SessionId, null));

        time.Advance(TimeSpan.FromSeconds(0.1));
        Assert.Equal("late", store.Read(SessionId, null)?.CliVersion);
    }

    [Fact]
    public void The_pause_is_per_id_and_does_not_stop_the_work_dir_lookup()
    {
        var time = new ManualTime(Start);
        var store = new ClaudeTranscriptStore(_projects.ProjectsDir, time);
        Assert.Null(store.Read(SessionId, null));

        _projects.Write(Folder, SessionId, WithVersion("work dir"));
        _projects.Write("C--other", "other-session", WithVersion("other"));

        Assert.Null(store.Read(SessionId, null));
        Assert.Equal("work dir", store.Read(SessionId, WorkDir)?.CliVersion);
        Assert.Equal("other", store.Read("other-session", null)?.CliVersion);
    }

    [Fact]
    public void A_search_after_the_pause_that_fails_again_starts_a_new_pause()
    {
        var time = new ManualTime(Start);
        var store = new ClaudeTranscriptStore(_projects.ProjectsDir, time);
        Assert.Null(store.Read(SessionId, null));
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(store.Read(SessionId, null));

        _projects.Write("C--late", SessionId, WithVersion("late"));
        time.Advance(TimeSpan.FromSeconds(20));
        Assert.Null(store.Read(SessionId, null));
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal("late", store.Read(SessionId, null)?.CliVersion);
    }
}
