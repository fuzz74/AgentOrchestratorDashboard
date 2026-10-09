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
    private readonly RunSnapshot _subRun = SampleRun.CreateSubAgents();

    private Session AlphaWorker => _run.Sessions.Single(s => s.Files.Key == SampleRun.AlphaWorkerKey);
    private Session AlphaReview => _run.Sessions.Single(s => s.Files.Key == SampleRun.AlphaReviewKey);
    private Session BetaWorker => _run.Sessions.Single(s => s.Files.Key == SampleRun.BetaWorkerKey);
    private Session Planner => _subRun.Sessions.Single(s => s.Files.Key == SampleRun.PlannerKey);
    private Session SubAlphaWorker => _subRun.Sessions.Single(s => s.Files.Key == SampleRun.AlphaWorkerKey);
    private Session SubBetaWorker => _subRun.Sessions.Single(s => s.Files.Key == SampleRun.BetaWorkerKey);

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

    [Fact]
    public void Child_rows_show_name_type_model_span_and_the_sub_agents_own_tool_calls()
    {
        Assert.Equal(
        [
            "  [muted]├[/][success]✔[/] Map the repo · explore · gpt-5.6-luna · 28s · 2 tool calls",
            "  [muted]│ └[/][success]✔[/] Read the spec · explore · gpt-5.6-luna · 14s · 1 tool call",
            "  [muted]└[/][success]✔[/] Survey the tests · explore · gpt-5.6-luna · 12s · 1 tool call",
        ], AgentTree.Rows([Planner]).Select(row => ConversationText.ChildRow(row, Now)));
        Assert.Equal(
        [
            "  [muted]├[/][success]✔[/] Survey the parser module · Explore · claude-haiku-4-5 · 41s · 2 tool calls",
            "  [muted]└[/][error]✖[/] Check the public API surface of… · general-purpose · claude-sonnet-4-5 · 44s · 1 tool call",
        ], AgentTree.Rows([SubAlphaWorker]).Select(row => ConversationText.ChildRow(row, Now)));
        Assert.Equal(
            ["  [muted]└[/][primary]▶[/] Survey CLI flags · Explore · claude-haiku-4-5 · 50s · 1 tool call"],
            AgentTree.Rows([SubBetaWorker]).Select(row => ConversationText.ChildRow(row, Now)));
    }

    [Fact]
    public void Child_rows_of_unknown_fields_show_dashes_and_escape_the_name()
    {
        var sub = SubAlphaWorker.Content.SubAgents[0] with { Name = "List [all] files", AgentType = null, Model = "", StartedAt = null };
        var session = SubAlphaWorker with { Content = SubAlphaWorker.Content with { SubAgents = [sub] } };

        Assert.Equal("  [muted]└[/][success]✔[/] List [[all]] files · - · - · - · 2 tool calls",
            ConversationText.ChildRow(new AgentRow(session, sub, 1, "└"), Now));
    }

    [Fact]
    public void A_running_sub_agent_of_a_session_that_ended_is_aborted()
    {
        var ended = SubBetaWorker with { State = SessionState.Succeeded };
        var sub = ended.Content.SubAgents.Single();

        var start = ConversationText.Entries(ended, Now).Single(e => e.Kind == EntryKind.SubAgent);
        var entries = ConversationText.SubAgentEntries(ended, SampleRun.BetaSub1Id, Now);

        Assert.Equal("  [muted]└[/][muted]◌[/] Survey CLI flags · Explore · claude-haiku-4-5 · 50s · 1 tool call",
            ConversationText.ChildRow(AgentTree.Rows([ended]).Single(), Now));
        Assert.Equal(["[muted]◌[/] [accent]sub-agent[/] Survey CLI flags · Explore · aborted"], start.Lines);
        Assert.Equal("Explore · claude-haiku-4-5 · [muted]◌ aborted[/] · started 12:29:10 · 50s · 1 tool call", entries[0].Lines[1]);
        Assert.Equal(EntryKind.Result, entries[^1].Kind);
        Assert.Equal(["[muted]result aborted[/]"], entries[^1].Lines);
        Assert.Empty(entries[^1].Popup);
        AssertSameEntry(entries[^1], ConversationText.SubAgentResultEntry(ended, sub));
    }

    [Fact]
    public void Session_rows_count_only_the_agents_own_tool_calls()
    {
        Assert.Equal("[success]✔[/] planner [blue]planner[/] #1 gpt-5.6-luna 58s 2 tool calls",
            ConversationText.SessionRow(Planner, Now));
        Assert.Equal("[success]✔[/] alpha [cyan]worker[/] #1 claude-sonnet-4-5 5m50s 4 tool calls 0.25 USD",
            ConversationText.SessionRow(SubAlphaWorker, Now));
        Assert.Equal("[primary]▶[/] beta [cyan]worker[/] #1 claude-sonnet-4-5 19m55s 4 tool calls",
            ConversationText.SessionRow(SubBetaWorker, Now));
    }

    [Fact]
    public void Session_entries_show_only_the_agents_own_items_and_number_its_own_calls()
    {
        var entries = ConversationText.Entries(Planner, Now);

        Assert.Equal(
        [
            EntryKind.Header, EntryKind.Prompt,
            EntryKind.CallSeparator, EntryKind.AssistantText, EntryKind.SubAgent, EntryKind.SubAgent,
            EntryKind.CallSeparator, EntryKind.AssistantText,
            EntryKind.Result,
        ], entries.Select(e => e.Kind));
        Assert.Equal(["[muted]call 1 · 11:59:02 · context 12.4k[/]"], entries[2].Lines);
        Assert.Equal(["I will map the repo first, then survey the tests."], entries[3].Lines);
        Assert.Equal(["[muted]call 2 · 11:59:54 · context 15.8k[/]"], entries[6].Lines);
        Assert.Equal(
            ["The plan has five tasks in three waves: alpha and gamma, then beta and delta, then epsilon."],
            entries[7].Lines);
    }

    [Fact]
    public void The_alpha_worker_entries_keep_their_calls_and_add_a_start_entry_per_sub_agent()
    {
        var entries = ConversationText.Entries(SubAlphaWorker, Now);

        Assert.Equal(
        [
            EntryKind.Header, EntryKind.Prompt,
            EntryKind.CallSeparator, EntryKind.Thinking, EntryKind.AssistantText, EntryKind.ToolCall, EntryKind.SubAgent,
            EntryKind.CallSeparator, EntryKind.Thinking, EntryKind.ToolCall, EntryKind.SubAgent, EntryKind.AssistantText,
            EntryKind.Result,
        ], entries.Select(e => e.Kind));
        Assert.Equal(["[muted]call 1 · 12:00:10 · context 19.2k[/]"], entries[2].Lines);
        Assert.Equal(["[muted]call 2 · 12:03:00 · context 43.3k[/]"], entries[7].Lines);
        Assert.Equal(
        [
            "[error]✖[/] [accent]sub-agent[/] Check the public API surface of… · general-purpose · failed",
            "[muted]API Error: 529 Overloaded. The sub-agent stopped before it finished.[/]",
        ], entries[10].Lines);
        Assert.Equal(SampleRun.AlphaSub2Id, entries[10].SelectsAgentId);
        Assert.All(entries.Where(e => e.Kind != EntryKind.SubAgent), e => Assert.Null(e.SelectsAgentId));
    }

    [Fact]
    public void A_sub_agent_start_entry_reads_name_type_state_and_the_reports_first_line()
    {
        var call = (ToolCall)SubAlphaWorker.Content.Items[3];
        var sub = SubAlphaWorker.Content.SubAgents[0];

        var entry = ConversationText.SubAgentStartEntry(SubAlphaWorker, call, sub);

        Assert.Equal(EntryKind.SubAgent, entry.Kind);
        Assert.Equal(
        [
            "[success]✔[/] [accent]sub-agent[/] Survey the parser module · Explore · succeeded",
            "[muted]The parser module has 3 files.[/]",
        ], entry.Lines);
        Assert.Equal(SampleRun.AlphaSub1Id, entry.SelectsAgentId);
        Assert.Equal("Agent Survey the parser module", entry.PopupTitle);
        Assert.Equal(
        [
            new PopupSection("Input", call.InputJson, TextKind.Json),
            new PopupSection("Result", "The parser module has 3 files."),
        ], entry.Popup);
        AssertSameEntry(entry, ConversationText.Entries(SubAlphaWorker, Now)[6]);
    }

    [Fact]
    public void A_running_sub_agent_start_entry_has_no_report_line()
    {
        var entry = ConversationText.Entries(SubBetaWorker, Now).Single(e => e.Kind == EntryKind.SubAgent);

        Assert.Equal(["[primary]▶[/] [accent]sub-agent[/] Survey CLI flags · Explore · running"], entry.Lines);
        Assert.Equal(SampleRun.BetaSub1Id, entry.SelectsAgentId);
        Assert.Equal(["Input"], entry.Popup.Select(p => p.Heading));
    }

    [Fact]
    public void Sub_agent_entries_show_header_prompt_its_items_and_result()
    {
        var sub = SubAlphaWorker.Content.SubAgents[0];

        var entries = ConversationText.SubAgentEntries(SubAlphaWorker, SampleRun.AlphaSub1Id, Now);

        Assert.Equal(
        [
            EntryKind.Header, EntryKind.Prompt,
            EntryKind.CallSeparator, EntryKind.ToolCall, EntryKind.ToolCall,
            EntryKind.CallSeparator, EntryKind.AssistantText,
            EntryKind.Result,
        ], entries.Select(e => e.Kind));
        Assert.Equal(
        [
            "[accent]sub-agent[/] alpha worker #1 › Survey the parser module",
            "Explore · claude-haiku-4-5 · [success]✔ succeeded[/] · started 12:00:17 · 41s · 2 tool calls",
        ], entries[0].Lines);
        Assert.Equal(
            [$"[accent]prompt[/] [muted]{sub.Prompt.Length} chars[/] List the files in src/Alpha and say what each one holds."],
            entries[1].Lines);
        Assert.Equal("Prompt", entries[1].PopupTitle);
        Assert.Equal([new PopupSection("Prompt", sub.Prompt)], entries[1].Popup);
        Assert.Equal(["[muted]call 1 · 12:00:19 · context 4.0k[/]"], entries[2].Lines);
        Assert.Equal(["Glob src/Alpha/** [success]ok[/] 1s", "[muted]src/Alpha/Lexer.cs...[/]"], entries[3].Lines);
        Assert.Equal(["Read src/Alpha/Parser.cs [success]ok[/] 1s", "[muted]namespace Alpha;...[/]"], entries[4].Lines);
        Assert.Equal(["[muted]call 2 · 12:00:50 · context 5.4k[/]"], entries[5].Lines);
        Assert.Equal(["The parser module has 3 files."], entries[6].Lines);
        Assert.Equal(["[success]result succeeded[/] The parser module has 3 files."], entries[7].Lines);
        Assert.Equal("Result", entries[7].PopupTitle);
        Assert.Equal([new PopupSection("Report", "The parser module has 3 files.")], entries[7].Popup);

        AssertSameEntry(entries[0], ConversationText.SubAgentHeader(SubAlphaWorker, sub, Now));
        AssertSameEntry(entries[1], ConversationText.SubAgentPromptEntry(sub));
        AssertSameEntry(entries[7], ConversationText.SubAgentResultEntry(SubAlphaWorker, sub));
    }

    [Fact]
    public void Sub_agent_header_popup_lists_its_fields()
    {
        var header = ConversationText.SubAgentEntries(SubAlphaWorker, SampleRun.AlphaSub1Id, Now)[0];
        var nested = ConversationText.SubAgentEntries(Planner, SampleRun.PlannerSub2Id, Now)[0];
        var running = ConversationText.SubAgentEntries(SubBetaWorker, SampleRun.BetaSub1Id, Now)[0];

        Assert.Equal("alpha worker #1 › Survey the parser module", header.PopupTitle);
        Assert.Equal(
        [
            new PopupSection("Sub-agent",
                """
                Id: toolu_alpha_sub1
                ToolCallId: toolu_alpha_sub1
                Parent: agent
                AgentType: Explore
                Model: claude-haiku-4-5
                Background: false
                Description: Survey the parser module
                StartedAt: 12:00:17
                FinishedAt: 12:00:58
                """.ReplaceLineEndings("\n")),
        ], header.Popup);
        Assert.Equal("planner #1 › Map the repo › Read the spec", nested.PopupTitle);
        Assert.Contains("Parent: Map the repo\n", nested.Popup.Single().Text, StringComparison.Ordinal);
        Assert.Contains("Background: true\n", running.Popup.Single().Text, StringComparison.Ordinal);
        Assert.EndsWith("FinishedAt: none", running.Popup.Single().Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sub_agent_entries_number_the_calls_among_its_own()
    {
        var entries = ConversationText.SubAgentEntries(Planner, SampleRun.PlannerSub3Id, Now);

        Assert.Equal(
        [
            "[accent]sub-agent[/] planner #1 › Survey the tests",
            "explore · gpt-5.6-luna · [success]✔ succeeded[/] · started 11:59:39 · 12s · 1 tool call",
        ], entries[0].Lines);
        Assert.Equal(["[muted]call 1 · 11:59:40 · context 6.1k[/]"], entries[2].Lines);
        Assert.Equal(["glob tests/**/*.cs [success]ok[/] 0s", "[muted]tests/Sample.Tests/SmokeTests.cs[/]"], entries[3].Lines);
        Assert.Equal(["One test project, tests/Sample.Tests, with a single smoke test."], entries[4].Lines);
        Assert.Equal(
            ["[success]result succeeded[/] One test project, tests/Sample.Tests, with a single smoke test."],
            entries[5].Lines);
        Assert.Equal(6, entries.Length);
    }

    [Fact]
    public void A_sub_agent_that_started_another_shows_its_start_entry()
    {
        var entries = ConversationText.SubAgentEntries(Planner, SampleRun.PlannerSub1Id, Now);

        Assert.Equal(
        [
            EntryKind.Header, EntryKind.Prompt,
            EntryKind.CallSeparator, EntryKind.SubAgent, EntryKind.ToolCall, EntryKind.AssistantText,
            EntryKind.Result,
        ], entries.Select(e => e.Kind));
        Assert.Equal("[accent]sub-agent[/] planner #1 › Map the repo", entries[0].Lines[0]);
        Assert.Equal(["[muted]call 1 · 11:59:08 · context 6.8k[/]"], entries[2].Lines);
        Assert.Equal(
        [
            "[success]✔[/] [accent]sub-agent[/] Read the spec · explore · succeeded",
            "[muted]The spec asks for five tasks: alpha, beta, gamma, delta and epsilon.[/]",
        ], entries[3].Lines);
        Assert.Equal(SampleRun.PlannerSub2Id, entries[3].SelectsAgentId);
        Assert.Equal(["glob **/* [success]ok[/] 1s", "[muted]src/Sample/Sample.csproj...[/]"], entries[4].Lines);
        Assert.Equal(["[success]result succeeded[/] Three folders: src, tests and docs."], entries[6].Lines);
    }

    [Fact]
    public void A_running_sub_agent_has_no_result_entry()
    {
        var entries = ConversationText.SubAgentEntries(SubBetaWorker, SampleRun.BetaSub1Id, Now);

        Assert.Equal(
            [EntryKind.Header, EntryKind.Prompt, EntryKind.CallSeparator, EntryKind.ToolCall],
            entries.Select(e => e.Kind));
        Assert.Equal(
        [
            "[accent]sub-agent[/] beta worker #1 › Survey CLI flags",
            "Explore · claude-haiku-4-5 · [primary]▶ running[/] · started 12:29:10 · 50s · 1 tool call",
        ], entries[0].Lines);
        Assert.Equal(["[muted]call 1 · 12:29:12 · context 3.6k[/]"], entries[2].Lines);
        Assert.Equal(["Read src/Beta/Program.cs [primary]running[/]"], entries[3].Lines);
    }

    [Fact]
    public void A_failed_sub_agent_result_shows_its_report_in_the_error_colour()
    {
        var entries = ConversationText.SubAgentEntries(SubAlphaWorker, SampleRun.AlphaSub2Id, Now);

        Assert.Equal(
            "general-purpose · claude-sonnet-4-5 · [error]✖ failed[/] · started 12:03:06 · 44s · 1 tool call",
            entries[0].Lines[1]);
        Assert.Equal(
            ["[error]result failed[/] API Error: 529 Overloaded. The sub-agent stopped before it finished."],
            entries[^1].Lines);
    }

    [Fact]
    public void An_unknown_sub_agent_id_gives_the_sessions_entries()
    {
        Assert.Equal(
            ConversationText.Entries(SubAlphaWorker, Now).Select(e => e.Lines),
            ConversationText.SubAgentEntries(SubAlphaWorker, "toolu_unknown", Now).Select(e => e.Lines));
    }

    [Fact]
    public void Sub_agent_header_shows_dashes_for_unknown_fields()
    {
        var sub = SubAlphaWorker.Content.SubAgents[0] with
        {
            ToolCallId = "", AgentType = null, Model = null, StartedAt = null, Description = null,
        };
        var session = SubAlphaWorker with { Content = SubAlphaWorker.Content with { SubAgents = [sub] } };

        var header = ConversationText.SubAgentHeader(session, sub, Now);

        Assert.Equal("- · - · [success]✔ succeeded[/] · started - · - · 2 tool calls", header.Lines[1]);
        Assert.Contains("ToolCallId: none\n", header.Popup.Single().Text, StringComparison.Ordinal);
        Assert.Contains("Description: none\n", header.Popup.Single().Text, StringComparison.Ordinal);
        Assert.Contains("StartedAt: none\n", header.Popup.Single().Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sub_agent_text_is_escaped()
    {
        var sub = SubAlphaWorker.Content.SubAgents[0] with { Name = "a[1]", AgentType = "[x]", Report = "[/]" };
        var session = SubAlphaWorker with { Content = SubAlphaWorker.Content with { SubAgents = [sub] } };
        var call = (ToolCall)session.Content.Items[3];

        Assert.Equal(["[success]✔[/] [accent]sub-agent[/] a[[1]] · [[x]] · succeeded", "[muted][[/]][/]"],
            ConversationText.SubAgentStartEntry(session, call, sub).Lines);
        Assert.Equal("[accent]sub-agent[/] alpha worker #1 › a[[1]]", ConversationText.SubAgentHeader(session, sub, Now).Lines[0]);
        Assert.Equal(["[success]result succeeded[/] [[/]]"], ConversationText.SubAgentResultEntry(session, sub).Lines);
    }

    [Fact]
    public void No_sub_agent_entry_is_longer_than_three_lines()
    {
        foreach (var session in _subRun.Sessions)
        {
            var entries = ConversationText.Entries(session, Now)
                .Concat(session.Content.SubAgents.SelectMany(sub => ConversationText.SubAgentEntries(session, sub.Id, Now)));
            foreach (var entry in entries)
                Assert.InRange(entry.Lines.Length, 1, 3);
        }
    }

    // Records compare their ImmutableArray members by reference, so the members are compared one by one.
    private static void AssertSameEntry(ConversationEntry expected, ConversationEntry actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Lines, actual.Lines);
        Assert.Equal(expected.PopupTitle, actual.PopupTitle);
        Assert.Equal(expected.Popup, actual.Popup);
        Assert.Equal(expected.SelectsAgentId, actual.SelectsAgentId);
    }

    private static Session WithFiles(Session session, string key, string? taskId, AgentRole role) =>
        session with { Files = session.Files with { Key = key, TaskId = taskId, Role = role } };

    private static Session WithItems(Session session, params ConversationItem[] items) =>
        session with { Content = session.Content with { Items = [.. items] } };
}
