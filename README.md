# OrchDash

A fullscreen terminal dashboard for runs of the agent orchestrator. The orchestrator builds a project with headless
Claude Code or Copilot CLI agents (bootstrap, planner, workers, reviewers, resolvers) and logs everything under
`<repo>/.orchestrator/`. OrchDash reads that folder and shows the run's progress, its tasks, the agents that are
running, the progress log, and each session's conversation: the prompt, the model's responses, its tool calls and the
result. It works for finished runs and live ones; changes show up within 2 seconds.

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
| `1`, `2` or a click on a tab | Show the Overview or the Conversation page |
| `p` or a click on `<n> problems` | Open the Problems pop-up (one line per problem found while reading) |
| `q` or a click on `quit` | Quit |
| Ctrl+Q | Quit, also while a pop-up is open |

The command bar on the last row lists the keys that work where the focus is.

**Pop-ups** (modal, 90 % of the screen)

| Key | Action |
| --- | --- |
| Up, Down, PageUp, PageDown, Home, End, wheel | Scroll |
| Escape, `q` or a click on `[X]` | Close |

**Overview page**: the run panel, the task table, the running sessions and the log.

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

## What it reads

Everything under `<repo>/.orchestrator/`:

- `tasks.json`: the plan (spec, branches, settings) and the tasks with their dependencies.
- `state.json`: each task's status, attempts, cost, summary, notes, error and feedback.
- `progress.md`: the progress log, which also gives the run's start, finish, provider and max parallel.
- `run.lock` and `stop-requested`: whether the run is still active and whether a stop was requested.
- `logs/`: one session per agent run, with `X.json` (the result), `X.json.prompt.md` (the prompt),
  `X.json.events.jsonl` (the event log, read as it grows) and `X.json.stderr`, plus the setup, acceptance and
  integration logs, which only feed the task detail column.

## Read-only

OrchDash never creates, changes, deletes or renames a file or folder. It opens files only for reading, with
`FileShare.ReadWrite | FileShare.Delete` so that the orchestrator can keep writing them, and it starts no process and
uses no network. It cannot stop a run.

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

The tests run on copies of two real runs in `tests/Fixtures` (`claude-run` and `copilot-run`, see
`tests/Fixtures/README.md`). The UI tests run the app on an in-memory terminal and save each page and pop-up as an SVG
in `tests/OrchDash.Tests/bin/Debug/net10.0/frames/`, for example `app-claude-overview.svg` and
`app-copilot-conversation.svg`.

## Manual checks

1. `dotnet run --project src/OrchDash -- C:\Data\AI\TextKit` (a Copilot run) and
   `dotnet run --project src/OrchDash -- C:\Data\AI\AnsiDemo` (a Claude run): the header shows `Finished`, the task
   table and the log are filled, every session opens on the Conversation page, the pop-ups open and close with the keys
   and the mouse, and `q` leaves the app with exit code 0 and the terminal as it was.
2. One live run: start OrchDash on a repo while the orchestrator runs. Tasks change state, running agents appear in
   the running panel with their latest tool calls, the log follows new entries, and a running session on the
   Conversation page follows its newest entry.
3. Open the SVG files in `tests/OrchDash.Tests/bin/Debug/net10.0/frames/` after a test run and look at each page and
   pop-up.
