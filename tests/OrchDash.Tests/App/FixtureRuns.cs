using System.Globalization;
using OrchDash.App;
using OrchDash.Core.Model;

namespace OrchDash.Tests.App;

/// <summary>The fixture repos in the test output, and helpers for the end-to-end tests on them.</summary>
internal static class FixtureRuns
{
    public static string ClaudeRepo { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures", "claude-run");

    public static string CopilotRepo { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures", "copilot-run");

    /// <summary>The snapshot of a first <c>Poll()</c> of a store with the real reader and parsers.</summary>
    public static RunSnapshot Poll(string repo)
    {
        using var store = AppRunner.CreateStore(repo);
        store.Poll();
        return store.Current;
    }

    /// <summary>
    /// Every folder below <paramref name="folder"/>, and every file with its size and last write time, sorted, so that
    /// two listings are equal only when nothing was created, changed, deleted or renamed (N.1).
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
