using System.Globalization;
using OrchDash.Core.Model;
using OrchDash.Core.Store;
using static OrchDash.Core.Tests.Store.RunData;

namespace OrchDash.Core.Tests.Store;

// A temp run for the RunStore tests of spec 14: a Claude worker (alpha, id s1, with a rate limit) and a Copilot worker
// (beta, no id in its log, with a sent prompt and a checkpoint), a reader problem, and fakes of the provider stores.
// The finder gives beta CopilotId; the other fakes return nothing until a test sets their Next.
public sealed class ProviderRun : IDisposable
{
    public const string CopilotId = "p-1";
    public const string ReaderProblem = "state.json: in use";

    public ProviderRun()
    {
        Claude = Repo.Session("alpha/20261003-120000/attempt-1-worker.json", "alpha", promptWrittenAt: At(12, 0, 5));
        Copilot = Repo.Session("beta/20261003-120000/attempt-1-worker.json", "beta", promptWrittenAt: At(12, 0, 6));
        Repo.Append(Claude, ClaudeLine("hello", At(12, 0, 10)) + "\n" + RateLimitLine(0.2) + "\n");
        Repo.Append(Copilot, CopilotLine("hi", At(12, 0, 20)) + "\n" + SentPromptLine("Build beta.") + "\n"
            + CheckpointLine("view", "tone_and_style") + "\n");
        Sessions = [Claude, Copilot];
        Reader = new FakeRunFolderReader(() => Data(sessions: Sessions, problems: [ReaderProblem]));
        Finder.Next = (name, _, _, _) => name == "orch:beta" ? CopilotId : null;
    }

    public TempRepo Repo { get; } = new();
    public FakeParserFactory Factory { get; } = new();
    public FakeSessionStore Transcripts { get; } = new();
    public FakeSessionStore Folders { get; } = new();
    public FakeSessionIdFinder Finder { get; } = new();
    public FakeUsageReader Usage { get; } = new();
    public FakeRunFolderReader Reader { get; }
    public SessionFiles Claude { get; }
    public SessionFiles Copilot { get; }
    public SessionFiles[] Sessions { get; set; }   // what the reader returns

    public ProviderStores Stores => new(Transcripts, Folders, Finder, Usage);

    public void Dispose() => Repo.Dispose();

    public RunStore NewStore(ProviderStores? stores) => new(Repo.RepoPath, Reader, Factory.Create, stores: stores);

    public FakeSessionParser ParserOf(Provider provider) => Factory.Parsers.Single(p => p.Provider == provider);

    public static Session SessionOf(RunStore store, SessionFiles files) =>
        store.Current.Sessions.Single(s => s.Files.Key == files.Key);

    public static string CopilotLine(string text, DateTimeOffset time) =>
        $$"""{"data":{},"text":"{{text}}","ts":"{{time:O}}"}""";

    public static string SentPromptLine(string prompt) => $$"""{"sent_prompt":"{{prompt}}"}""";

    public static string RateLimitLine(double fiveHourUsed) =>
        string.Create(CultureInfo.InvariantCulture, $$"""{"rate_limit":{{fiveHourUsed}}}""");

    // A checkpoint with the tools powershell and `tool` and the segments identity and `segment`.
    public static string CheckpointLine(string tool, string segment) =>
        $$$"""{"checkpoint":{"tools":["powershell","{{{tool}}}"],"segments":[{"name":"identity","tokens":120},{"name":"{{{segment}}}","tokens":227}]}}""";
}
