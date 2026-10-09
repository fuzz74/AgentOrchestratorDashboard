# OrchDash

A fullscreen terminal dashboard for runs of the agent orchestrator. The orchestrator builds a project with headless
Claude Code or Copilot CLI agents (bootstrap, planner, workers, reviewers, resolvers) and logs everything under
`<repo>/.orchestrator/`. OrchDash reads that folder and shows the run's progress, its tasks, the agents that are
running, the progress log, and each session's conversation: the prompt, the model's responses, its tool calls and the
result. From the providers' own stores it adds what each agent's context window holds at every model call (Context
page) and the tokens, cost, premium requests, AIU, rate limits and lines changed per call, session, task and run (Usage
page). It draws the task graph by waves (Graph page), shows each task's git state: branch, worktree, commits and diff
against the integration branch (Git page), the PID, uptime, CPU and memory of every running agent process (Overview
page), and the output of every setup, acceptance and integration command (Commands page). The Timeline page merges
the events of all agents and of the orchestrator's own log into one list, one line per event. Eight pages in all.

Workers and the planner can start sub-agents. Every page that shows tasks or sessions shows them too: as child rows
under their task or session, or as a path such as `alpha worker #1 › Survey billing module` (see "Sub-agents").

Replay shows the whole dashboard as the run was at a chosen time: the time bar under the header moves that time with
the arrow keys or a click, and every page shows the run at it. The run picker (`r`) lists the repo's current run and its
archived runs under `<repo>.runs` and switches the dashboard to one. It works for finished runs and live ones; changes
show up within 2 seconds, process figures within 4 and git changes within 8.

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

`repo` may also be an archived run, `<repo>.runs\<stamp>`: that folder holds its own `.orchestrator`, so OrchDash opens
it like any run. The header then shows `<repo> · <stamp>`, the sessions' work dirs are the task worktrees
`<repo>.worktrees\<task>` (and `<repo>` for the bootstrap and the planner), and git reads `<repo>`.

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
| `1` to `8` or a click on a tab | Show the Overview, Conversation, Context, Usage, Graph, Git, Commands or Timeline page |
| Left | Step back: replay at the previous event's time |
| Right | Step forward: replay at the next event's time, or return to live after the last one |
| Shift+Left, Shift+Right | Replay one minute earlier or later (Shift+Right returns to live at the end) |
| A click on a cell of the time bar | Replay at that cell's time; the last cell returns to live |
| Escape (while replaying) or a click on the bar's state text | `Live`: return to live |
| `r` or a click on the header's run text | Open the `Runs` dialog |
| `p` or a click on `<n> problems` | Open the Problems pop-up (one line per problem found while reading) |
| `q` or a click on `quit` | Quit |
| Ctrl+Q | Quit, also while a pop-up is open |

The command bar on the last row lists the keys that work where the focus is: Left as `Step`, Escape as `Live` while
replaying and `r` as `Runs`; Right, Shift+Left and Shift+Right have no entry. Left, Right and Escape act on the time
only while no pop-up or dialog is open; Escape closes an open one first.

**Pop-ups** (modal, 90 % of the screen)

| Key | Action |
| --- | --- |
| Up, Down, PageUp, PageDown, Home, End, wheel | Scroll |
| Escape, `q` or a click on `[X]` | Close |

**Overview page**: the run panel, the task table, the running sessions and the log. Each running session shows its
latest tool calls and, as its second line, `context <size>`: the context of its latest model call with known usage, with
the model's limit and share when one is known, for example `context 34.5k of 200.0k (17 %)`. Under that line comes the
agent's process: `pid 4242 · up 20m00s · cpu 12 % · mem 367.0 MB` (the PID, the time since it started, its share of all
CPU cores since the last sample and its working set; `-` for an unknown figure), or `no process` in the warning colour
when no `claude.exe` or `copilot.exe` process belongs to the session's task and role. Before the first process sample
there is no such line. The task pop-up has a `Processes` section after `Sessions` with two lines per process of the
task: `pid <pid> · <role> · started <time> · cpu <n %> · mem <bytes>` and its command line.

| Key | Action |
| --- | --- |
| Tab, Shift+Tab | Move to the next or previous panel |
| Up, Down, PageUp, PageDown, Home, End, wheel | Move the selection in the focused panel |
| Enter or a click | Run panel: every run and plan value. Task: its state, plan, prompt, summary, notes, error, feedback and sessions. Log entry: the whole message |
| Enter or a click on a sub-agent's child row in the task table | Open that sub-agent on the Conversation page |
| Enter or a click on a running session | Open that session on the Conversation page |
| End (log) | Follow the newest log entry again |

**Conversation page**: the session list on the left, the selected session's entries on the right.

| Key | Action |
| --- | --- |
| Up, Down, PageUp, PageDown, Home, End, wheel | Move the selection in the focused list; in the session list a sub-agent's child row selects that sub-agent |
| Tab, Shift+Tab | Switch between the session list and the entries |
| Enter on a session or a sub-agent | Move the focus to its entries |
| Enter or a click on an entry | Open the full data: the session's files, init and result; the whole prompt or text; a tool call's input, result and diff; the result's text, structured output and review issues; a sub-agent's header, prompt and report |
| Enter or a click on a `sub-agent` entry | Select the sub-agent that call started and move the focus to its entries |
| End (entries) | Select the newest entry and follow a running session or sub-agent again |

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
| Enter on a session or a sub-agent, or a click | Select it; Enter moves the focus to its calls |
| Enter on a call, or a click | Select the call; Enter moves the focus to its parts |
| End (calls) or a click on the last call | Select the newest call and follow a running session's or sub-agent's new calls again |
| Enter or a click on a part | Open the part: its text, a tool definition's description and schema, a tool call's input and result |
| A click on a segment of the make-up bar | Show the category's tokens and share in a tip; a mouse move, a key or a click elsewhere removes it |
| A click on a category line of the make-up | Explain the category: what it is, how it gets into the context, why it matters and how this page measures it |
| `s` | Open the system prompt, one section per block; for a sub-agent the one in its transcript |
| `t` | Open the tool definitions: name, description and schema of each tool; for a sub-agent the ones in its transcript |

**Usage page**: the run panel (sessions, calls, input, cache read, cache write, output, thinking, cost, premium
requests, AIU and lines changed over the whole run), the latest rate limits, the CLI versions, the group table
(bootstrap, planner and one row per task) with the chart `Tokens per group`, and the sessions of the selected group. A
figure that no session knows shows as `-`.

| Key | Action |
| --- | --- |
| Up, Down, PageUp, PageDown, Home, End, wheel | Move the selection in the focused table; in the session table this also selects the session or sub-agent on the other pages |
| Tab, Shift+Tab | Switch between the group table and the session table |
| Enter on a group, or a click | Select the group; Enter moves the focus to its sessions |
| Enter or a click on a session | Open `Usage: <group> <role> #<attempt>`: the session's totals, one line per model call of its own, one `Sub-agent <name>` section per sub-agent with its calls, and the reasons for missing data |
| Enter or a click on a sub-agent's child row | Open `Usage: <path>`, for example `Usage: alpha worker #1 › Survey the parser module`: the sub-agent's totals and one line per model call |

**Graph page**: one column per wave (`W1`, `W2`, ...) with one card per task: its status icon and id. The selected task
is marked `●`, its direct and indirect dependencies `◂` and its dependents `▸`; lines connect it to its direct
dependencies and dependents. The `Task` panel below shows the selected task's deps and dependents with their status,
its owned paths, the other tasks whose owned paths overlap, its state, attempts and cost, why it waits (`detail`) and
its number of sessions.

| Key | Action |
| --- | --- |
| Up, Down | Select the previous or next card in the same column, past the sub-agent lines |
| Tab, Shift+Tab | Select the nearest card in the next or previous column (after the last comes the first) |
| A click on a card | Select it |
| Enter or a click on the selected card | Open the task pop-up, as on the Overview page |
| A click on a sub-agent line under a card | Open that sub-agent on the Conversation page |
| `c` | Open the task's last session on the Conversation page |
| Wheel | Scroll |

**Git page**: a header with the integration and base branches and their tips, the number of worktrees and archive
branches and the time of the last git read (`git unavailable: <reason>` below it in the warning colour when git failed);
the task table (status, id, `@<tip>` of the task branch or `archived`, `clean` or `<n> uncommitted`, attempt commits
and syncs, committed files with lines added and removed, merge commit); and for the selected task its commits, oldest
first, and its changed files: the committed ones, then the uncommitted lines of `git status`.

| Key | Action |
| --- | --- |
| Up, Down, PageUp, PageDown, Home, End, wheel | Move the selection in the focused list |
| Tab, Shift+Tab | Move the focus between the task table, the commits and the files |
| A click on a task | Select it |
| Enter or a click on the selected task | Open `Diff: <id>`: the committed and the uncommitted diff |
| Enter or a click on a commit | Open `<short sha> <subject>`: sha, time, kind, attempt, subject and body |
| Enter or a click on a file | Open `Diff: <id> <path>`: that file's part of the diff |

**Commands page**: the list of command logs on the left, newest first: outcome icon (`✔` passed, `✖` failed, `▶`
running, `·` unknown), time, task or `bootstrap`, kind (setup, acceptance, integration setup, integration check,
bootstrap setup, bootstrap check), `#<attempt>` and size. On the right the selected log: a header with the command, the
outcome with its exit code, the log's path under `logs/` and its size, then its output and, when there is any,
`── stderr ──` and the error output. While a command runs, the output follows its end as it grows.

| Key | Action |
| --- | --- |
| Up, Down, PageUp, PageDown, Home, wheel | Move the selection in the list, or scroll the output; scrolling up stops following |
| Tab, Shift+Tab | Switch between the list and the output |
| A click on a log | Select it |
| Enter or a click on the selected log | Open `<kind> <task> #<attempt>` with the command, outcome, output and stderr |
| End (output) | Jump to the end and follow a running command again |

**Timeline page**: a filter row, `[o] orchestrator  [a] calls  [u] tools  [x] text  [e] prompt/result  task: all  ▶
replay here` (active kinds in the accent colour, hidden ones muted), and below it one row per event, oldest first:
time, source (`orchestrator`, or `<task> <role> <attempt>` such as `alpha worker #1`, followed by ` › <name>` for a
sub-agent's event), kind (`orch`, `prompt`, `call`, `tool`, `text`, `result`) and text. The events are the
orchestrator's progress entries (without the activity lines), and for every session its prompt, its model calls, its
tool calls, its texts and its result, and the start, calls, tool calls, texts and end of each of its sub-agents. While
the run has sub-agents, the filter row shows `[s] sub-agents` after `[e] prompt/result`. `No events yet` while there is
none.

| Key | Action |
| --- | --- |
| `o`, `a`, `u`, `x`, `e` or a click on the label | Show or hide orchestrator entries, model calls, tool calls, texts, or prompts and results |
| `s` or a click on `[s] sub-agents` | Show or hide every event of a sub-agent (only while the run has sub-agents) |
| `f` or a click on `task:` | Task filter: only the selected event's task and the `run` events (`task: <task>`), or all again (`task: all`) |
| Up, Down, PageUp, PageDown, Home, wheel | Move the selection; Up stops following |
| End or a click on the last row | Select the last row and follow new events while the run is active |
| Enter or a click on the selected row | Open the event: the log entry, the prompt, the call's figures, the tool call's input, result and diff, the text, or the result |
| `c` | Open the event's session on the Conversation page (for a sub-agent's event: that sub-agent; for a task's orchestrator entry: the task's last session) |
| `t` or a click on `▶ replay here` | Replay at the selected event's time |

## Replay

The row under the header is the time bar: `<start> <50 cells> <end>  <state>`. `start` is the time of the first event of
the timeline, `end` the later of the last event and the run's finish (the clock while the run is active). The cells
before the cursor are `━`, the cursor is `●` and the cells after it `─`; while live the cursor is the last cell. The
state is `live` (green), or `replay <clock> · <k> of <n> events · git live`, where k counts the events up to the replay
time and n all events. While a run switch loads, ` · loading <run>` follows, and ` · <problem>` when a switch or listing
failed. `no events yet` stands in for the bar while the run has no events.

While replaying, every page shows the run at that time, rebuilt from the live data by one pure function: the progress
log cut at the time, the run info (phase, start, finish) and the task states, attempts, details, costs, summaries and
feedback rebuilt from it; the sessions that had started, with their tool calls, texts, model calls and results cut at
the time and their state derived again; their sub-agents that had started by then, cut the same way, where one that
finished later shows as running, without its finish and report (as aborted when its session was no longer running at
that time); and the command logs written by then. Git stays live, which the state text marks with `git live`; the
process list is empty. New data from the live run keeps arriving underneath, and Escape returns to it. Switching runs
ends replay.

## Run picker

`r`, or a click on the run name in the header, opens the modal `Runs` dialog. It lists the repo's own run as `current`
(when its `.orchestrator` holds `tasks.json`, `state.json` or `progress.md`) and then every archived run, newest stamp
first. The archive layout is the orchestrator's: `<repo>.runs\<stamp>\.orchestrator`, beside the repo. Columns: `●` for
the run shown, run (`current` or the stamp), spec, started, finished, tasks (`<done> done · <failed> failed · <n>
tasks`) and provider (or the model when the provider is unknown). For each run it reads `tasks.json` (the spec file
name and the number of tasks), `state.json` (the done and failed tasks) and `progress.md` (start, finish, provider and
the planning model). A file that cannot be read or parsed shows as ` · <file>: <reason>` in the warning colour at the
end of its row; a `.runs` folder that cannot be listed adds one last warning row. `loading…` stands in until the listing
is done; it runs on its own thread, and the dialog refreshes when a new listing arrives.

| Key | Action |
| --- | --- |
| Up, Down, wheel | Move the selection |
| Enter or a click on a run | Switch to it (only close for the run shown) |
| Escape, `q` or a click on `[X]` | Close |

A switch runs in the background: the dashboard keeps showing the old run, with `loading <stamp>` in the time bar, until
the new run's first read is done; then the old run's reader stops. When the new run cannot be opened, the old one stays
and the time bar shows why.

## Sub-agents

A worker or the planner can hand part of its work to a sub-agent: Claude Code starts one with its `Agent` (or `Task`)
tool, Copilot CLI with its `task` tool. A sub-agent runs inside its agent's process, and its events go into the agent's
own `events.jsonl`, marked with the id of the tool call that started it (Claude's `parent_tool_use_id`) or with its
own id (Copilot's `agentId`). OrchDash keeps each sub-agent's model calls, tool calls, texts, state and report apart
from its agent's: a sub-agent's answer never becomes the agent's result, its calls are not part of the agent's context
chain, and a Claude session that waits for its background sub-agents shows no result until its last `result` event. A
sub-agent runs until its end event (Claude's `task_notification`, Copilot's `subagent.completed` or `subagent.failed`)
or, in the foreground, until the tool call that started it returns; it has then succeeded or failed, and its report is
the final answer it handed back. One that still runs when its session has ended is aborted. Its name is its
description, cut to 32 characters (`Check the public API surface of…`). Copilot sub-agents can start sub-agents of their
own; Claude's cannot.

Where a page lists tasks or sessions, the sub-agents show as child rows below them, drawn as a tree (`├`, `└`, `│`)
with the icon of their state (`▶` running, `✔` succeeded, `✖` failed, `◌` aborted) and their name, in the state's
colour. In flat lists a sub-agent shows as a path: its session's `<task> <role> <attempt>`, then ` › <name>` for each
sub-agent down to it, such as `alpha worker #1 › Survey billing module` or `planner #1 › Map the repo › Read the spec`.

- **Overview.** Under each task row, one child row per sub-agent of the task's sessions, starting at the id column:
  `├✔ Survey the parser module · worker #1 · 2 tool calls · 41s` (its session's role and attempt, its own tool calls
  and how long it ran). A running session's block takes its tool-call count, its context line and its last 5 items from
  the agent's own events, and ends with one line per sub-agent, `└▶ Survey CLI flags · 1 tool call · context 3.6k`,
  then, while it runs, its latest item. A log entry of a sub-agent (one the orchestrator wrote as
  `↳ [<name>] <message>`) reads `11:59:30 [planner › Map the repo] Glob **/*`, and its pop-up's heading shows the same
  path. The task pop-up's `Sessions` section lists each session's sub-agents below it, with type, state and model:
  `├ Survey the parser module · Explore · Succeeded · claude-haiku-4-5`.
- **Graph.** Under each card, one line per sub-agent of the task's sessions, `├✔ Survey the parser m…` (a name longer
  than 20 characters is cut to 19 and `…`). The column widens to fit these lines, and the lines between tasks still end
  at the cards. The `Task` panel's last line reads `sessions: <n> · sub-agents: <m> (<k> running)`.
- **Conversation.** The session list shows the child rows below each session, with type, model, how long it ran and
  its own tool calls: `├✔ Survey the parser module · Explore · claude-haiku-4-5 · 41s · 2 tool calls`. A session's
  entries, its `call N` separators and its tool-call count are the agent's own; the tool call that started a sub-agent
  reads `✔ sub-agent Survey the parser module · Explore · succeeded`, with the first line of its report below. A
  selected sub-agent shows its own entries: a header, `sub-agent alpha worker #1 › Survey the parser module` over
  `Explore · claude-haiku-4-5 · ✔ succeeded · started 12:00:17 · 41s · 2 tool calls`, whose pop-up section `Sub-agent`
  lists its id, tool call id, parent, type, model, background, description, start and finish; its `prompt`; its own
  items, with `call N` counting its own calls and a `sub-agent` entry for each sub-agent it started; and, once it has
  finished, `result <state>` with the start of its report.
- **Context.** The session list shows the child rows with the context of each sub-agent's latest call,
  `├✔ Survey the parser module 5.4k`. A session's calls, chart, peak, steps and make-up count only the agent's own
  calls and items, across its chain. A selected sub-agent shows its own calls (no chain) under the header
  `Claude · sub-agent · alpha worker #1 › Survey the parser module · claude-haiku-4-5 · 2 calls · peak 5.4k`, then the
  selected call's `context <size> of <limit>`, where the limit, when known, is the largest context window that a session
  of the run with the same model reported. Its make-up takes the system prompt, the tool definitions and the injected
  text from its sub-agent transcript (Claude only; without one it has none), the part `prompt` is its prompt from its
  first call on, and the conversation is its items.
- **Usage.** The session table lists each session's sub-agents below it: `├✔ Survey the parser module` across the role
  and attempt columns, then the sub-agent's model, calls, peak, input, cache read, cache write, output and AIU; cost and
  lines are `-`. A session row still counts its sub-agents' calls, but its peak is that of its own calls. The run panel
  adds `sub-agents <n>`; the group table adds a last column `Sub`, `<n> · <share>` (the group's sub-agents and their
  share of its tokens, `-` for a group without any), which shows in full from about 172 columns; and the chart
  `Tokens per group` adds a bar `└ sub-agents` under each group that has them.
- **Timeline.** A sub-agent's events have its path as their source. Its calls, tool calls and texts are `call`, `tool`
  and `text` rows, with `call N` counting its own calls; its start is a `prompt` row,
  `sub-agent started · Explore · <n> chars`, and its end a `result` row, `result succeeded · <its report's first line>`
  in its state's colour. An orchestrator entry of a sub-agent reads `[planner › Map the repo] <message>`. The task
  filter keeps a sub-agent's events with its session's task, and `[s] sub-agents` shows or hides them all.

The Conversation, Context and Usage pages share the selection: a sub-agent selected on one is selected on the others.
A run without sub-agents looks as before: no child rows, paths, `sub-agents` lines, `Sub` column or `[s]` toggle.

## What it reads

Everything under `<repo>/.orchestrator/`:

- `tasks.json`: the plan (spec, branches, settings) and the tasks with their dependencies.
- `state.json`: each task's status, attempts, cost, summary, notes, error and feedback.
- `progress.md`: the progress log, which also gives the run's start, finish, provider and max parallel. An entry whose
  message starts with `↳ [<name>] ` comes from the sub-agent `<name>` of the entry's source.
- `run.lock` and `stop-requested`: whether the run is still active and whether a stop was requested.
- `logs/`: one session per agent run, with `X.json` (the result), `X.json.prompt.md` (the prompt),
  `X.json.events.jsonl` (the event log, read as it grows, which holds the events of the agent's sub-agents too) and
  `X.json.stderr`, plus the setup, acceptance and integration command logs (`<task>/<start>/setup.log`,
  `<task>/<start>/attempt-<n>-acceptance.log`, `<task>-integration-setup.log`, `<task>-integration-check.log`,
  `bootstrap-<start>/attempt-<n>-setup.log` and `bootstrap-<start>/attempt-<n>-integration-check.log`, each with its
  `.stderr`), listed every poll and read again only when they changed.

Every read that finds a change publishes a new snapshot of the run; the UI takes each new snapshot instance it finds on
its next tick (not only one with a higher version number), so a switch to another run, whose versions start again,
shows at once. The `Runs` dialog also reads `tasks.json`, `state.json` and `progress.md` of the archived runs under
`<repo>.runs`, only when it opens.

And, by each session's id, three stores the CLIs keep in the user profile folder:

- The Claude Code transcript, `%USERPROFILE%\.claude\projects\<folder>\<sessionId>.jsonl`: the system prompt, the
  tool definitions, the text injected into the context, the exact output and thinking tokens and stop reason per call,
  and the session's cost and lines changed. The same for each sub-agent comes from its own transcript,
  `%USERPROFILE%\.claude\projects\<folder>\<sessionId>\subagents\agent-<x>.jsonl`, whose `agent-<x>.meta.json` names
  in `toolUseId` the tool call that started it; a transcript without a readable meta file is skipped, and each one is
  read again only when its length or last write time changed.
- The Copilot session folder, `%USERPROFILE%\.copilot\session-state\<sessionId>\`: the system prompt and CLI version
  from `events.jsonl` (a `system.message` with an `agentId` is a sub-agent's and is skipped), and from `workspace.yaml`
  the id of a session that is still running.
- The Copilot database, `%USERPROFILE%\.copilot\session-store.db`: the tokens, AIU, duration and finish reason of every
  model call. The columns `agent_id` and `parent_tool_call_id` of `assistant_usage_events` tell which agent a row
  belongs to: the sub-agent with that `agent_id`, else the sub-agent started by the tool call `parent_tool_call_id`,
  else, when both are empty, the agent itself. A row of a sub-agent that the event log does not know is left out, and
  each agent's rows match its own calls in order.

The stores are internal formats of the CLIs, so all three are optional. What is missing shows as `unavailable` with a
reason (`session id not known yet`, `no transcript`, `no session folder`, `no database rows`) on the Context page and
in the Usage page's session pop-up, and as `-` in the figures, a sub-agent's too; the rest of the dashboard works as
before. A database that cannot be found or read adds a line to Problems.

Git, read-only, on its own thread at most every 5 seconds, so a slow `git status` never delays the dashboard. Every
command is `git --no-optional-locks -C <folder> ...`, so git takes no lock and does not refresh the index:
`rev-parse --show-toplevel`, `worktree list --porcelain`, `for-each-ref` over `refs/heads/`, `log --all` filtered to the
orchestrator's commits, and per task `status --porcelain` in its worktree and `diff` (with `--numstat` for the counts)
of its uncommitted changes and of its branch against the integration branch, or of its merge commit. A task's diffs are
read again only when its tip, merge commit or worktree status changed. For an archived run under
`<repo>.runs\<timestamp>`, which is not a work tree, git reads the repo `<repo>` beside it. When git is missing or a
command fails, the Git page shows `git unavailable: <reason>` and Problems gets the command and its error; when git
cannot be started at all, OrchDash tries again after 60 seconds.

The agent processes, every 2 seconds, with one WMI query:
`SELECT ProcessId, Name, CommandLine, CreationDate, WorkingSetSize, UserModeTime, KernelModeTime FROM Win32_Process
WHERE Name = 'claude.exe' OR Name = 'copilot.exe'`. A process belongs to a task and role by the `--name orch:<task>`
(or the `--resume <session id>`) on its command line. When the query fails, the last sample stays and Problems gets
`processes: <reason>`.

### Tested versions

OrchDash was made for Claude Code 2.1.285, Copilot CLI 1.0.91 and Copilot database schema 8. These numbers are in
`src/OrchDash.Core/Model/TestedVersions.cs`; change them there after checking a newer CLI. When a run was written by
another version, Problems gets a line such as `Claude Code 2.1.3: OrchDash was made for 2.1.285`, and the Usage page's
versions line shows that provider in the warning colour.

## Read-only

OrchDash never creates, changes, deletes or renames a file or folder. It opens files only for reading, with
`FileShare.ReadWrite | FileShare.Delete` so that the orchestrator and the CLIs can keep writing them; that holds for
the run picker's reads of the archived runs and for the sub-agent transcripts and their meta files too. It starts no
process except `git`, and that only with the subcommands and arguments listed under "What it reads", each with
`--no-optional-locks`; it runs no WMI operation other than the one process query; and it uses no network. It cannot
stop a run.

The one exception is the Copilot database: OrchDash opens it read-only through SQLite
(`Mode=ReadOnly;Pooling=False;Default Timeout=1`) and runs only two `SELECT` statements (the schema version, and the
usage rows with their `agent_id` and `parent_tool_call_id`), but SQLite itself may create or update
`session-store.db-shm` and `session-store.db-wal` next to it, as it does for every reader of a database in WAL mode.

## Setup and tests

Requires the .NET 10 SDK on Windows 11 and `git` 2.15 or newer on `PATH` (for `--no-optional-locks`). Besides
`XenoAtom.Terminal.UI`, `Microsoft.Data.Sqlite` and `System.Text.Json`, OrchDash uses one package, `System.Management`,
for the process query; its API is Windows-only, so every assembly is marked Windows-only.

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
`tests/Fixtures/README.md`). No test reads the real stores in the user profile folder. The fixtures lie inside this
repo's work tree, so the tests on them read the command logs but run neither git nor the process query; the git reader
is tested on temp repos that the tests build with `git`. The UI tests run the app on an in-memory terminal and save each
page and pop-up as an SVG in `tests/OrchDash.Tests/bin/Debug/net10.0/frames/`, for example `app-claude-overview.svg`,
`app-copilot-conversation.svg`, `app-claude-context.svg`, `app-claude-usage.svg`, `app-claude-graph.svg`,
`app-claude-git.svg`, `app-claude-commands.svg`, `app-claude-timeline.svg`, `overview-context.svg`,
`overview-process.svg`, `context.svg`, `context-part-popup.svg`, `context-system-popup.svg`, `context-tools-popup.svg`,
`usage.svg`, `usage-session-popup.svg`, `graph.svg`, `graph-popup.svg`, `git.svg`, `git-diff-popup.svg`,
`commands.svg`, `commands-popup.svg`, `timeline.svg`, `timeline-filtered.svg`, `timeline-popup.svg`,
`shell-timebar.svg`, `shell-replay.svg` and `shell-runs.svg`. The run host's tests switch between copies of the two
fixture runs laid out as a repo and an archived run in a temp folder.

The page tests for sub-agents run on a sample run whose alpha and beta workers and planner have sub-agents, and save
`overview-subagents.svg`, `graph-subagents.svg`, `conversation-subagent.svg`, `context-subagent.svg`,
`usage-subagents.svg` and `timeline-subagents.svg`. The parsers and stores are tested on synthetic event lines, temp
sub-agent transcripts and temp databases with `agent_id` rows. An end-to-end test runs the app on a temp repo with a
Claude worker and a Copilot planner that start sub-agents, a Claude sub-agent transcript and a Copilot database with
`agent_id` rows, and saves `app-subagents-overview.svg`, `app-subagents-graph.svg`, `app-subagents-conversation.svg`,
`app-subagents-context.svg`, `app-subagents-usage.svg` and `app-subagents-timeline.svg`.

## Manual checks

1. `dotnet run --project src/OrchDash -- C:\Data\AI\TextKit` (a Copilot run) and
   `dotnet run --project src/OrchDash -- C:\Data\AI\AnsiDemo` (a Claude run): the header shows `Finished`, the task
   table and the log are filled, every session opens on the Conversation page, the Context and Usage pages show context
   sizes, make-up and usage for every session, the pop-ups open and close with the keys and the mouse, and `q` leaves
   the app with exit code 0 and the terminal as it was.
2. One live run: start OrchDash on a repo while the orchestrator runs. Tasks change state, running agents appear in
   the running panel with their latest tool calls and context size, the log follows new entries, a running session on
   the Conversation page follows its newest entry, and on the Context page a running agent gets context sizes while it
   runs (for Copilot too). Each running block on the Overview page shows its agent's pid, uptime, CPU and memory, which
   change as it works, and the task pop-up lists the task's processes; the Commands page follows a running acceptance
   log as it grows, and End follows it again after scrolling up.
3. The part 2 run of this repo (now archived under `C:\Data\AI\AgentOrchestratorDashboard.runs`; open it with `r` or
   as the start argument): the Graph page shows its waves, the Git page shows the 17 task branches and worktrees with their commits, files and
   diffs, and the Commands page lists its setup, acceptance and integration logs with their outcomes and output.
4. `dotnet run --project src/OrchDash -- C:\Data\AI\AgentOrchestratorDashboard`: its `.orchestrator` holds only
   `project.json`, so the header says `NotStarted`. `r` lists the four archived runs under
   `C:\Data\AI\AgentOrchestratorDashboard.runs`, one of them interrupted. Switch to `20261006-195310`, read the
   Timeline page, step back through the run with Left, Shift+Left and clicks on the time bar, and return to live with
   Escape.
5. `dotnet run --project src/OrchDash -- C:\Data\AI\AgentOrchestratorDashboard.runs\20261006-195310` directly: the
   header shows `AgentOrchestratorDashboard · 20261006-195310`.
6. `dotnet run --project src/OrchDash -- C:\Data\AI\TextKit`: replay through `count`'s syncs (Running with mode `sync`,
   then `resolver (attempt 1)`, then Done with 2 sync runs).
7. One live run: step back while agents run, and return to live with Escape; the dashboard then shows the newest state.
8. One Claude run and one Copilot run in which workers and the planner use sub-agents: every page shows the child rows
   or paths; Copilot sub-agent calls get database figures (so `agent_id` equals the event log's `agentId`); Claude
   sub-agent calls get transcript figures from `<sessionId>\subagents\`; a background Claude sub-agent finishes on its
   `task_notification`. `C:\Data\AI\TextKit`, `C:\Data\AI\AnsiDemo` and the archived runs look as before.
9. Open the SVG files in `tests/OrchDash.Tests/bin/Debug/net10.0/frames/` after a test run and look at each page and
   pop-up.
