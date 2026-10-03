<!-- orchestrator-role: reviewer -->
You review one task from an automated multi-agent build before it is merged. The
change is already committed on the current branch. Its acceptance command has passed.
Read files in the worktree as needed for context. Do not edit anything.

## The task: count - Implement and test count

Read src/TextKit/Commands/UpperCommand.cs and tests/TextKit.Tests/Commands/UpperCommandTests.cs first and follow their shape. Add CountCommand implementing ICommand with Name = "count", Usage = "count <file>", Description = "Count lines, words and characters". Require exactly one argument or throw CommandException.Usage(this). Print lines, whitespace-delimited word count, and normalized-text character count exactly as section 4 specifies, using InputFile. Add tests for outputs, whitespace and line-ending cases, usage errors, missing files, and exactly one CountCommand in CommandRegistry.All; use temporary input directories and complete StringWriter output assertions. Shared-file rule: add exactly one new CountCommand() line directly below the last existing entry in src/TextKit/CommandRegistry.cs and exactly one `count <file>` row directly below the last existing row in the README.md command table, matching section 4.3. Add only your own line and row; keep every line already present when resolving a merge conflict. Do not edit another module or project files. Run the count module check before finishing.

Files the task was allowed to edit:
- `src/TextKit/Commands/CountCommand.cs`
- `tests/TextKit.Tests/Commands/CountCommandTests.cs`
- Shared files any task may edit: `src/TextKit/CommandRegistry.cs`, `README.md`

Additional directories available for inspection (do not edit):
(none)

## Give two verdicts

- `spec_verdict`: `fail` only if the change misses part of the task, contradicts it,
  or fakes it (placeholders, hard-coded test answers, skipped or deleted tests).
- `quality_verdict`: `fail` only for blocker or major problems: bugs, broken error
  handling, security holes, or code that clearly ignores the repo's conventions.

List every problem in `issues` with a severity. Minor issues alone never fail a
verdict. Be concrete: name the file and what to change, because your issues are sent
back to the worker as its to-do list.

## Project spec

# TextKit

## 1. Summary

TextKit is a small command-line tool with four commands that each read one text file and print a
result: `upper` (upper-case the text), `count` (lines, words, characters), `find` (lines that
contain a text) and `freq` (most frequent words). A dispatcher picks the command by name and prints
a help text. Done means: `dotnet run --project src/TextKit -- count samples/poem.txt` prints three
counts, `dotnet run --project src/TextKit` lists the four commands, and every requirement below
passes its tests.

## 2. Scope

**In scope**
- The dispatcher with help and error reporting, a file reader shared by all commands, the four
  commands, a command registry, sample files, end-to-end tests that start the built program, and a
  README with a command table and examples.

**Out of scope**
- Reading from stdin, several input files, wildcards, output files, encodings other than UTF-8.
- Regular expressions, colours, configuration files, a `--version` option, localisation.
- Commands or options beyond those named here.

## 3. Requirements

The module that implements each area is in brackets (4.2). `<file>`, `<text>` and `<n>` stand for
command-line arguments. "Throw Usage" means: throw `CommandException.Usage(this)` (4.3).

### 1. Dispatch and help [core]
**Objective:** As a user, I want one program that runs a command by name and tells me which exist.

1.1 When `App.Run` is called with no arguments, or with `help`, `--help` or `-h` as the first argument, the App shall write the help text ("Help format" in 4.3) to `output` and return `ExitCodes.Ok`.
1.2 When the first argument equals the `Name` of a command in `commands` (ordinal, case-sensitive), the App shall call that command's `Run` with the remaining arguments and `output`, and return its result.
1.3 If the first argument matches no command and is not one of the help words of 1.1, the App shall write the line `error: unknown command '<argument>'` to `error`, write nothing to `output`, and return `ExitCodes.Usage`.
1.4 If a command throws `CommandException`, the App shall write the line `error: <Message>` to `error` and return the exception's `ExitCode`.
1.5 The `Program` entry point shall return `App.Run(args, CommandRegistry.All, Console.Out, Console.Error)` as the process exit code.
1.6 `CommandException.Usage(command)` shall return an exception with `ExitCode` = `ExitCodes.Usage` and `Message` = `usage: textkit ` followed by `command.Usage`.

### 2. Input files [core]
**Objective:** As a command author, I want one way to read the input, so that all commands treat files and line endings alike.

2.1 When `InputFile.ReadAllText(path)` is called for an existing file, InputFile shall return its content read with `File.ReadAllText(path, Encoding.UTF8)` (which drops a leading byte order mark) with every `\r\n` replaced by `\n`.
2.2 If `path` does not name an existing file, InputFile shall throw `CommandException` with `ExitCodes.Input` and the message `file not found: <path>`.
2.3 When `InputFile.ReadLines(path)` is called, InputFile shall return `ReadAllText(path)` split on `\n`, without the last element when that element is empty: `"a\nb\n"` gives `a`, `b`; an empty file gives no lines; `"a\n\n"` gives `a` and one empty line.

### 3. upper [core]
**Objective:** As a user, I want a file printed in upper case. As a command author, I want one finished command to copy.

3.1 When `upper <file>` is run, UpperCommand shall write `InputFile.ReadAllText(<file>).ToUpperInvariant()` to `output` with `Write` (no line break added) and return `ExitCodes.Ok`.
3.2 If the number of arguments is not 1, UpperCommand shall throw Usage.

### 4. count [count]
**Objective:** As a user, I want to know how long a text is.

4.1 When `count <file>` is run, CountCommand shall write the three lines `lines: <L>`, `words: <W>` and `chars: <C>` and return `ExitCodes.Ok`, where L = `InputFile.ReadLines(<file>).Length`, W = the number of maximal runs of characters for which `char.IsWhiteSpace` is false in `InputFile.ReadAllText(<file>)`, and C = the `Length` of that text.
4.2 If the number of arguments is not 1, CountCommand shall throw Usage.

### 5. find [find]
**Objective:** As a user, I want to see the lines that mention something.

5.1 When `find <file> <text>` is run, FindCommand shall write, for each line of `InputFile.ReadLines(<file>)` that contains `<text>` (ordinal, case-sensitive), the line `<n>: <line>`, where n is the 1-based line number, in file order, and return `ExitCodes.Ok`, also when no line matches.
5.2 Where `--ignore-case` is given as the third argument, FindCommand shall compare with `StringComparison.OrdinalIgnoreCase` instead.
5.3 If the number of arguments is not 2 or 3, the third argument is not `--ignore-case`, or `<text>` is empty, FindCommand shall throw Usage.

### 6. freq [freq]
**Objective:** As a user, I want to know which words a text uses most.

6.1 When `freq <file>` is run, FreqCommand shall take as words the maximal runs of characters for which `char.IsLetterOrDigit` is true in `InputFile.ReadAllText(<file>)`, lower-cased with `ToLowerInvariant`, write one line `<count> <word>` per distinct word, ordered by count descending and then by word ascending (ordinal), stop after 10 lines, and return `ExitCodes.Ok`; a file without words writes nothing.
6.2 Where `--top <n>` is given as the second and third arguments, FreqCommand shall stop after n lines instead of 10.
6.3 If the number of arguments is not 1 or 3, the second argument is not `--top`, or `<n>` is not an integer from 1 to 1000 (`int.TryParse` with `NumberStyles.None` and `CultureInfo.InvariantCulture`), FreqCommand shall throw Usage.

### 7. End to end [e2e]
**Objective:** As a maintainer, I want proof that the built program works from the command line and that the README is true.

7.1 The e2e module shall provide `samples/poem.txt` (ASCII only, 8 to 12 lines, exactly one of them empty, one word that occurs at least three times, and one word that occurs both capitalised and in lower case) and `samples/empty.txt` (zero bytes).
7.2 When an end-to-end test runs a command, it shall start `dotnet` in a child process with the path of the `TextKit.dll` in `AppContext.BaseDirectory` followed by the arguments, use the repository root (the nearest parent directory of `AppContext.BaseDirectory` that contains `TextKit.slnx`) as the working directory, capture stdout, stderr and the exit code, and replace `\r\n` by `\n` in both streams before comparing.
7.3 The end-to-end tests shall assert the complete stdout, an empty stderr and exit code 0 for: each of the four commands on `samples/poem.txt` (`find` once with and once without `--ignore-case`, `freq` once without and once with `--top 3`), `count` and `freq` on `samples/empty.txt`, and no arguments (the help text of 4.3 for exactly the four commands).
7.4 The end-to-end tests shall assert an empty stdout, the exact stderr line and the exit code for: an unknown command (1.3, exit 1), `count` on a file that does not exist (2.2 through 1.4, exit 2), and `count` without arguments (`error: usage: textkit count <file>`, exit 1).
7.5 A test shall assert that the names in `CommandRegistry.All` are, as a set in any order, `count`, `find`, `freq` and `upper`, and that `README.md` in the repository root has, for each command, a line equal to its row in the "README command table" format of 4.3; if a registry line or a row is missing, doubled or wrong, the e2e module corrects the shared file, not the test.
7.6 The e2e module shall add a section `## Examples` at the end of `README.md` with one example per command: a command line from that command's 7.3 tests on `samples/poem.txt`, written as `dotnet run --project src/TextKit -- <command> ...`, followed by the expected stdout of that test.

### 8. Registration [core, count, find, freq]
**Objective:** As a user, I want every command that exists to be runnable and documented.

8.1 The module that adds a command shall add the command's line to the registry and its row to the README command table (both in 4.3), and a test in the command's own test class shall assert that `CommandRegistry.All` contains exactly one instance of the command's class.

**Non-functional**
- N.1 The solution shall build with `dotnet build TextKit.slnx -warnaserror` without warnings.

## 4. Design

### 4.1 Approach

One console project and one test project. Each command is one class that implements `ICommand`. An
explicit list, `CommandRegistry.All`, names the commands; the module that adds a command adds its
own line to that list and its own row to the README table. `App` dispatches on the first argument
and is the only place that catches `CommandException` and writes to stderr, so commands stay plain
functions from arguments to text and are tested with a `StringWriter`. Rejected: finding commands by
reflection (more code than a four-line list, and the list of commands is no longer readable in one
place), the `System.CommandLine` package (a dependency for a few lines of dispatch), one project per
command (project files become shared files).

### 4.2 Modules and boundaries

| Module | Responsibility | Paths | Uses | Requirements |
| --- | --- | --- | --- | --- |
| core | The types of 4.3, implemented; `Program.cs`; `UpperCommand`; creates the registry with `upper` and the README command table with the `upper` row; tests of `App` (with fake commands), `InputFile`, `CommandException` and `UpperCommand` | `src/TextKit/Core/**`, `src/TextKit/Program.cs`, `src/TextKit/Commands/UpperCommand.cs`, `tests/TextKit.Tests/Core/**`, `tests/TextKit.Tests/Commands/UpperCommandTests.cs` | – | 1.1-1.6, 2.1-2.3, 3.1-3.2, 8.1, N.1 |
| count | `CountCommand` and its tests; its registry line and README row | `src/TextKit/Commands/CountCommand.cs`, `tests/TextKit.Tests/Commands/CountCommandTests.cs` | core | 4.1-4.2, 8.1, N.1 |
| find | `FindCommand` and its tests; its registry line and README row | `src/TextKit/Commands/FindCommand.cs`, `tests/TextKit.Tests/Commands/FindCommandTests.cs` | core | 5.1-5.3, 8.1, N.1 |
| freq | `FreqCommand` and its tests; its registry line and README row | `src/TextKit/Commands/FreqCommand.cs`, `tests/TextKit.Tests/Commands/FreqCommandTests.cs` | core | 6.1-6.3, 8.1, N.1 |
| e2e | Sample files, end-to-end tests, the registry and README check, README examples | `samples/**`, `tests/TextKit.Tests/EndToEnd/**` | core, count, find, freq | 7.1-7.6, N.1 |

The registry `src/TextKit/CommandRegistry.cs` and `README.md` belong to no module: they are the
shared files of section 7. Namespaces follow folders: `TextKit` (registry, `Program`),
`TextKit.Core`, `TextKit.Commands`; tests `TextKit.Tests.Core`, `TextKit.Tests.Commands`,
`TextKit.Tests.EndToEnd`.

**Existing files changed:** `src/TextKit/Program.cs` – the skeleton's placeholder is replaced by
core. `README.md` – core adds the command table, count, find and freq add one row each, e2e adds
the examples.

### 4.3 Shared contracts

**Types** (core implements them exactly as written; one file per type in `src/TextKit/Core/`):

```csharp
namespace TextKit.Core;

public interface ICommand
{
    string Name { get; }          // what the user types: lower-case ASCII letters
    string Usage { get; }         // Name and its arguments, e.g. "upper <file>"
    string Description { get; }   // one phrase without a final period
    int Run(string[] args, TextWriter output);   // args: everything after Name; returns an ExitCodes value
}

public static class ExitCodes
{
    public const int Ok = 0;
    public const int Usage = 1;   // unknown command or wrong arguments
    public const int Input = 2;   // input file not found
}

public sealed class CommandException(int exitCode, string message) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
    public static CommandException Usage(ICommand command) =>
        new(ExitCodes.Usage, $"usage: textkit {command.Usage}");
}

public static class InputFile
{
    public static string ReadAllText(string path);   // 2.1, 2.2
    public static string[] ReadLines(string path);   // 2.3
}

public static class App
{
    public static int Run(string[] args, IReadOnlyList<ICommand> commands, TextWriter output, TextWriter error);
}
```

**Commands** (class `<Class>` in namespace `TextKit.Commands`, public, with a public parameterless
constructor; the three properties return exactly these values):

| Name | Class | Usage | Description |
| --- | --- | --- | --- |
| `upper` | `UpperCommand` | `upper <file>` | Print the file in upper case |
| `count` | `CountCommand` | `count <file>` | Count lines, words and characters |
| `find` | `FindCommand` | `find <file> <text> [--ignore-case]` | Print the lines that contain a text |
| `freq` | `FreqCommand` | `freq <file> [--top <n>]` | List the most frequent words |

**Registry** (`src/TextKit/CommandRegistry.cs`, as core leaves it). A module that adds a command
adds one line `new <Class>(),` directly below the last entry of the list:

```csharp
using TextKit.Commands;
using TextKit.Core;

namespace TextKit;

public static class CommandRegistry
{
    public static IReadOnlyList<ICommand> All { get; } =
    [
        new UpperCommand(),
    ];
}
```

**Program** (`src/TextKit/Program.cs`, whole file):

```csharp
using TextKit;
using TextKit.Core;

return App.Run(args, CommandRegistry.All, Console.Out, Console.Error);
```

**Output.** Commands and `App` write whole lines with `WriteLine` (only `upper` uses `Write`). Unit
tests pass `new StringWriter { NewLine = "\n" }` and compare the complete text.

**Help format.** The line `Usage: textkit <command> [arguments]`, an empty line, the line
`Commands:`, then one line per command ordered by `Name` (ordinal): two spaces, `Usage` padded with
spaces on the right to the longest `Usage` among `commands`, three spaces, `Description`. For the
four commands:

```
Usage: textkit <command> [arguments]

Commands:
  count <file>                         Count lines, words and characters
  find <file> <text> [--ignore-case]   Print the lines that contain a text
  freq <file> [--top <n>]              List the most frequent words
  upper <file>                         Print the file in upper case
```

**README command table** (as core leaves it, at the end of `README.md`). A module that adds a
command adds its row directly below the last row of the table, built like the `upper` row: a pipe,
a space, the `Usage` in backticks, space pipe space, the `Description`, a space, a pipe.

```markdown
## Commands

| Command | Description |
| --- | --- |
| `upper <file>` | Print the file in upper case |
```

### 4.4 Error handling

A command reports a failure only by throwing `CommandException`; it never writes to stderr, never
calls `Console` and never calls `Environment.Exit`. `App.Run` catches `CommandException` only (1.4);
any other exception is a bug and is left to crash the process. Output written before a command
throws stays written. Exit codes are those of `ExitCodes`; nothing else is returned.

## 5. Build order

1. core.
2. In parallel: count, find, freq. They do not depend on each other.
3. Last: e2e, which depends on count, find and freq.

Five tasks, one per module of 4.2. The modules are small on purpose: do not merge them and do not
split them. Each task's `owns` is its module's Paths, and its acceptance command is its per-module
check from section 6. Do not put the shared files of section 7 in any task's `owns`: every task may
edit them through `settings.shared`, which the person running the plan sets by hand; say so in the
plan's `notes`. Repeat the shared-files rule of section 7 (which line and row to add, where, "add
only your own, keep every line already there") in the prompts of count, find and freq: the agent
that settles a merge conflict sees only the task prompt, not this spec.

## 6. Verification

- **Setup command:** `dotnet restore TextKit.slnx`
- **Test runner and layout:** xUnit 2.9 in `tests/TextKit.Tests`, one test class per file as `tests/TextKit.Tests/<Area>/<Class>Tests.cs` with namespace `TextKit.Tests.<Area>` (Area = Core, Commands, EndToEnd). Unit tests create their input files in a new directory under `Path.GetTempPath()` and delete it afterwards. Expected text in tests is written with `\n` escapes in ordinary string literals, never as multi-line raw or verbatim literals (a checkout may change the line endings of source files); tests read `README.md` only with `File.ReadAllLines` and never read `samples/` files themselves.
- **Per-module check:** `dotnet build TextKit.slnx -warnaserror && dotnet test TextKit.slnx --no-build --filter "FullyQualifiedName~<Filter>" -- RunConfiguration.TreatNoTestsAsError=true` with Filter = `TextKit.Tests.Commands.CountCommandTests` (count), `TextKit.Tests.Commands.FindCommandTests` (find), `TextKit.Tests.Commands.FreqCommandTests` (freq), `TextKit.Tests.EndToEnd` (e2e). For core, the whole-project check.
- **Whole-project check:** `dotnet build TextKit.slnx -warnaserror && dotnet test TextKit.slnx --no-build`
- **Manual checks:** `dotnet run --project src/TextKit -- freq samples/poem.txt --top 3` prints three lines; `dotnet run --project src/TextKit -- nope` prints the unknown-command error and `$LASTEXITCODE` is 1.

## 7. Constraints

- **Stack:** C# on .NET SDK 10 (`net10.0`, the SDK's default language version), file-scoped namespaces, nullable and implicit usings enabled, `InvariantGlobalization` true. One console project `src/TextKit/TextKit.csproj` (`OutputType` Exe, `AssemblyName` and `RootNamespace` TextKit) and `tests/TextKit.Tests/TextKit.Tests.csproj` referencing it (with `<Using Include="Xunit" />`), in `TextKit.slnx`. Packages, in the test project only: `Microsoft.NET.Test.Sdk`, `xunit` 2.9.x, `xunit.runner.visualstudio`. No other dependencies.
- **Skeleton:** the bootstrap creates `TextKit.slnx`, both project files, `.gitignore` (`bin/`, `obj/`), `.gitattributes` with the single line `* text=auto eol=lf`, `README.md` (title, one-line description, how to set up, build and test; no command list), `tests/TextKit.Tests/SmokeTests.cs` and a placeholder `src/TextKit/Program.cs` that prints one line. No task edits the solution or a project file.
- **Conventions:** one type per file; `Console` only in `Program.cs`; invariant culture for parsing and formatting; tests are `public class <Class>Tests` with `[Fact]`/`[Theory]`. count, find and freq first read `src/TextKit/Commands/UpperCommand.cs` and `tests/TextKit.Tests/Commands/UpperCommandTests.cs` and follow their shape; core writes those two files as the example to copy.
- **Shared files:** `src/TextKit/CommandRegistry.cs` and `README.md` (they go in `settings.shared`). core creates the registry and the command table; count, find and freq each add exactly one registry line and one table row (4.3); e2e adds the examples and may correct the registry and the table (7.5). Tasks that run in parallel add their lines at the same place. Merge conflicts there are expected and are settled when a task is merged: add only your own line, and keep every line that is already there.

**Always**
- Run your module's check from section 6 before finishing, and make it pass.
- Keep every signature, name, usage string and description of 4.3 as written; add members only inside your own module.
- Test every criterion of your module, including each Usage case. Exceptions: 1.5 and N.1 are covered by the build and by 7.3 and 7.4; 7.1, 7.2 and 7.6 need no test of their own.

**Stop and report blocked**
- The .NET 10 SDK is missing, or a requirement cannot be met without changing something in 4.3 or editing a file outside your module's Paths and the shared files.

**Never**
- Add NuGet packages or change `.slnx` or `.csproj` files.
- Add, change or remove another module's line in a shared file, except for the corrections of 7.5.
- Call `Console` or `Environment.Exit` outside `Program.cs`.
- Write outside the repository and the OS temp directory during tests; skip or delete a test.

## 8. Open questions

None.


## Diff (orch/integration..HEAD)

```
README.md                                         |  1 +
 src/TextKit/CommandRegistry.cs                    |  1 +
 src/TextKit/Commands/CountCommand.cs              | 41 +++++++++++++++++++++++++++++++++++++++++
 tests/TextKit.Tests/Commands/CountCommandTests.cs | 72 ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
 4 files changed, 115 insertions(+)
```

```diff
diff --git a/README.md b/README.md
index 5d7ab10..0f18595 100644
--- a/README.md
+++ b/README.md
@@ -17,3 +17,4 @@ Run `dotnet build TextKit.slnx -warnaserror && dotnet test TextKit.slnx --no-bui
 | `upper <file>` | Print the file in upper case |
 | `find <file> <text> [--ignore-case]` | Print the lines that contain a text |
 | `freq <file> [--top <n>]` | List the most frequent words |
+| `count <file>` | Count lines, words and characters |
diff --git a/src/TextKit/CommandRegistry.cs b/src/TextKit/CommandRegistry.cs
index a842989..65141b3 100644
--- a/src/TextKit/CommandRegistry.cs
+++ b/src/TextKit/CommandRegistry.cs
@@ -10,5 +10,6 @@ public static class CommandRegistry
         new UpperCommand(),
         new FindCommand(),
         new FreqCommand(),
+        new CountCommand(),
     ];
 }
diff --git a/src/TextKit/Commands/CountCommand.cs b/src/TextKit/Commands/CountCommand.cs
new file mode 100644
index 0000000..3c2ebc8
--- /dev/null
+++ b/src/TextKit/Commands/CountCommand.cs
@@ -0,0 +1,41 @@
+using TextKit.Core;
+
+namespace TextKit.Commands;
+
+public class CountCommand : ICommand
+{
+    public string Name => "count";
+    public string Usage => "count <file>";
+    public string Description => "Count lines, words and characters";
+
+    public int Run(string[] args, TextWriter output)
+    {
+        if (args.Length != 1)
+        {
+            throw CommandException.Usage(this);
+        }
+
+        var text = InputFile.ReadAllText(args[0]);
+        var lines = InputFile.ReadLines(args[0]).Length;
+        var words = 0;
+        var inWord = false;
+
+        foreach (var character in text)
+        {
+            if (char.IsWhiteSpace(character))
+            {
+                inWord = false;
+            }
+            else if (!inWord)
+            {
+                words++;
+                inWord = true;
+            }
+        }
+
+        output.WriteLine($"lines: {lines}");
+        output.WriteLine($"words: {words}");
+        output.WriteLine($"chars: {text.Length}");
+        return ExitCodes.Ok;
+    }
+}
diff --git a/tests/TextKit.Tests/Commands/CountCommandTests.cs b/tests/TextKit.Tests/Commands/CountCommandTests.cs
new file mode 100644
index 0000000..87e6eb5
--- /dev/null
+++ b/tests/TextKit.Tests/Commands/CountCommandTests.cs
@@ -0,0 +1,72 @@
+using TextKit;
+using TextKit.Commands;
+using TextKit.Core;
+
+namespace TextKit.Tests.Commands;
+
+public class CountCommandTests : IDisposable
+{
+    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
+
+    [Theory]
+    [InlineData("", "lines: 0\nwords: 0\nchars: 0\n")]
+    [InlineData("one two\nthree", "lines: 2\nwords: 3\nchars: 13\n")]
+    [InlineData("one\r\ntwo\r\n", "lines: 2\nwords: 2\nchars: 8\n")]
+    [InlineData("a\rb", "lines: 1\nwords: 2\nchars: 3\n")]
+    [InlineData("a\n\n", "lines: 2\nwords: 1\nchars: 3\n")]
+    [InlineData(" \t\r\n\u2003a\u00a0b \n", "lines: 2\nwords: 2\nchars: 9\n")]
+    [InlineData("a,b!c", "lines: 1\nwords: 1\nchars: 5\n")]
+    public void PrintsCompleteCounts(string text, string expected)
+    {
+        Directory.CreateDirectory(directory);
+        var path = Path.Combine(directory, "input.txt");
+        File.WriteAllText(path, text, System.Text.Encoding.UTF8);
+        using var output = new StringWriter { NewLine = "\n" };
+
+        var result = new CountCommand().Run([path], output);
+
+        Assert.Equal(ExitCodes.Ok, result);
+        Assert.Equal(expected, output.ToString());
+    }
+
+    [Fact]
+    public void WrongNumberOfArgumentsThrowsUsage()
+    {
+        using var output = new StringWriter { NewLine = "\n" };
+
+        foreach (string[] args in new string[][] { [], ["one", "two"] })
+        {
+            var exception = Assert.Throws<CommandException>(() => new CountCommand().Run(args, output));
+            Assert.Equal(ExitCodes.Usage, exception.ExitCode);
+            Assert.Equal("usage: textkit count <file>", exception.Message);
+        }
+        Assert.Equal("", output.ToString());
+    }
+
+    [Fact]
+    public void MissingFileThrowsInputErrorWithoutWritingOutput()
+    {
+        var path = Path.Combine(directory, "missing.txt");
+        using var output = new StringWriter { NewLine = "\n" };
+
+        var exception = Assert.Throws<CommandException>(() => new CountCommand().Run([path], output));
+
+        Assert.Equal(ExitCodes.Input, exception.ExitCode);
+        Assert.Equal($"file not found: {path}", exception.Message);
+        Assert.Equal("", output.ToString());
+    }
+
+    [Fact]
+    public void RegistryContainsExactlyOneCountCommand()
+    {
+        Assert.Single(CommandRegistry.All.OfType<CountCommand>());
+    }
+
+    public void Dispose()
+    {
+        if (Directory.Exists(directory))
+        {
+            Directory.Delete(directory, recursive: true);
+        }
+    }
+}
```


Return ONLY a JSON object matching this schema (no Markdown fence or explanation):
{"type":"object","required":["spec_verdict","quality_verdict","issues","summary"],"properties":{"spec_verdict":{"type":"string","enum":["pass","fail"],"description":"fail only if the change misses or contradicts what the task asked for."},"quality_verdict":{"type":"string","enum":["pass","fail"],"description":"fail only for blocker or major issues."},"issues":{"type":"array","items":{"type":"object","required":["severity","description"],"properties":{"severity":{"type":"string","enum":["blocker","major","minor"]},"file":{"type":"string"},"description":{"type":"string"}}}},"summary":{"type":"string"}}}
