using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation.Format;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Pages.Conversation.Format;

public sealed class ConversationTextTests
{
    private static readonly DateTimeOffset Now = SampleRun.At(12, 30, 0);

    private readonly RunSnapshot _run = SampleRun.Create();

    private Session AlphaWorker => _run.Sessions.Single(s => s.Files.Key == SampleRun.AlphaWorkerKey);
    private Session AlphaReview => _run.Sessions.Single(s => s.Files.Key == SampleRun.AlphaReviewKey);
    private Session BetaWorker => _run.Sessions.Single(s => s.Files.Key == SampleRun.BetaWorkerKey);

    [Fact]
    public void Sessions_are_ordered_bootstrap_planner_then_by_task()
    {
        var bootstrap = WithFiles(AlphaWorker, "bootstrap-20261003-115900/attempt-1.json", null, AgentRole.Bootstrap);
        var planner = WithFiles(AlphaWorker, "planner-20261003-115930-1.json", null, AgentRole.Planner);
        var orphan = WithFiles(BetaWorker, "zeta/20261003-121500/attempt-1-worker.json", "zeta", AgentRole.Worker);
        var run = _run with { Sessions = [orphan, BetaWorker, AlphaWorker, planner, AlphaReview, bootstrap] };

        var keys = ConversationText.OrderSessions(run).Select(s => s.Files.Key);

        Assert.Equal(
        [
            "bootstrap-20261003-115900/attempt-1.json",
            "planner-20261003-115930-1.json",
            SampleRun.AlphaWorkerKey,
            SampleRun.AlphaReviewKey,
            SampleRun.BetaWorkerKey,
            "zeta/20261003-121500/attempt-1-worker.json",
        ], keys);
    }

    [Fact]
    public void Session_rows()
    {
        Assert.Equal("[success]✔[/] alpha [cyan]worker[/] #1 claude-sonnet-4-5 5m50s 2 tool calls 0.25 USD",
            ConversationText.SessionRow(AlphaWorker, Now));
        Assert.Equal("[success]✔[/] alpha [yellow]reviewer[/] #1.1 gpt-5.1 2m25s 1 tool call",
            ConversationText.SessionRow(AlphaReview, Now));
        Assert.Equal("[primary]▶[/] beta [cyan]worker[/] #1 claude-sonnet-4-5 19m55s 3 tool calls",
            ConversationText.SessionRow(BetaWorker, Now));
    }

    [Fact]
    public void Session_rows_of_nudges_and_sessions_without_task()
    {
        var reviewNudge = AlphaReview with { Files = AlphaReview.Files with { IsNudge = true } };
        var workerNudge = AlphaWorker with { Files = AlphaWorker.Files with { IsNudge = true, Attempt = 2 } };
        var planner = WithFiles(AlphaReview, "planner-20261003-115930-1.json", null, AgentRole.Planner) with
        {
            State = SessionState.Failed,
        };

        Assert.Equal("[success]✔[/] alpha [yellow]reviewer[/] #1.1 nudge gpt-5.1 2m25s 1 tool call",
            ConversationText.SessionRow(reviewNudge, Now));
        Assert.Equal("[success]✔[/] alpha [cyan]worker[/] #2 nudge claude-sonnet-4-5 5m50s 2 tool calls 0.25 USD",
            ConversationText.SessionRow(workerNudge, Now));
        Assert.Equal("[error]✖[/] planner [blue]planner[/] #1 gpt-5.1 2m25s 1 tool call",
            ConversationText.SessionRow(planner, Now));
    }

    [Fact]
    public void Duration_falls_back_to_the_running_time_and_the_last_event()
    {
        var withoutResultDuration = AlphaWorker with
        {
            Content = AlphaWorker.Content with { Result = AlphaWorker.Content.Result! with { Duration = null } },
        };
        var withoutStart = BetaWorker with { StartedAt = null };
        var unknown = new Session(AlphaWorker.Files, Provider.Unknown, SessionState.Aborted, "", null, SessionContent.Empty);

        Assert.Equal(TimeSpan.FromSeconds(350), ConversationText.Duration(AlphaWorker, Now));
        Assert.Equal(new TimeSpan(0, 5, 40), ConversationText.Duration(withoutResultDuration, Now));
        Assert.Equal(new TimeSpan(0, 19, 55), ConversationText.Duration(BetaWorker, Now));
        Assert.Null(ConversationText.Duration(withoutStart, Now));
        Assert.Null(ConversationText.Duration(unknown, Now));
        Assert.Equal("[muted]◌[/] alpha [cyan]worker[/] #1 0 tool calls", ConversationText.SessionRow(unknown, Now));
    }

    [Fact]
    public void Alpha_worker_entries()
    {
        var entries = ConversationText.Entries(AlphaWorker, Now);

        Assert.Equal(
        [
            EntryKind.Header, EntryKind.Prompt,
            EntryKind.CallSeparator, EntryKind.Thinking, EntryKind.AssistantText, EntryKind.ToolCall,
            EntryKind.CallSeparator, EntryKind.Thinking, EntryKind.ToolCall, EntryKind.AssistantText,
            EntryKind.Result,
        ], entries.Select(e => e.Kind));

        Assert.Equal(
        [
            "Claude [cyan]worker[/] alpha claude-sonnet-4-5 [success]✔ succeeded[/]",
            "session 0b7f3c2a-5d41-4e8a-9c11-2f6a8d3e4b70 · started 12:00:10 · 5m50s · 9 turns · 0.25 USD",
        ], entries[0].Lines);
        Assert.Equal(
        [
            $"[accent]prompt[/] [muted]{AlphaWorker.Prompt.Length} chars[/] You are a worker agent of the orchestrator.",
            "Task: alpha - Alpha parser",
            "Implement the alpha parser in src/Alpha....",
        ], entries[1].Lines);
        Assert.Equal(223, AlphaWorker.Prompt.Length);
        Assert.Equal(["[muted]call 1 · 12:00:10 · context 19.2k[/]"], entries[2].Lines);
        Assert.Equal(
        [
            "[muted]The task asks for a parser in src/Alpha.[/]",
            "[muted]The spec says empty input gives null.[/]",
            "[muted]There is a stub Parser class already....[/]",
        ], entries[3].Lines);
        Assert.Equal(["I will replace the Parser stub in src/Alpha/Parser.cs."], entries[4].Lines);
        Assert.Equal(
        [
            "Edit src/Alpha/Parser.cs [success]ok[/] 1s",
            "[muted]The file src/Alpha/Parser.cs has been updated.[/]",
        ], entries[5].Lines);
        Assert.Equal(["[muted]call 2 · 12:03:00 · context 43.3k[/]"], entries[6].Lines);
        Assert.Equal(["[muted]thinking (~1.2k tokens, no text)[/]"], entries[7].Lines);
        Assert.Equal(
        [
            "Bash dotnet test tests/Alpha [error]error[/] 35s",
            "[muted]Build FAILED....[/]",
        ], entries[8].Lines);
        Assert.Equal(["The parser is in place and all alpha tests pass."], entries[9].Lines);
        Assert.Equal(
        [
            "[success]result success[/] [success]done[/] Added Parser with Parse and TryParse.",
            "Parse returns null on empty input.",
            "Added six tests in tests/Alpha/ParserTests.cs....",
        ], entries[10].Lines);
    }

    [Fact]
    public void Review_entries()
    {
        var entries = ConversationText.Entries(AlphaReview, Now);

        Assert.Equal(
        [
            EntryKind.Header, EntryKind.Prompt,
            EntryKind.CallSeparator, EntryKind.Thinking, EntryKind.ToolCall,
            EntryKind.CallSeparator, EntryKind.AssistantText,
            EntryKind.Result,
        ], entries.Select(e => e.Kind));
        Assert.Equal(
        [
            "Copilot [yellow]reviewer[/] alpha gpt-5.1 [success]✔ succeeded[/]",
            "session c41e7b9d-2a6f-4d03-b8e5-7f19a2c6d840 · started 12:06:30 · 2m25s · 2 turns",
            "[warning]2 lines not understood[/]",
        ], entries[0].Lines);
        Assert.Equal(["[muted]call 1 · 12:06:30[/]"], entries[2].Lines);
        Assert.Equal(["view src/Alpha/Parser.cs [success]ok[/] 1s", "[muted]namespace Alpha;...[/]"], entries[4].Lines);
        Assert.Equal(["[muted]call 2 · 12:07:40[/]"], entries[5].Lines);
        Assert.Equal(["[success]result success[/] spec [success]pass[/] quality [error]fail[/] 2 issues"], entries[7].Lines);
    }

    [Fact]
    public void Running_session_has_a_running_tool_call_and_no_result()
    {
        var entries = ConversationText.Entries(BetaWorker, Now);

        Assert.Equal(
        [
            EntryKind.Header, EntryKind.Prompt,
            EntryKind.CallSeparator, EntryKind.Thinking, EntryKind.AssistantText, EntryKind.ToolCall, EntryKind.ToolCall,
            EntryKind.CallSeparator, EntryKind.AssistantText, EntryKind.ToolCall,
        ], entries.Select(e => e.Kind));
        Assert.Equal(
        [
            "Claude [cyan]worker[/] beta claude-sonnet-4-5 [primary]▶ running[/]",
            "session 9a3c5e71-8b2d-4f60-a4c9-1d7e0b2f6a58 · started 12:10:05 · 19m55s",
        ], entries[0].Lines);
        Assert.Equal(["Bash dotnet build src/Beta [primary]running[/]"], entries[^1].Lines);
        Assert.Equal([new PopupSection("Input", """{"command":"dotnet build src/Beta"}""", TextKind.Json)], entries[^1].Popup);
    }

    [Fact]
    public void Items_without_a_known_new_call_get_no_separator()
    {
        var content = AlphaWorker.Content with
        {
            Calls = [new ModelCall("c1", null, null, null), new ModelCall("c2", null, SampleRun.At(12, 1, 0), null)],
            Items =
            [
                new AssistantText(null, null, "no call"),
                new AssistantText("c1", null, "first"),
                new AssistantText("c1", null, "same call"),
                new AssistantText("unknown", null, "unknown call"),
                new AssistantText("c2", null, "second"),
            ],
            Result = null,
        };

        var entries = ConversationText.Entries(AlphaWorker with { Content = content }, Now).Skip(2).ToList();

        Assert.Equal(
        [
            "no call", "[muted]call 1[/]", "first", "same call", "unknown call", "[muted]call 2 · 12:01:00[/]", "second",
        ], entries.Select(e => e.Lines.Single()));
    }

    [Fact]
    public void Thinking_without_text_or_estimate()
    {
        var session = WithItems(AlphaWorker, new Thinking("msg_01A7alpha", null, "", null));

        var thinking = ConversationText.Entries(session, Now).Single(e => e.Kind == EntryKind.Thinking);

        Assert.Equal(["[muted]thinking (no text)[/]"], thinking.Lines);
        Assert.Equal([new PopupSection("Text", "thinking (no text)")], thinking.Popup);
    }

    [Fact]
    public void User_text_and_notice_start_with_their_label()
    {
        var session = WithItems(AlphaWorker,
            new UserText(null, null, "Please continue.", IsSynthetic: false),
            new UserText(null, null, "Caveat: local command output", IsSynthetic: true),
            new Notice(null, null, "permission denied", "Bash: not allowed\nsecond line"));

        var entries = ConversationText.Entries(session, Now).Skip(2).Take(3).ToList();

        Assert.Equal(["[accent]user[/] Please continue."], entries[0].Lines);
        Assert.Equal(EntryKind.UserText, entries[0].Kind);
        Assert.Equal(["[accent]user (synthetic)[/] Caveat: local command output"], entries[1].Lines);
        Assert.Equal(["[warning]permission denied[/] Bash: not allowed", "second line"], entries[2].Lines);
        Assert.Equal(EntryKind.Notice, entries[2].Kind);
        Assert.Equal("permission denied", entries[2].PopupTitle);
        Assert.Equal([new PopupSection("Text", "Bash: not allowed\nsecond line")], entries[2].Popup);
    }

    [Fact]
    public void Result_without_report_or_verdict_shows_its_text()
    {
        var result = AlphaWorker.Content.Result! with
        {
            IsError = true, Subtype = "error_max_turns", Text = "line 1\nline 2\nline 3\nline 4",
            StructuredJson = null, Worker = null,
        };
        var session = AlphaWorker with { Content = AlphaWorker.Content with { Result = result } };

        var entry = ConversationText.Entries(session, Now)[^1];

        Assert.Equal(["[error]result error_max_turns[/] line 1", "line 2", "line 3..."], entry.Lines);
        Assert.Equal([new PopupSection("Text", "line 1\nline 2\nline 3\nline 4")], entry.Popup);
    }

    [Fact]
    public void No_entry_is_longer_than_three_lines()
    {
        const string longText = "1\n2\n3\n4\n5\n6\n7\n8\n9\n10";
        var longItems = WithItems(AlphaWorker,
            new AssistantText(null, null, longText),
            new Thinking(null, null, longText, 10),
            new UserText(null, null, longText, false),
            new Notice(null, null, "notice", longText),
            new ToolCall(null, null, "t", "Bash", "{}", "Bash x", new ToolResult(null, false, longText, null, 3))) with
        {
            Prompt = longText,
        };
        var noEvents = AlphaWorker with { Files = AlphaWorker.Files with { HasEventsFile = false } };

        foreach (var session in _run.Sessions.Append(longItems).Append(noEvents))
        {
            foreach (var entry in ConversationText.Entries(session, Now))
                Assert.InRange(entry.Lines.Length, 1, 3);
        }
    }

    [Fact]
    public void Header_popup_has_files_init_and_result()
    {
        var header = ConversationText.Entries(AlphaWorker, Now)[0];

        Assert.Equal(SampleRun.AlphaWorkerKey, header.PopupTitle);
        Assert.Equal(["Files", "Init", "Result"], header.Popup.Select(p => p.Heading));
        Assert.All(header.Popup, p => Assert.Equal(TextKind.Plain, p.Kind));
        Assert.Equal(
            $"""
            Key: {SampleRun.AlphaWorkerKey}
            TaskId: alpha
            Role: worker
            StartFolder: 20261003-120005
            Attempt: 1
            ReviewTry: 0
            IsNudge: false
            ResultPath: {AlphaWorker.Files.ResultPath}
            PromptPath: {AlphaWorker.Files.PromptPath}
            EventsPath: {AlphaWorker.Files.EventsPath}
            StderrPath: {AlphaWorker.Files.StderrPath}
            HasResultFile: true
            HasEventsFile: true
            PromptWrittenAt: 12:00:05
            """.ReplaceLineEndings("\n"), header.Popup[0].Text);
        Assert.Equal(
            """
            Cwd: C:\Work\SampleRepo.worktrees\alpha
            PermissionMode: bypassPermissions
            CliVersion: 2.1.3
            Tools: Read, Edit, Write, Glob, Grep, Bash, StructuredOutput
            McpServers: github (connected)
            """.ReplaceLineEndings("\n"), header.Popup[1].Text);
        Assert.Equal(
            $"""
            IsError: false
            Subtype: success
            Text: 48 chars
            StructuredJson: {AlphaWorker.Content.Result!.StructuredJson!.Length} chars
            Worker: status done
            Review: none
            CostUsd: 0.25 USD
            Turns: 9
            Duration: 5m50s
            ApiDuration: 4m50s
            Usage: input 2.0k, cache read 56.0k, cache write 4.5k, output 3.1k
            ContextWindow: 200.0k
            PremiumRequests: none
            LinesAdded: none
            LinesRemoved: none
            """.ReplaceLineEndings("\n"), header.Popup[2].Text);
    }

    [Fact]
    public void Header_popup_leaves_out_missing_init_and_result()
    {
        var review = ConversationText.Entries(AlphaReview, Now)[0];
        var beta = ConversationText.Entries(BetaWorker, Now)[0];

        Assert.Equal(["Files", "Result"], review.Popup.Select(p => p.Heading));
        Assert.Contains("Review: spec pass, quality fail, 2 issues", review.Popup[1].Text, StringComparison.Ordinal);
        Assert.Contains("PremiumRequests: 1", review.Popup[1].Text, StringComparison.Ordinal);
        Assert.Equal(["Files", "Init"], beta.Popup.Select(p => p.Heading));
        Assert.Contains("McpServers: none", beta.Popup[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Popups_of_prompt_texts_and_separators()
    {
        var entries = ConversationText.Entries(AlphaWorker, Now);
        var items = AlphaWorker.Content.Items;

        Assert.Equal("Prompt", entries[1].PopupTitle);
        Assert.Equal([new PopupSection("Prompt", AlphaWorker.Prompt)], entries[1].Popup);
        Assert.Empty(entries[2].Popup);
        Assert.Equal("Thinking", entries[3].PopupTitle);
        Assert.Equal([new PopupSection("Text", ((Thinking)items[0]).Text)], entries[3].Popup);
        Assert.Equal("Assistant text", entries[4].PopupTitle);
        Assert.Equal([new PopupSection("Text", ((AssistantText)items[1]).Text)], entries[4].Popup);
        Assert.Equal([new PopupSection("Text", "thinking (~1.2k tokens, no text)")], entries[7].Popup);
    }

    [Fact]
    public void Tool_call_popups()
    {
        var entries = ConversationText.Entries(AlphaWorker, Now);
        var edit = (ToolCall)AlphaWorker.Content.Items[2];
        var bash = (ToolCall)AlphaWorker.Content.Items[4];

        Assert.Equal("Edit src/Alpha/Parser.cs", entries[5].PopupTitle);
        Assert.Equal(
        [
            new PopupSection("Input", edit.InputJson, TextKind.Json),
            new PopupSection("Result", "The file src/Alpha/Parser.cs has been updated."),
            new PopupSection("Diff", edit.Result!.Diff!, TextKind.Diff),
        ], entries[5].Popup);
        Assert.Equal(
        [
            new PopupSection("Input", bash.InputJson, TextKind.Json),
            new PopupSection("Result", bash.Result!.Content),
        ], entries[8].Popup);
    }

    [Fact]
    public void Tool_call_result_popup_starts_with_a_known_exit_code()
    {
        var call = new ToolCall(null, null, "t", "powershell", "{}", "powershell dotnet build",
            new ToolResult(null, true, "error CS1002", null, 1));

        var entry = ConversationText.Entries(WithItems(AlphaWorker, call), Now)[2];

        Assert.Equal(["powershell dotnet build [error]error[/]", "[muted]error CS1002[/]"], entry.Lines);
        Assert.Equal(new PopupSection("Result", "exit code: 1\nerror CS1002"), entry.Popup[1]);
    }

    [Fact]
    public void Result_popups()
    {
        var worker = ConversationText.Entries(AlphaWorker, Now)[^1];
        var review = ConversationText.Entries(AlphaReview, Now)[^1];
        var result = AlphaReview.Content.Result!;

        Assert.Equal("Result", worker.PopupTitle);
        Assert.Equal(
        [
            new PopupSection("Text", AlphaWorker.Content.Result!.Text!),
            new PopupSection("Structured", AlphaWorker.Content.Result!.StructuredJson!, TextKind.Json),
        ], worker.Popup);
        Assert.Equal(
        [
            new PopupSection("Text", result.Text!),
            new PopupSection("Structured", result.StructuredJson!, TextKind.Json),
            new PopupSection("Issues",
                "major · src/Alpha/Parser.cs · TryParse catches every exception and hides the cause.\n" +
                "minor · No test covers input with only whitespace."),
        ], review.Popup);
    }

    [Fact]
    public void Result_popup_leaves_out_empty_sections()
    {
        var result = AlphaReview.Content.Result! with
        {
            Text = null, Review = AlphaReview.Content.Result!.Review! with { Issues = [] },
        };
        var session = AlphaReview with { Content = AlphaReview.Content with { Result = result } };

        var entry = ConversationText.Entries(session, Now)[^1];

        Assert.Equal(["[success]result success[/] spec [success]pass[/] quality [error]fail[/] 0 issues"], entry.Lines);
        Assert.Equal([new PopupSection("Structured", result.StructuredJson!, TextKind.Json)], entry.Popup);
    }

    [Fact]
    public void Session_without_events_file_shows_header_prompt_and_no_event_log()
    {
        var session = AlphaWorker with { Files = AlphaWorker.Files with { HasEventsFile = false } };

        var entries = ConversationText.Entries(session, Now);

        Assert.Equal([EntryKind.Header, EntryKind.Prompt, EntryKind.NoEventLog], entries.Select(e => e.Kind));
        Assert.Equal(["[muted]no event log[/]"], entries[2].Lines);
        Assert.Empty(entries[2].Popup);
    }

    [Fact]
    public void Model_text_is_escaped()
    {
        var withModel = AlphaWorker with { Content = AlphaWorker.Content with { Model = "model[1]" } };
        var session = WithItems(withModel,
            new AssistantText(null, null, "Use [bold] here"),
            new Thinking(null, null, "x[0] = 1", null),
            new Notice(null, null, "[kind]", "[text]"),
            new ToolCall(null, null, "t", "Grep", "{}", "Grep a[bc] in src", new ToolResult(null, false, "[/]", null, null))) with
        {
            Prompt = "[red]prompt",
        };

        var entries = ConversationText.Entries(session, Now);

        Assert.StartsWith("Claude [cyan]worker[/] alpha model[[1]] ", entries[0].Lines[0], StringComparison.Ordinal);
        Assert.EndsWith("[[red]]prompt", entries[1].Lines[0], StringComparison.Ordinal);
        Assert.Equal(["Use [[bold]] here"], entries[2].Lines);
        Assert.Equal(["[muted]x[[0]] = 1[/]"], entries[3].Lines);
        Assert.Equal(["[warning][[kind]][/] [[text]]"], entries[4].Lines);
        Assert.Equal(["Grep a[[bc]] in src [success]ok[/]", "[muted][[/]][/]"], entries[5].Lines);
        Assert.Contains("model[[1]]", ConversationText.SessionRow(session, Now), StringComparison.Ordinal);
    }

    private static Session WithFiles(Session session, string key, string? taskId, AgentRole role) =>
        session with { Files = session.Files with { Key = key, TaskId = taskId, Role = role } };

    private static Session WithItems(Session session, params ConversationItem[] items) =>
        session with { Content = session.Content with { Items = [.. items] } };
}
