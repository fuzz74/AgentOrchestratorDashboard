# Test fixtures

Copies of two real orchestrator runs that the reader, parser and end-to-end tests use. Both test
projects copy everything under this folder to `<test output>/Fixtures/`; the tests find the runs
through `OrchDash.Core.Tests.Fixtures.FixturePaths`.

| Target | Source | Provider |
| --- | --- | --- |
| `claude-run/.orchestrator` | `C:\Data\AI\AnsiDemo\.orchestrator` | Claude Code |
| `copilot-run/.orchestrator` | `C:\Data\AI\TextKit\.orchestrator` | Copilot CLI |

Copied on 2026-10-03, byte for byte (`File.Copy`), keeping the relative paths. The only change
is the trimming of the Copilot event logs described below.

**Fixture content must not be edited.** Tests rely on the files being exactly what the
orchestrator wrote. To refresh a fixture, copy it again from its source by the rules here and
update this README and `OrchDash.Core.Tests/Fixtures/FixtureFilesTests.cs`.

`.gitattributes` (`* -text`) keeps git from converting line endings, and `.gitignore` (`!*`)
keeps any ignore rule elsewhere in the repo (such as one for `.orchestrator`) from hiding a
fixture file.

## Selection

Only these files are copied (paths relative to the source `.orchestrator` folder). Nothing else,
for example `spec.md` or the logs of other tasks.

| Source | Files |
| --- | --- |
| AnsiDemo | `tasks.json`, `state.json`, `progress.md`, `project.json`, `logs/bootstrap-20261001-100433/**`, `logs/audio-synth/**`, `logs/audio-synth-integration-setup.log`, `logs/audio-synth-integration-check.log` |
| TextKit | `tasks.json`, `state.json`, `progress.md`, `project.json`, `run.lock`, `logs/bootstrap-20261003-110028/**`, `logs/planner-20261003-110111-1.json*`, `logs/core/**`, `logs/count/**`, `logs/core-integration-*.log*`, `logs/count-integration-*.log*` |

That is 22 files for `claude-run` and 67 for `copilot-run`; `FixtureFilesTests` lists each one.

## Trimming

Copilot run only: in each `*.events.jsonl`, every line that contains `"ephemeral":true` is
dropped, except the first 5 of each top-level `type` in that file. The file is split on the
line-feed byte, whole lines are kept or dropped together with their line break, and the kept
bytes are written unchanged. The Claude run is not trimmed.

| `copilot-run/.orchestrator/` file | Lines before | Lines after |
| --- | ---: | ---: |
| `logs/bootstrap-20261003-110028/attempt-1.json.events.jsonl` | 1453 | 88 |
| `logs/planner-20261003-110111-1.json.events.jsonl` | 2256 | 75 |
| `logs/core/20261003-113444/attempt-1-worker.json.events.jsonl` | 5117 | 112 |
| `logs/core/20261003-113444/attempt-1-review-1.json.events.jsonl` | 67 | 22 |
| `logs/count/20261003-114955/attempt-1-worker.json.events.jsonl` | 1916 | 103 |
| `logs/count/20261003-114955/attempt-1-review-1.json.events.jsonl` | 105 | 47 |
| `logs/count/20261003-115046/attempt-1-resolver.json.events.jsonl` | 706 | 94 |
| `logs/count/20261003-115046/attempt-1-review-1.json.events.jsonl` | 204 | 58 |
| `logs/count/20261003-115126/attempt-1-resolver.json.events.jsonl` | 1035 | 105 |
| `logs/count/20261003-115126/attempt-1-review-1.json.events.jsonl` | 262 | 53 |

## Provider stores

`stores/` holds what Claude Code and Copilot CLI kept in their own stores about the sessions of
the two runs. The tests find it through `FixturePaths.ClaudeStore` and `FixturePaths.CopilotStore`.

| Target | Stands for | Read by |
| --- | --- | --- |
| `stores/claude` | `%USERPROFILE%\.claude` | the Claude transcript store (`projects/*/<sessionId>.jsonl`) |
| `stores/copilot` | `%USERPROFILE%\.copilot` | the Copilot session folder store (`session-state/<sessionId>/`) and usage reader (`session-store.db`) |

`claude` stands for `.claude` and `copilot` for `.copilot`, so that no fixture folder has a dot
name.

Source: the staging folder
`C:\Data\AI\AgentOrchestratorDashboardPitch\part2-fixture-sources\stores`, which was prepared on
2026-10-04 from `%USERPROFILE%\.claude` and `%USERPROFILE%\.copilot` on the PC that made the
fixture runs. Copied on 2026-10-04, byte for byte (`File.Copy`), keeping the relative paths,
and checked by the SHA-256 of every file. `session-store.db` is a binary SQLite file.

### Selection

The 24 files of the fixture manifest (2,313,084 bytes); `FixtureFilesTests` lists each one.

| Files | Sessions |
| --- | --- |
| `claude/projects/C--Data-AI-AnsiDemo/60e2b369-7dd9-4eeb-b389-cdadd402e942.jsonl` | `claude-run` bootstrap |
| `claude/projects/C--Data-AI-AnsiDemo-worktrees-audio-synth/66a6a33c-01ca-42bf-85bb-4eec9505a991.jsonl` and `210c86fa-485b-48c1-8808-6dac62e28c71.jsonl` | `claude-run` `audio-synth` worker and review |
| `copilot/session-state/<sessionId>/workspace.yaml` and `events.jsonl` | the 10 sessions of `copilot-run`: `1ebef052-3d86-4b0f-abd5-111868a6de34`, `483087e5-0b46-4aa4-ad51-a9cb81de2f9d`, `632962e6-b77b-473f-8159-fa68fc99acba`, `905a692e-5150-4700-8ea7-ac94558036db`, `93a2aa4b-22ca-410a-bef8-d28ca63c86ae`, `9db7bfa4-3063-4400-93f0-97f8a68e0d91`, `ae783abf-0989-4988-88c1-089deac14062`, `c61fb851-7815-4f6a-9f37-9e82a52e3ece`, `d92e413e-38cc-401c-8118-611e46e77210`, `f7dfd185-96f7-4770-99c9-5d660aba6c5c` |
| `copilot/session-store.db` | the 10 sessions of `copilot-run` |

### Preparation

The staging folder was made from the real stores by these rules; the copy into this folder
changed nothing.

1. Transcripts: whole files; the user's email address is replaced by `user@example.com` and the
   organisation id by `00000000-0000-0000-0000-000000000000`.
2. Session folders: `workspace.yaml` unchanged; `events.jsonl` keeps the first 2 lines of each
   `type`.
3. Database: a new file with the real schema (ordinary tables and indexes, without the full-text
   index), the row of `schema_version`, and the rows of `sessions` and `assistant_usage_events`
   of the 10 sessions. It is in rollback-journal mode, so a read-only connection creates no file
   next to it.

## Size

The whole folder is about 4.9 MB: the two runs about 2.6 MB and `stores` 2,313,084 bytes;
`FixtureFilesTests` fails above 6 MB (6 MiB).
