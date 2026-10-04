namespace OrchDash.Core.Tests.Fixtures;

/// <summary>Locations of the fixture runs in the test output (see tests/Fixtures/README.md).</summary>
public static class FixturePaths
{
    /// <summary>Root of the copied fixtures: <c>&lt;test output&gt;/Fixtures</c>.</summary>
    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>Repo folder of the Claude run (AnsiDemo).</summary>
    public static string ClaudeRepo { get; } = Path.Combine(Root, "claude-run");

    /// <summary>Repo folder of the Copilot run (TextKit).</summary>
    public static string CopilotRepo { get; } = Path.Combine(Root, "copilot-run");

    /// <summary>The <c>.orchestrator</c> folder of the Claude run.</summary>
    public static string ClaudeRunDir { get; } = Path.Combine(ClaudeRepo, ".orchestrator");

    /// <summary>The <c>.orchestrator</c> folder of the Copilot run.</summary>
    public static string CopilotRunDir { get; } = Path.Combine(CopilotRepo, ".orchestrator");

    /// <summary>Copy of Claude Code's <c>.claude</c> folder for the Claude run (holds <c>projects</c>).</summary>
    public static string ClaudeStore { get; } = Path.Combine(Root, "stores", "claude");

    /// <summary>Copy of Copilot's <c>.copilot</c> folder for the Copilot run (holds <c>session-state</c> and <c>session-store.db</c>).</summary>
    public static string CopilotStore { get; } = Path.Combine(Root, "stores", "copilot");
}
