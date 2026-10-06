using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace OrchDash.Core.Git;

/// <summary>Runs one read-only git command as <c>git --no-optional-locks -C &lt;dir&gt; &lt;args&gt;</c> without a shell (20.3).</summary>
internal sealed class GitRunner(string gitPath)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public GitRunResult Run(string dir, params string[] args)
    {
        var info = new ProcessStartInfo(gitPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.ArgumentList.Add("--no-optional-locks");
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(dir);
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Exception e) when (e is Win32Exception or FileNotFoundException)
        {
            return new GitRunResult(null, "", "", e.Message, CouldNotStart: true);
        }

        if (process is null)
            return new GitRunResult(null, "", "", "git did not start", CouldNotStart: true);

        using (process)
        {
            // Both streams are read at the same time so a large diff cannot fill a pipe and block git. Both are read
            // asynchronously so the timeout also holds while git keeps its output open.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var clock = Stopwatch.StartNew();
            if (!process.WaitForExit(Timeout) || !Task.WhenAll(stdout, stderr).Wait(Remaining(clock)))
            {
                Kill(process);
                return new GitRunResult(null, "", "", "timed out", CouldNotStart: false);
            }

            var exitCode = process.ExitCode;
            var error = stderr.Result;
            var problem = exitCode == 0 ? null : FirstLine(error) is { Length: > 0 } line ? line : $"exit code {exitCode}";
            return new GitRunResult(exitCode, stdout.Result, error, problem, CouldNotStart: false);
        }
    }

    private static TimeSpan Remaining(Stopwatch clock) =>
        clock.Elapsed < Timeout ? Timeout - clock.Elapsed : TimeSpan.Zero;

    private static string FirstLine(string text)
    {
        var end = text.IndexOf('\n');
        return (end < 0 ? text : text[..end]).TrimEnd('\r');
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // it has exited meanwhile
        }
    }
}
