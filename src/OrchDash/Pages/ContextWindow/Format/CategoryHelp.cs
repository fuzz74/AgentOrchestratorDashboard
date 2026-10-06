using OrchDash.Contracts;

namespace OrchDash.Pages.ContextWindow.Format;

// The explanation of each category of the make-up, opened by a click on its line: what the category is, how it gets
// into the context, why it matters and how this page measures it. Written for a developer who is not an AI expert.
public static class CategoryHelp
{
    public const string WhatItIs = "What it is";
    public const string HowItGetsIn = "How it gets into the context";
    public const string WhyItMatters = "Why it matters";
    public const string OnThisPage = "On this page";

    public static ContextPopup Popup(PartCategory category) => category switch
    {
        PartCategory.SystemPrompt => Popup(category,
            "The standing instructions of the agent. Before the conversation starts, the tool you run (Claude Code, " +
            "the Copilot CLI) tells the model who it is and how to behave: its role, the rules it must follow, how and " +
            "when to use its tools, how to format its answers, and facts about the environment such as the operating " +
            "system, the working directory, the git status and the date. The project's own instruction file " +
            "(CLAUDE.md, copilot-instructions.md) is appended to it. The vendor of the tool writes most of this text; " +
            "the user's task is not part of it.",
            "A model call is one request that carries the whole conversation. The system prompt travels in a slot of " +
            "its own: the system parameter of the Anthropic Messages API, or a message with the role system in " +
            "OpenAI-style APIs, and models are trained to treat that slot as authoritative. The model keeps nothing " +
            "between calls, so the harness sends the same system prompt with every call, and it counts towards the " +
            "context of every call.",
            "It is the fixed cost of every call: a few thousand tokens paid before the user has said a word, on each of " +
            "the hundreds of calls an agent makes. Because it is identical from call to call it is the first thing " +
            "prompt caching saves: the provider keeps the processed prefix of a request and charges a fraction for the " +
            "next request that starts with the same bytes, so a system prompt that changes between calls (a clock, a " +
            "counter) costs real money. It also shapes the agent's behaviour in ways the user never sees, so when an " +
            "agent does something odd, this is the first place to look.",
            "For a Claude Code session the blocks come from the prompt snapshot in the transcript, in order; their " +
            "tokens are estimates spread over the parts of the first call by their characters (est.). For a Copilot CLI " +
            "session the checkpoint in the session folder gives the exact token count of each segment of the system " +
            "prompt but not its text. Key s opens the text of the selected session's system prompt when it is known."),

        PartCategory.ToolDefinitions => Popup(category,
            "The functions the model is allowed to call. A language model only produces text: it cannot read a file, " +
            "run a command or search the web. The harness offers it tools instead, each with a name, a description of " +
            "what it does and when to use it, and a JSON schema of its parameters. Read, Edit, Bash and Grep are tools, " +
            "and so is every function that an MCP server (Model Context Protocol, the standard for plugging external " +
            "services into an agent) adds to the session.",
            "The definitions are sent with every call as the tools array of the request, next to the system prompt. " +
            "When the model wants to use one it runs nothing itself: it answers with a structured tool-call block, the " +
            "name and a JSON object of arguments that fits the schema. The harness executes the call, appends the " +
            "result to the conversation as a tool-result message and makes the next model call. That loop (the model " +
            "proposes, the harness runs, the result goes back) is what turns a chat model into an agent. The model " +
            "picks a tool by its description, which is why descriptions read like documentation.",
            "Tool definitions are the other fixed cost of every call, and often the larger one: a session with several " +
            "MCP servers can carry tens of thousands of tokens of schemas before any work is done. Like the system " +
            "prompt they are identical on every call, so they cache well, and some harnesses send only the names of " +
            "rarely used tools and load the full schema on demand. A tool that is defined but never called still costs " +
            "its tokens on every call.",
            "For a Claude Code session each tool of the transcript's prompt snapshot is one part, with the characters " +
            "of its description and schema and estimated tokens (est.). For a Copilot CLI session the checkpoint gives " +
            "one exact token count for all tools together, with their names, so they form one part. Key t opens the " +
            "definitions of the selected session."),

        PartCategory.Injected => Popup(category,
            "Text that the harness slips into the conversation on its own: the user did not type it and the model did " +
            "not write it. Typical items are reminders (the token budget, the user has not heard from you for a " +
            "while), memory and instruction files loaded as they become relevant, the list of available skills, the " +
            "user's current editor selection and notes about what a hook did. It is how the tool keeps the model up to " +
            "date on things that change while the conversation runs, without touching the system prompt.",
            "Injected text is attached to a user turn, usually wrapped in a tag such as <system-reminder>, so that the " +
            "model reads it as background information and not as a request from the user. It is added at a point in " +
            "time: the calls before that point never saw it, and every call after it carries it for the rest of the " +
            "session.",
            "Injected text explains behaviour that is neither in the system prompt nor in the user's words: why the " +
            "model suddenly knows a file, follows a rule from a memory file or changes how often it reports back. It " +
            "also adds up. A long session collects many reminders, each paid for on every later call, so when the " +
            "context fills faster than the visible conversation suggests, this is one of the places the tokens went.",
            "Claude Code records each injection in the transcript as an attachment with the text that was rendered for " +
            "the model; its kind (total_tokens_reminder, nested_memory and so on) is the part's label, and the part is " +
            "born at the first call that started after the injection's time. Tokens are estimates (est.). The Copilot " +
            "CLI session folder records no injections, so Copilot sessions show none here."),

        PartCategory.Prompt => Popup(category,
            "The task. The first user message of the session: what the agent was asked to do. In an orchestrated run " +
            "the user is the orchestrator, which writes a prompt file for each worker and reviewer with the task, the " +
            "acceptance criteria, the conventions of the repository and the way to report back, and starts the agent " +
            "with it. It is the one part of the context whose text the author of the run controls completely.",
            "It is sent as the first message with the role user, after the system prompt and the tool definitions, and " +
            "like everything else it is sent again with every call of the session: the model does not remember the " +
            "task between calls, it re-reads it. A resumed attempt of the same task sends a prompt of its own.",
            "The prompt is the main lever: it carries the goal and the constraints, and everything the model does is an " +
            "attempt to satisfy it within the rules of the system prompt. It is written once but sent with every call, " +
            "so a prompt that pastes whole files or logs keeps them in the context for the entire session; pointing the " +
            "agent at files it can read with its tools is cheaper. When an agent went wrong, read the prompt together " +
            "with the system prompt: the two are the whole of its instructions.",
            "One part per attempt of the chain, labelled prompt #<attempt>, with the characters of the prompt file in " +
            "the run's logs folder (for the Copilot CLI the prompt as it was sent, when the session folder has it). " +
            "Tokens are estimates (est.). Enter on the part shows the text."),

        PartCategory.Conversation => Popup(category,
            "Everything said and done since the prompt: the model's visible replies, its thinking (the reasoning it " +
            "writes for itself before it answers, on models that support it), every tool call it made with the " +
            "arguments it chose, and every tool result the harness sent back: file contents, command output, search " +
            "hits, error messages. This is the part that grows.",
            "A model has no memory, so each call sends the complete history as a list of messages: the prompt, then " +
            "assistant messages (text, thinking, tool calls) alternating with user messages (tool results, new input). " +
            "The output of one call becomes input of the next, which is why an item enters the context at the call " +
            "after the one that produced it. Tool results stay as they were returned unless the harness truncates them " +
            "or clears old ones; whether earlier thinking stays depends on the provider.",
            "Because the whole history is sent again each time, the cost of a call grows with the length of the " +
            "conversation and the cost of a session grows roughly with the square of it. Tool results are usually the " +
            "largest contributor: one listing of a big file can cost more than the entire system prompt. Near the limit " +
            "of the context window the harness has to compact: it asks the model to summarise the history and " +
            "continues from the summary, and the context drops sharply. Reading too much, or too much at once, is the " +
            "most common way an agent runs out of context.",
            "One part per item of the session's event log: text, thinking, user text, a notice, or a tool call labelled " +
            "with its summary, with the characters of its text or of the call's input and result. An item is born at " +
            "the call after the one that produced it. Tokens are estimates spread by characters (est.), so a thinking " +
            "block and a tool result of the same length get the same count. Enter on a part shows its text, or a tool " +
            "call's input and result."),

        PartCategory.Other => Popup(category,
            "Tokens that the provider counted in the call's context but that this page cannot attribute to any part it " +
            "knows. The provider reports the size of each call's input exactly: the fresh input tokens plus the tokens " +
            "read from and written to the prompt cache. The parts on this page are rebuilt from the logs the tools " +
            "leave behind, and the two do not always add up.",
            "Several things are in the request without appearing in any log: the framing tokens of each message and " +
            "tool call, instructions that the harness or the provider adds to the request, tool results the log " +
            "truncated and conversation items the log does not record. Those tokens are real and paid for, but there " +
            "is no part to hang them on.",
            "A large Other means that the picture on this page is incomplete, not that the model got nothing for the " +
            "tokens. A call that added no known part while the context still grew puts its whole growth here. A small " +
            "Other on every call is normal overhead.",
            "Other is the call's context minus the tokens of all known parts, shown only while it is above 0. It has " +
            "no parts and no characters, so it carries no est. mark and does not appear in the part list."),

        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    // "<category> explained" with the four sections.
    private static ContextPopup Popup(PartCategory category, string what, string how, string why, string here) =>
        new(ContextText.CategoryName(category) + " explained",
        [
            new PopupSection(WhatItIs, what),
            new PopupSection(HowItGetsIn, how),
            new PopupSection(WhyItMatters, why),
            new PopupSection(OnThisPage, here),
        ]);
}
