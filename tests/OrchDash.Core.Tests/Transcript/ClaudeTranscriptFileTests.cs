using OrchDash.Core.Transcript;
using Xunit;
using static OrchDash.Core.Tests.Transcript.TranscriptLines;

namespace OrchDash.Core.Tests.Transcript;

/// <summary>Lines, unchanged files and unreadable files (spec 11.4-11.6).</summary>
public sealed class ClaudeTranscriptFileTests : IDisposable
{
    private readonly TempProjects _projects = new();

    public void Dispose() => _projects.Dispose();

    [Fact]
    public void Lines_that_are_not_json_objects_are_counted_and_skipped()
    {
        _projects.Write(Folder, SessionId, Lines(
            "not json",
            Assistant("msg_1", Time1, Usage(1, 2, 3, 4, 0)),
            "[1,2]",
            "42",
            "",
            """{"type":"assistant",""",
            CostState(0.5, 1, 0)));

        var data = new ClaudeTranscriptStore(_projects.ProjectsDir).Read(SessionId, WorkDir);

        Assert.Equal(5, data?.UnparsedLines);
        Assert.Equal(["msg_1"], data?.Calls.Select(call => call.CallId));
        Assert.Equal(0.5, data?.CostUsd);
    }

    [Fact]
    public void Records_of_unknown_types_are_skipped_without_counting()
    {
        _projects.Write(Folder, SessionId, Lines(
            """{"type":"user","message":{"id":"msg_1","usage":{"output_tokens":4}}}""",
            """{"type":"custom-title","rendered":[{"content":"x"}]}""",
            """{"message":{"id":"msg_2"}}""",
            """{"type":7}"""));

        var data = new ClaudeTranscriptStore(_projects.ProjectsDir).Read(SessionId, WorkDir);

        Assert.NotNull(data);
        Assert.Equal(0, data.UnparsedLines);
        Assert.Empty(data.Calls);
        Assert.Empty(data.Injected);
    }

    [Fact]
    public void Crlf_line_ends_and_a_byte_order_mark_are_removed()
    {
        var path = _projects.Write(Folder, SessionId, "");
        File.WriteAllText(path, "﻿" + CostState(0.5, 1, 0) + "\r\n" + Assistant("msg_1", Time1, Usage(1, 2, 3, 4, 0)) + "\r\n",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var data = new ClaudeTranscriptStore(_projects.ProjectsDir).Read(SessionId, WorkDir);

        Assert.Equal(0, data?.UnparsedLines);
        Assert.Equal(0.5, data?.CostUsd);
        Assert.Single(data!.Calls);
    }

    [Fact]
    public void A_last_line_without_a_line_break_is_read_once_its_break_arrives()
    {
        var path = _projects.Write(Folder, SessionId, Lines(CostState(0.25, 1, 0)) + CostState(0.5, 2, 0));
        var store = new ClaudeTranscriptStore(_projects.ProjectsDir);

        var before = store.Read(SessionId, WorkDir);
        Assert.Equal(0.25, before?.CostUsd);
        Assert.Equal(0, before?.UnparsedLines);

        TempProjects.Append(path, "\n");
        var after = store.Read(SessionId, WorkDir);
        Assert.Equal(0.5, after?.CostUsd);
        Assert.Equal(2, after?.LinesAdded);
    }

    [Fact]
    public void An_unchanged_file_gives_the_same_instance_and_an_append_a_new_one()
    {
        var path = _projects.Write(Folder, SessionId, Lines(Assistant("msg_1", Time1, Usage(1, 2, 3, 4, 0))));
        var store = new ClaudeTranscriptStore(_projects.ProjectsDir);

        var first = store.Read(SessionId, WorkDir);
        Assert.NotNull(first);
        Assert.Same(first, store.Read(SessionId, WorkDir));

        TempProjects.Append(path, Lines(Assistant("msg_2", Time2, Usage(1, 2, 3, 5, 0))));
        var second = store.Read(SessionId, WorkDir);
        Assert.NotSame(first, second);
        Assert.Equal(["msg_1", "msg_2"], second?.Calls.Select(call => call.CallId));
        Assert.Same(second, store.Read(SessionId, WorkDir));
    }

    [Fact]
    public void A_new_last_write_time_with_the_same_length_reads_the_file_again()
    {
        var path = _projects.Write(Folder, SessionId, Lines(CostState(0.25, 1, 0)));
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc));
        var store = new ClaudeTranscriptStore(_projects.ProjectsDir);
        var first = store.Read(SessionId, WorkDir);

        File.WriteAllText(path, Lines(CostState(0.75, 1, 0)));
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
        var second = store.Read(SessionId, WorkDir);

        Assert.Equal(0.25, first?.CostUsd);
        Assert.Equal(0.75, second?.CostUsd);
    }

    [Fact]
    public void A_file_that_cannot_be_opened_gives_the_last_result()
    {
        var path = _projects.Write(Folder, SessionId, Lines(CostState(0.25, 1, 0)));
        var store = new ClaudeTranscriptStore(_projects.ProjectsDir);
        var last = store.Read(SessionId, WorkDir);
        Assert.NotNull(last);

        TempProjects.Append(path, Lines(CostState(0.5, 2, 0)));
        using (TempProjects.Hold(path))
        {
            Assert.Same(last, store.Read(SessionId, WorkDir));
            Assert.Same(last, store.Read(SessionId, null));
        }

        Assert.Equal(0.5, store.Read(SessionId, WorkDir)?.CostUsd);
    }

    [Fact]
    public void A_file_that_cannot_be_opened_gives_null_without_an_earlier_result()
    {
        var path = _projects.Write(Folder, SessionId, Lines(CostState(0.25, 1, 0)));
        var store = new ClaudeTranscriptStore(_projects.ProjectsDir);

        using (TempProjects.Hold(path))
            Assert.Null(store.Read(SessionId, WorkDir));

        Assert.Equal(0.25, store.Read(SessionId, WorkDir)?.CostUsd);
    }

    [Fact]
    public void The_file_is_read_with_sharing_so_a_writer_can_keep_it_open()
    {
        var path = _projects.Write(Folder, SessionId, "");
        using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete);
        writer.Write(System.Text.Encoding.UTF8.GetBytes(Lines(CostState(0.25, 1, 0))));
        writer.Flush();

        var data = new ClaudeTranscriptStore(_projects.ProjectsDir).Read(SessionId, WorkDir);

        Assert.Equal(0.25, data?.CostUsd);
    }
}
