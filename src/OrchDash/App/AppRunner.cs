using OrchDash.Contracts;
using OrchDash.Core.Claude;
using OrchDash.Core.Copilot;
using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;
using OrchDash.Core.Store;
using OrchDash.Pages.Conversation;
using OrchDash.Pages.Overview;
using OrchDash.Shell;
using XenoAtom.Terminal.UI;

namespace OrchDash.App;

/// <summary>
/// The whole app behind <c>Program.Main</c> (spec 1.1-1.5): finds the repo, starts the store, builds the shell and runs
/// the UI through <c>runUi</c>, which is <c>Terminal.Run</c> in production and a fake in tests.
/// </summary>
public static class AppRunner
{
    /// <summary>The run ended normally: the shell returned Stop, or the user pressed Ctrl+Q.</summary>
    public const int ExitOk = 0;

    /// <summary>An exception ended the app; its message is on stderr.</summary>
    public const int ExitError = 1;

    /// <summary>No <c>.orchestrator</c> folder at or above the start path; the UI did not start.</summary>
    public const int ExitNoRepo = 2;

    /// <summary>
    /// Runs the app and returns its exit code. <paramref name="args"/>[0], when given, is the start path, resolved
    /// against <paramref name="currentDirectory"/>; otherwise the search starts at <paramref name="currentDirectory"/>.
    /// <paramref name="stderr"/> is written only before the UI starts or after <paramref name="runUi"/> has returned
    /// or thrown, so never while the fullscreen UI is shown.
    /// </summary>
    public static int Run(string[] args, string currentDirectory, TextWriter stderr, Action<Visual, Func<TerminalLoopResult>> runUi)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(currentDirectory);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(runUi);

        var startPath = args.Length > 0 ? Resolve(args[0], currentDirectory) : currentDirectory;
        var repo = RepoLocator.Find(startPath);
        if (repo is null)
        {
            stderr.WriteLine($"No {RepoLocator.RunFolderName} folder at or above {startPath}");
            return ExitNoRepo;
        }

        try
        {
            // The store is disposed when this block is left, also by an exception, before the catch below runs.
            using var store = CreateStore(repo);
            store.Start();
            var shell = new AppShell(CreatePages(), () => store.Current);
            runUi(shell.Root, shell.OnUpdate);
            return ExitOk;
        }
        catch (Exception e)
        {
            // Terminal.Run leaves the fullscreen UI in a finally block, so the screen is restored by now.
            stderr.WriteLine(e.Message);
            return ExitError;
        }
    }

    /// <summary>A store on <paramref name="repoPath"/> with the real reader and the session parser of each provider.</summary>
    public static RunStore CreateStore(string repoPath, TimeSpan? pollInterval = null) =>
        new(repoPath, new RunFolderReader(), CreateParser, pollInterval);

    /// <summary>The pages in tab order: Overview first, then Conversation.</summary>
    public static IReadOnlyList<IPage> CreatePages() => [new OverviewPage(), new ConversationPage()];

    private static ISessionParser CreateParser(Provider provider, string? workDir) =>
        provider == Provider.Claude ? new ClaudeSessionParser(workDir) : new CopilotSessionParser(workDir);

    /// <summary>The full path of <paramref name="path"/> relative to <paramref name="currentDirectory"/>; the path unchanged when it is not valid.</summary>
    private static string Resolve(string path, string currentDirectory)
    {
        try
        {
            return Path.GetFullPath(path, currentDirectory);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }
}
