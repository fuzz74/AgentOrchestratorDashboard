using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace OrchDash.Core.Tests.Git;

// A repo built with real git under <temp>\<name>, with the worktrees in <temp>\<name>.worktrees (spec 6 "Git tests"):
// task x attempted, synced, merged and archived; task y attempted with an edited and an untracked file. Dispose deletes
// the whole <temp> folder.
public sealed class TempGitRepo : IDisposable
{
    private static readonly DateTimeOffset FirstTime = new(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(2));

    private int _clock;

    public TempGitRepo(string name = "repo")
    {
        Temp = Path.Combine(Path.GetTempPath(), "OrchDash.Git." + Guid.NewGuid().ToString("N"));
        Root = Path.Combine(Temp, name);
        Directory.CreateDirectory(Root);

        Git(Root, "init", "-b", "main");
        CommitFile(Root, "README.md", "readme\n", "Initial commit");

        Git(Root, "branch", "orch/integration");
        Git(Root, "worktree", "add", IntegrationWorktree, "orch/integration");

        Git(Root, "branch", "orch/task/x", "orch/integration");
        Git(Root, "worktree", "add", WorktreeOf("x"), "orch/task/x");
        CommitFile(WorktreeOf("x"), "x.txt", "x1\n", "orch(x): Task x", "Attempt 1.");
        CommitFile(IntegrationWorktree, "integration.txt", "i1\n", "Integration change");
        Git(WorktreeOf("x"), "merge", "--no-edit", "orch/integration");
        Git(IntegrationWorktree, "merge", "--no-ff", "--no-edit", "-m", "Merge task x: Task x", "orch/task/x");
        Git(Root, "branch", "orch/archive/x/20261006-120000000", "orch/task/x");

        Git(Root, "branch", "orch/task/y", "orch/integration");
        Git(Root, "worktree", "add", WorktreeOf("y"), "orch/task/y");
        CommitFile(WorktreeOf("y"), "y.txt", "y1\n", "orch(y): Task y", "Attempt 1.");
        File.AppendAllText(Path.Combine(WorktreeOf("y"), "y.txt"), "y2\n");
        File.WriteAllText(Path.Combine(WorktreeOf("y"), "new.txt"), "new\n");
    }

    public string Temp { get; }
    public string Root { get; }
    public string IntegrationWorktree => WorktreeOf("_integration");

    public string WorktreeOf(string id) => Path.Combine(Temp, Path.GetFileName(Root) + ".worktrees", id);

    public string Sha(string rev) => Git(Root, "rev-parse", rev);

    public string ShortSha(string rev) => Git(Root, "rev-parse", "--short", rev);

    // Writes a file in a worktree and commits it alone.
    public void CommitFile(string dir, string file, string text, string subject, string? body = null)
    {
        File.WriteAllText(Path.Combine(dir, file), text);
        Git(dir, "add", file);
        if (body is null)
            Git(dir, "commit", "-m", subject);
        else
            Git(dir, "commit", "-m", subject, "-m", body);
    }

    // Runs git in dir and returns its trimmed stdout; every call gets a later author and committer date.
    public string Git(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in (string[])
            ["-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false",
             "-c", "core.autocrlf=false", "-C", dir, .. args])
            info.ArgumentList.Add(arg);
        var date = FirstTime.AddMinutes(_clock++).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        info.Environment["GIT_AUTHOR_DATE"] = date;
        info.Environment["GIT_COMMITTER_DATE"] = date;
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(info)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr.Result}");
        return stdout.Trim();
    }

    public void Dispose()
    {
        // git makes its object files read-only, and a git process of a background read may still hold a file.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (!Directory.Exists(Temp))
                    return;
                foreach (var file in Directory.EnumerateFiles(Temp, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(Temp, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
    }
}
