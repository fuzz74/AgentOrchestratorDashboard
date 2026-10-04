using System.Globalization;
using OrchDash.App;
using OrchDash.Core.Model;
using OrchDash.Core.Store;

namespace OrchDash.Tests.App;

/// <summary>The fixture repos and provider stores in the test output, and helpers for the end-to-end tests on them.</summary>
internal static class FixtureRuns
{
    /// <summary>The whole fixture folder in the test output.</summary>
    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string ClaudeRepo { get; } = Path.Combine(Root, "claude-run");

    public static string CopilotRepo { get; } = Path.Combine(Root, "copilot-run");

    /// <summary>The fixture's stand-in for <c>%USERPROFILE%\.claude</c>.</summary>
    public static string ClaudeStore { get; } = Path.Combine(Root, "stores", "claude");

    /// <summary>The fixture's stand-in for <c>%USERPROFILE%\.copilot</c>.</summary>
    public static string CopilotStore { get; } = Path.Combine(Root, "stores", "copilot");

    /// <summary>A store with the real reader, parsers and provider stores, on the fixture store folders.</summary>
    public static RunStore CreateStore(string repo, TimeSpan? pollInterval = null) =>
        AppRunner.CreateStore(repo, pollInterval, ClaudeStore, CopilotStore);

    /// <summary>The snapshot of a first <c>Poll()</c> of a store on the fixture store folders.</summary>
    public static RunSnapshot Poll(string repo) => Poll(repo, ClaudeStore, CopilotStore);

    /// <summary>The snapshot of a first <c>Poll()</c> of a store on <paramref name="claudeDir"/> and <paramref name="copilotDir"/>.</summary>
    public static RunSnapshot Poll(string repo, string claudeDir, string copilotDir)
    {
        using var store = AppRunner.CreateStore(repo, claudeDir: claudeDir, copilotDir: copilotDir);
        store.Poll();
        return store.Current;
    }

    /// <summary>
    /// Every folder below <paramref name="folder"/>, and every file with its size and last write time, sorted, so that
    /// two listings are equal only when nothing was created, changed, deleted or renamed (N.5).
    /// </summary>
    /// <remarks>
    /// Sizes and times are read from each file itself: the values that come with a directory enumeration are copies in
    /// the parent folder's index, which NTFS updates lazily. For the same reason folders are compared by name only.
    /// </remarks>
    public static string[] Listing(string folder) =>
    [
        .. Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var relative = Path.GetRelativePath(folder, path);
                if (Directory.Exists(path))
                {
                    return "folder " + relative;
                }
                var file = new FileInfo(path);
                return string.Create(CultureInfo.InvariantCulture, $"file {relative} {file.Length} {file.LastWriteTimeUtc:O}");
            })
            .Order(StringComparer.Ordinal),
    ];
}
