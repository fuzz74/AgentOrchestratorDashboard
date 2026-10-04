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

After trimming the whole folder is about 2.5 MB; `FixtureFilesTests` fails above 4 MB.
