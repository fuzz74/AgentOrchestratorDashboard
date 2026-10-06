# OrchDash

A fullscreen terminal dashboard for runs of the agent orchestrator. The orchestrator builds a project with headless
Claude Code or Copilot CLI agents (bootstrap, planner, workers, reviewers, resolvers) and logs everything under
`<repo>/.orchestrator/`. OrchDash reads that folder and shows the run's progress, its tasks, the agents that are
running, the progress log, and each session's conversation: the prompt, the model's responses, its tool calls and the
result. From the providers' own stores it adds what each agent's context window holds at every model call (Context
page) and the tokens, cost, premium requests, AIU, rate limits and lines changed per call, session, task and run (Usage
page). It works for finished runs and live ones; changes show up within 2 seconds.

## Usage

```text
OrchDash [repo]
```

From the source tree:

```powershell
dotnet run --project src/OrchDash -- <repo>
```

`repo` is any folder inside the repo; OrchDash uses the nearest folder at or above it that contains `.orchestrator`.
A relative path is resolved against the current directory. Without an argument the search starts at the current
directory.

| Exit code | Meaning |
| --- | --- |
| 0 | The user quit (`q`, a click on `quit`, or Ctrl+Q). |
| 1 | An unexpected error ended the app; its message is written to stderr after the screen is restored. |
| 2 | No `.orchestrator` folder at or above the start path: stderr shows `No .orchestrator folder at or above <path>` and the UI does not start. |

## Keys

Everything can be done with the keyboard and with the mouse: a click selects a row or opens it where Enter would, and
the wheel scrolls.

**Everywhere**

| Key | Action |
| --- | --- |
| `1`, `2`, `3`, `4` or a click on a tab | Show the Overview, Conversation, Context or Usage page |
| `p` or a click on `<n> problems` | Open the Problems pop-up (one line per problem found while reading) |
| `q` or a click on `quit` | Quit |
| Ctrl+Q | Quit, also while a pop-up is open |

The command bar on the last row lists the keys that work where the focus is.

**Pop-ups** (modal, 90 % of the screen)

| Key | Action |
| --- | --- |
| Up, Down, PageUp, PageDown, Home, End, wheel | Scroll |
| Escape, `q` or a click on `[X]` | Close |

**Overview page**: the run panel, the task table, the running sessions and the log. Each running session shows its
latest tool calls and, as its second line, `context <size>`: the context of its latest model call with known usage, with
the model's limit and share when one is known, for example `context 34.5k of 200.0k (17 %)`.

| Key | Action |
| --- | --- |
| Tab, Shift+Tab | Move to the next or previous panel |
| Up, Down, PageUp, PageDown, Home, End, wheel | Move the selection in the focused panel |
| Enter or a click | Run panel: every run and plan value. Task: its state, plan, prompt, summary, notes, error, feedback and sessions. Log entry: the whole message |
| Enter or a click on a running session | Open that session on the Conversation page |
| End (log) | Follow the newest log entry again |

**Conversation page**: the session list on the left, the selected session's entries on the right.

| Key | Action |
| --- | --- |
| Up, Down, PageUp, PageDown, Home, End, wheel | Move the selection in the focused list |
| Tab, Shift+Tab | Switch between the session list and the entries |
| Enter on a session | Move the focus to its entries |
| Enter or a click on an entry | Open the full data: the session's files, init and result; the whole prompt or text; a tool call's input, result and diff; the result's text, structured output and review issues |
| End (entries) | Select the newest entry and follow a running session again |

**Context page**: what the selected session's context window holds at each model call. The session list (with each
session's latest context size), a header (provider, role, task, model, calls, peak, the selected call's context and
`unavailable: <reasons>` when a store has nothing for the session), the chart `Context per call`, the call list (start
time, context, change from the previous call, output, thinking, stop reason), the make-up of the selected call by
category (system prompt, tool definitions, injected text, prompt, conversation, other) and the part list. Attempts of
the same session (a resumed worker) form one chain of calls. Tokens marked `~` or `est.` are estimates spread over the
parts by their characters.

| Key | Action |
| --- | --- |
| Up, Down, PageUp, PageDown, Home, wheel | Move the selection in the focused list |
| Tab, Shift+Tab | Move the focus between the session list, the call list and the part list |
| Enter on a session, or a click | Select the session; Enter moves the focus to its calls |
| Enter on a call, or a click | Select the call; Enter moves the focus to its parts |
| End (calls) or a click on the last call | Select the newest call and follow a running session's new calls again |
| Enter or a click on a part | Open the part: its text, a tool definition's description and schema, a tool call's input and result |
| A click on a segment of the make-up bar | Show the category's tokens and share in a tip; a mouse move, a key or a click elsewhere removes it |
| A click on a category line of the make-up | Explain the category: what it is, how it gets into the context, why it matters and how this page measures it |
| `s` | Open the system prompt, one section per block |
| `t` | Open the tool definitions: name, description and schema of each tool |

**Usage page**: the run panel (sessions, calls, input, cache read, cache write, output, thinking, cost, premium
requests, AIU and lines changed over the whole run), the latest rate limits, the CLI versions, the group table
(bootstrap, planner and one row per task) with the chart `Tokens per group`, and the sessions of the selected group. A
figure that no session knows shows as `-`.

| Key | Action |
| --- | --- |
| Up, Down, PageUp, PageDown, Home, End, wheel | Move the selection in the focused table; in the session table this also selects the session on the other pages |
| Tab, Shift+Tab | Switch between the group table and the session table |
| Enter on a group, or a click | Select the group; Enter moves the focus to its sessions |
| Enter or a click on a session | Open `Usage: <group> <role> #<attempt>`: the session's totals, one line per model call and the reasons for missing data |

## What it reads

Everything under `<repo>/.orchestrator/`:

- `tasks.json`: the plan (spec, branches, settings) and the tasks with their dependencies.
- `state.json`: each task's status, attempts, cost, summary, notes, error and feedback.
- `progress.md`: the progress log, which also gives the run's start, finish, provider and max parallel.
- `run.lock` and `stop-requested`: whether the run is still active and whether a stop was requested.
- `logs/`: one session per agent run, with `X.json` (the result), `X.json.prompt.md` (the prompt),
  `X.json.events.jsonl` (the event log, read as it grows) and `X.json.stderr`, plus the setup, acceptance and
  integration logs, which only feed the task detail column.

And, by each session's id, three stores the CLIs keep in the user profile folder:

- The Claude Code transcript, `%USERPROFILE%\.claude\projects\<folder>\<sessionId>.jsonl`: the system prompt, the
  tool definitions, the text injected into the context, the exact output and thinking tokens and stop reason per call,
  and the session's cost and lines changed.
- The Copilot session folder, `%USERPROFILE%\.copilot\session-state\<sessionId>\`: the system prompt and CLI version
  from `events.jsonl`, and from `workspace.yaml` the id of a session that is still running.
- The Copilot database, `%USERPROFILE%\.copilot\session-store.db`: the tokens, AIU, duration and finish reason of every
  model call.

The stores are internal formats of the CLIs, so all three are optional. What is missing shows as `unavailable` with a
reason (`session id not known yet`, `no transcript`, `no session folder`, `no database rows`) on the Context page and
in the Usage page's session pop-up, and as `-` in the figures; the rest of the dashboard works as before. A database
that cannot be found or read adds a line to Problems.

### Tested versions

OrchDash was made for Claude Code 2.1.285, Copilot CLI 1.0.91 and Copilot database schema 8. These numbers are in
`src/OrchDash.Core/Model/TestedVersions.cs`; change them there after checking a newer CLI. When a run was written by
another version, Problems gets a line such as `Claude Code 2.1.3: OrchDash was made for 2.1.285`, and the Usage page's
versions line shows that provider in the warning colour.

## Read-only

OrchDash never creates, changes, deletes or renames a file or folder. It opens files only for reading, with
`FileShare.ReadWrite | FileShare.Delete` so that the orchestrator and the CLIs can keep writing them, and it starts no
process and uses no network. It cannot stop a run.

The one exception is the Copilot database: OrchDash opens it read-only through SQLite
(`Mode=ReadOnly;Pooling=False;Default Timeout=1`) and runs only two `SELECT` statements, but SQLite itself may create or
update `session-store.db-shm` and `session-store.db-wal` next to it, as it does for every reader of a database in WAL
mode.

## Setup and tests

Requires the .NET 10 SDK on Windows 11.

```powershell
dotnet restore AgentOrchestratorDashboard.slnx
dotnet build AgentOrchestratorDashboard.slnx -warnaserror && dotnet test AgentOrchestratorDashboard.slnx --no-build
```

One module's tests, for example the app's:

```powershell
dotnet build AgentOrchestratorDashboard.slnx -warnaserror && dotnet test tests/OrchDash.Tests --no-build --filter "FullyQualifiedName~OrchDash.Tests.App" -- RunConfiguration.TreatNoTestsAsError=true
```

The tests run on copies of two real runs in `tests/Fixtures` (`claude-run` and `copilot-run`) and of the provider
stores they left (`tests/Fixtures/stores`, with `claude` and `copilot` standing for `.claude` and `.copilot`; see
`tests/Fixtures/README.md`). No test reads the real stores in the user profile folder. The UI tests run the app on an
in-memory terminal and save each page and pop-up as an SVG in `tests/OrchDash.Tests/bin/Debug/net10.0/frames/`, for
example `app-claude-overview.svg`, `app-copilot-conversation.svg`, `app-claude-context.svg`, `app-claude-usage.svg`,
`overview-context.svg`, `context.svg`, `context-part-popup.svg`, `context-system-popup.svg`,
`context-tools-popup.svg`, `usage.svg` and `usage-session-popup.svg`.

## Manual checks

1. `dotnet run --project src/OrchDash -- C:\Data\AI\TextKit` (a Copilot run) and
   `dotnet run --project src/OrchDash -- C:\Data\AI\AnsiDemo` (a Claude run): the header shows `Finished`, the task
   table and the log are filled, every session opens on the Conversation page, the Context and Usage pages show context
   sizes, make-up and usage for every session, the pop-ups open and close with the keys and the mouse, and `q` leaves
   the app with exit code 0 and the terminal as it was.
2. One live run: start OrchDash on a repo while the orchestrator runs. Tasks change state, running agents appear in
   the running panel with their latest tool calls and context size, the log follows new entries, a running session on
   the Conversation page follows its newest entry, and on the Context page a running agent gets context sizes while it
   runs (for Copilot too).
3. Open the SVG files in `tests/OrchDash.Tests/bin/Debug/net10.0/frames/` after a test run and look at each page and
   pop-up.
