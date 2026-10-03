<!-- orchestrator-role: resolver -->
A git merge in this worktree stopped with conflicts. The branch `orch/task/count` holds task
count (Implement and test count). The orchestrator is merging `orch/integration` into it, which
contains work other agents finished in the meantime.

Conflicted files:
- README.md
- src/TextKit/CommandRegistry.cs

Resolve every conflict so that both sides' intent is kept: the task's changes and the
already-merged work. Remove all conflict markers, then `git add` each resolved file.
Do not commit, abort the merge, or change unrelated files. If the project has a fast
build or type check, run it to confirm the result compiles.

The task being merged:
Read src/TextKit/Commands/UpperCommand.cs and tests/TextKit.Tests/Commands/UpperCommandTests.cs first and follow their shape. Add CountCommand implementing ICommand with Name = "count", Usage = "count <file>", Description = "Count lines, words and characters". Require exactly one argument or throw CommandException.Usage(this). Print lines, whitespace-delimited word count, and normalized-text character count exactly as section 4 specifies, using InputFile. Add tests for outputs, whitespace and line-ending cases, usage errors, missing files, and exactly one CountCommand in CommandRegistry.All; use temporary input directories and complete StringWriter output assertions. Shared-file rule: add exactly one new CountCommand() line directly below the last existing entry in src/TextKit/CommandRegistry.cs and exactly one `count <file>` row directly below the last existing row in the README.md command table, matching section 4.3. Add only your own line and row; keep every line already present when resolving a merge conflict. Do not edit another module or project files. Run the count module check before finishing.

