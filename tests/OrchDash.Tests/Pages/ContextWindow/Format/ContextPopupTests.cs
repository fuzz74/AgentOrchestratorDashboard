using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.ContextWindow.Format;
using OrchDash.Tests.Support;
using Xunit;

namespace OrchDash.Tests.Pages.ContextWindow.Format;

// The part, system prompt and tool definition pop-ups of the Context page on SampleRun.CreateEnriched().
public sealed class ContextPopupTests
{
    private readonly RunSnapshot _run = SampleRun.CreateEnriched();

    private Session Get(string key) => _run.Sessions.Single(s => s.Files.Key == key);

    private ContextMakeup Makeup(string key) => ContextMakeup.Build(_run, Get(key));

    private ContextPart Part(string key, string label) => Makeup(key).Parts.Single(p => p.Label == label);

    [Fact]
    public void System_prompt_block_shows_its_text()
    {
        AssertPopup(
            new ContextPopup("System prompt: block 1",
                [new PopupSection("Text", "You are Claude Code, Anthropic's official CLI for Claude.")]),
            ContextText.PartPopup(Part(SampleRun.AlphaWorkerKey, "block 1")));
    }

    [Fact]
    public void System_segment_shows_its_tokens_and_where_the_text_is()
    {
        AssertPopup(
            new ContextPopup("System prompt: tool_instructions",
            [
                new PopupSection("Tokens",
                    "1540 tokens, from Copilot's checkpoint. The text is part of the system prompt (key s)."),
            ]),
            ContextText.PartPopup(Part(SampleRun.AlphaReviewKey, "tool_instructions")));
    }

    [Fact]
    public void Tool_definition_shows_its_description_and_schema_as_json()
    {
        var read = Get(SampleRun.AlphaWorkerKey).Stores.Tools[0];

        AssertPopup(
            new ContextPopup("Tool definitions: Read",
            [
                new PopupSection("Description", "Reads a file from the local filesystem."),
                new PopupSection("Schema", read.SchemaJson!, TextKind.Json),
            ]),
            ContextText.PartPopup(Part(SampleRun.AlphaWorkerKey, "Read")));
    }

    [Fact]
    public void Tool_summary_shows_the_tool_names()
    {
        AssertPopup(
            new ContextPopup("Tool definitions: 2 tools", [new PopupSection("Tools", "view\npowershell")]),
            ContextText.PartPopup(Part(SampleRun.AlphaReviewKey, "2 tools")));
    }

    [Fact]
    public void Injected_item_shows_its_text()
    {
        AssertPopup(
            new ContextPopup("Injected: total_tokens_reminder",
                [new PopupSection("Text", "<system-reminder>\nToken usage: 19200/200000; 180800 remaining\n</system-reminder>")]),
            ContextText.PartPopup(Part(SampleRun.AlphaWorkerKey, "total_tokens_reminder")));
    }

    [Fact]
    public void Prompt_shows_the_prompt_as_sent_or_else_the_prompt()
    {
        var review = Get(SampleRun.AlphaReviewKey);
        var worker = Get(SampleRun.AlphaWorkerKey);

        AssertPopup(
            new ContextPopup("Prompt: prompt #1.1", [new PopupSection("Prompt", review.Content.SentPrompt!)]),
            ContextText.PartPopup(Part(SampleRun.AlphaReviewKey, "prompt #1.1")));
        Assert.StartsWith("<current_datetime>", review.Content.SentPrompt);
        AssertPopup(
            new ContextPopup("Prompt: prompt #1", [new PopupSection("Prompt", worker.Prompt)]),
            ContextText.PartPopup(Part(SampleRun.AlphaWorkerKey, "prompt #1")));
    }

    [Fact]
    public void Prompt_of_a_later_attempt_shows_that_attempt()
    {
        AssertPopup(
            new ContextPopup("Prompt: prompt #2", [new PopupSection("Prompt", Get(SampleRun.GammaWorker2Key).Prompt)]),
            ContextText.PartPopup(Part(SampleRun.GammaWorker1Key, "prompt #2")));
    }

    [Fact]
    public void Tool_call_shows_its_input_as_json_and_its_result()
    {
        var view = (ToolCall)Get(SampleRun.AlphaReviewKey).Content.Items[1];

        AssertPopup(
            new ContextPopup("Conversation: view src/Alpha/Parser.cs",
            [
                new PopupSection("Input", view.InputJson, TextKind.Json),
                new PopupSection("Result", "namespace Alpha;\n\npublic class Parser\n{\n}"),
            ]),
            ContextText.PartPopup(Part(SampleRun.AlphaReviewKey, "view src/Alpha/Parser.cs")));
    }

    [Fact]
    public void Tool_call_without_result_has_no_result_section()
    {
        var part = Part(SampleRun.BetaWorkerKey, "Read src/Alpha/Parser.cs");
        var running = part with { Source = ((ToolCall)part.Source) with { Result = null } };

        AssertPopup(
            new ContextPopup("Conversation: Read src/Alpha/Parser.cs",
            [
                new PopupSection("Input",
                    """{"file_path":"C:\\Work\\SampleRepo.worktrees\\beta\\src\\Alpha\\Parser.cs"}""", TextKind.Json),
            ]),
            ContextText.PartPopup(running));
    }

    [Fact]
    public void Other_conversation_items_show_their_text()
    {
        AssertPopup(
            new ContextPopup("Conversation: thinking",
                [new PopupSection("Text", "The feedback says empty input must be handled before formatting.")]),
            ContextText.PartPopup(Part(SampleRun.GammaWorker1Key, "thinking")));
        AssertPopup(
            new ContextPopup("Conversation: text",
                [new PopupSection("Text", "I will replace the Parser stub in src/Alpha/Parser.cs.")]),
            ContextText.PartPopup(Part(SampleRun.AlphaWorkerKey, "text")));
    }

    [Fact]
    public void System_prompt_popup_has_one_section_per_block()
    {
        AssertPopup(
            new ContextPopup("System prompt",
            [
                new PopupSection("Block 1, 57 characters", "You are Claude Code, Anthropic's official CLI for Claude."),
                new PopupSection("Block 2, 156 characters",
                    "You are an interactive agent that helps users with software engineering tasks.\n" +
                    "Use the instructions below and the tools available to you to assist the user."),
            ]),
            ContextText.SystemPromptPopup(Get(SampleRun.AlphaWorkerKey)));
    }

    [Fact]
    public void System_prompt_popup_without_blocks_shows_the_reasons_or_unavailable_alone()
    {
        var beta = Get(SampleRun.BetaWorkerKey);

        AssertPopup(
            new ContextPopup("System prompt", [new PopupSection("Unavailable", "unavailable: no transcript")]),
            ContextText.SystemPromptPopup(beta));
        AssertPopup(
            new ContextPopup("System prompt",
                [new PopupSection("Unavailable", "unavailable: no session folder, no database rows")]),
            ContextText.SystemPromptPopup(beta with { Unavailable = ["no session folder", "no database rows"] }));
        AssertPopup(
            new ContextPopup("System prompt", [new PopupSection("Unavailable", "unavailable")]),
            ContextText.SystemPromptPopup(beta with { Unavailable = [] }));
    }

    [Fact]
    public void Tools_popup_shows_the_definitions_of_the_alpha_worker()
    {
        var worker = Get(SampleRun.AlphaWorkerKey);
        var read = worker.Stores.Tools[0];
        var bash = worker.Stores.Tools[1];

        AssertPopup(
            new ContextPopup("Tool definitions",
            [
                new PopupSection("Read", "Reads a file from the local filesystem.\n\n" + read.SchemaJson),
                new PopupSection("Bash", "Executes a given bash command and returns its output.\n\n" + bash.SchemaJson),
            ]),
            ContextText.ToolsPopup(worker, Makeup(SampleRun.AlphaWorkerKey)));
    }

    [Fact]
    public void Tools_popup_shows_the_checkpoint_tokens_and_names_of_the_alpha_review()
    {
        AssertPopup(
            new ContextPopup("Tool definitions", [new PopupSection("Tools", "680 tokens\nview\npowershell")]),
            ContextText.ToolsPopup(Get(SampleRun.AlphaReviewKey), Makeup(SampleRun.AlphaReviewKey)));
    }

    [Fact]
    public void Tools_popup_of_the_beta_worker_is_unavailable()
    {
        AssertPopup(
            new ContextPopup("Tool definitions", [new PopupSection("Unavailable", "unavailable: no transcript")]),
            ContextText.ToolsPopup(Get(SampleRun.BetaWorkerKey), Makeup(SampleRun.BetaWorkerKey)));
    }

    // ContextPopup compares Sections by reference, so the title and the sections are compared one by one.
    private static void AssertPopup(ContextPopup expected, ContextPopup actual)
    {
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Sections, actual.Sections);
    }
}
