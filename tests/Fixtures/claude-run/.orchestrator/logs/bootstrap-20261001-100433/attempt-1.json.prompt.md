<!-- orchestrator-role: bootstrap -->
You set up the skeleton of a new project. After you, an orchestrator splits the spec below
into tasks and builds them with parallel coding agents, each in its own git worktree.
Every one of those worktrees starts from the commit you prepare. It runs a setup command
first, then the task's test command. Your job is to make both work before any feature exists.

The current directory is the repository root. It may be empty apart from `.orchestrator/`,
which you must leave alone.

## Build only the skeleton

Read the spec, mainly its stack, verification, constraints and modules sections. Then create:

1. The project manifest and configuration for the stack the spec names: package or project
   file, compiler/linter config if the spec asks for it, and a lock file produced by actually
   installing (so a clean setup command such as `npm ci` works).
2. Only the dependencies needed to build and test: the test runner, build tooling and the
   frameworks the spec names. Nothing speculative.
3. A `.gitignore` covering dependency folders, build output, caches and local env files.
4. One trivial passing test that proves the test runner works, placed where the spec's test
   layout says tests go, but outside every module path listed in the spec (for example
   `tests/smoke.test.ts` or `tests/test_smoke.py`).
5. A short `README.md`: what the project is (one line from the spec), how to set up, how to test.

Do not implement any requirement, contract or module from the spec: that is the other
agents' work, and files inside module paths would collide with them. Do not run git
commands; the orchestrator commits for you.

## Commands you return

- `setup`: installs dependencies in a fresh clone, e.g. `npm ci`, `uv sync`, `dotnet restore`.
  Use the spec's setup command if it gives one.
- `integration_check`: builds and runs the whole test suite, e.g. `npm run build && npm test`.
  Use the spec's whole-project check if it gives one. It runs after `setup` in the same folder.

Both run with PowerShell 7 (`pwsh`) from the repository root. Chain steps with `&&`. Run
both yourself and make them pass before you finish.

If the spec names no stack and you cannot infer one, or the stack cannot be installed on
this machine, return `status: blocked` with the reason instead of guessing.

## Spec

# AnsiDemo

## 1. Summary

AnsiDemo is a terminal demoscene demo for Windows Terminal: eight scenes built from twelve classic
effects (plasma, fire, tunnel, starfield, rotozoomer, 3D cube, metaballs, sine scroller, Mandelbrot
zoom, voxel landscape, raymarched spheres, Matrix rain), drawn in 24-bit ANSI colour with half-block
and Braille characters, with smooth transitions and a chiptune soundtrack played through the Windows
waveOut API from a four-channel synth driven by a tracker. There is no business logic; the point is
to show off. Done means: `dotnet run --project src/AnsiDemo` plays the whole demo with music at a
party, every effect and the whole demo have golden frames at fixed timestamps, every effect has an
FPS budget, and every requirement below passes its tests.

## 2. Scope

**In scope**
- Contracts (pixels, frames, effects, transitions, synth, songs), two renderers, the ANSI console host,
  the timeline and scene runner, 12 effects, 4 transitions, the synth, waveOut and WAV output, the
  tracker, one soundtrack, the demo script, and a CLI with headless frame and WAV rendering.

**Out of scope**
- Terminals other than Windows Terminal, non-Windows audio, 16/256-colour fallbacks, mouse input.
- A tracker editor, loading songs or scripts from files at runtime, stereo, MIDI, configuration
  files, video recording, effects or transitions beyond those named here.

## 3. Requirements

The module that implements each area is in brackets (4.2). Headless time advances in fixed steps of
1/30 s ("Time model" in 4.3). Coordinates: x from the left, y from the top, 0-based.

### 1. Pixels, cells and renderers [core]
**Objective:** As an effect author, I want to draw RGB pixels and get terminal cells out.

1.1 When `HalfBlockRenderer.Render(pixels, frame)` is called with pixels of size (cols, 2·rows), the renderer shall set cell (x, y) to `▀` (U+2580) with Fg = pixel (x, 2y) and Bg = pixel (x, 2y+1).
1.2 When `BrailleRenderer.Render(pixels, frame)` is called with pixels of size (2·cols, 4·rows), the renderer shall set cell (x, y) to the Braille character whose dot (dx, dy) is on iff pixel (px, py) = (2x+dx, 4y+dy) has `Luma > 16 · Bayer4[py & 3][px & 3]` (4.3), with Fg = the per-channel integer mean of the on pixels and Bg = Black; a cell with no dot on is `Cell.Blank`.
1.3 If the pixel buffer size differs from `PixelBuffer.SizeFor(mode, frame.Cols, frame.Rows)`, the renderer shall throw `ArgumentException`.
1.4 `Frame.ToAnsi()` shall produce exactly the "ANSI format" of 4.3, and `Frame.ToText()` the characters only. [contracts]

### 2. Console output [demo]
**Objective:** As a viewer, I want a flicker-free full-screen picture that gives my shell back.

2.1 When the ConsoleHost starts, it shall enable virtual terminal processing on stdout, set UTF-8 output, switch to the alternate screen buffer and hide the cursor; when it stops for any reason, including an exception or Ctrl+C, it shall restore all four and leave the prompt usable.
2.2 When a frame is written, the ConsoleHost shall position the cursor per row with `ESC[<row>;1H`, write SGR sequences only where the colour changes from the previous cell, never clear the screen, and never write a newline after the last column of the last row (no scrolling).
2.3 While the window is smaller than 80×24, the ConsoleHost shall draw only `Window too small: need 80x24, have {w}x{h}` on row 0, keep time running and handle only the quit keys.
2.4 When the window size changes, the ConsoleHost shall recreate the buffers at the new size and `Reset` the active scenes' effects; the timeline position shall be kept.

### 3. Timeline and runners [core]
**Objective:** As the demo author, I want scenes to follow a script on a beat grid, with transitions.

3.1 `Timeline.StartOf(scenes, i)` shall return the sum of `Bars · SecondsPerBar` of scenes 0..i−1, and `TotalSeconds(scenes)` the sum over all scenes.
3.2 When `Timeline.CueAt(scenes, t)` is called with 0 ≤ t < TotalSeconds, it shall return the scene i with StartOf(i) ≤ t < StartOf(i+1) and `SceneTime = t − StartOf(i)`; while `SceneTime < TransitionBars · SecondsPerBar`, it shall set `FromScene = i − 1` (−1 for i = 0, meaning a blank frame), `FromSceneTime = t − StartOf(i − 1)` (0 for i = 0) and `Progress = SceneTime / (TransitionBars · SecondsPerBar)`; otherwise `FromScene = null` and `Progress = 1`.
3.3 If t < 0 or t ≥ TotalSeconds, `CueAt` shall return null.
3.4 When `SceneRunner.Render(frame)` is called, the runner shall clear the pixel buffer to Black, call `Render` of each `IPixelEffect` layer in script order, run the scene's renderer, then call `Render` of each `ICellEffect` layer in script order.
3.5 When a `SceneRunner` is constructed, it shall create each layer through `EffectCatalog.Create(name)` (never touching `TransitionCatalog`; effects and transitions need a public parameterless constructor) and call `Reset` with `new DemoRandom((uint)(seed · 31 + layerIndex + 1))` and a context with Time 0 and DeltaTime 0; `Step(dt)` shall call `Update` on every layer with Time accumulated over the steps.
3.6 When `SceneRunner.RenderAt(scene, cols, rows, t, seed)` is called, it shall construct the runner, call `Step(1/30)` round(30·t) times and return the rendered frame; `DemoRunner.RenderAt(scenes, cols, rows, t)` shall do the same for the cue's scene and from-scene (each from its own start) and return `TransitionCatalog.Create(scene.Transition)` applied with `from` = the from-scene's frame (blank for −1), `to` = the scene's frame and the cue's Progress, or the scene's frame alone when `FromScene` is null.
3.7 When `DemoRunner.Step(dt)` moves into a new cue, the runner shall create the new scene's `SceneRunner` and keep the previous one as the from-scene until its transition ends; `Seek(t)` shall set Time = t and create only the cue's scene, stepped from its start in 1/30 s steps; with no from-scene, `Render` shows that scene alone, without a transition.
3.8 If an effect or transition name is unknown, `EffectCatalog.Create` and `TransitionCatalog.Create` shall throw `ArgumentException` naming it; `Names` shall list every attributed class in every loaded assembly whose name starts with `AnsiDemo`, sorted ordinally (core's tests define `[Effect("test-…")]` and `[Transition("test-…")]` classes under `tests/AnsiDemo.Tests/Core/` and assert containment, not an exact list).
3.9 When `Resize(cols, rows)` is called on a runner, it shall recreate its buffers at the new size and call `Reset` on every active layer with a context at that layer's current scene time; `Time` shall be kept.

### 4. Effects [one module per effect]
**Objective:** As a viewer, I want twelve distinct, animated, deterministic effects.

Every effect E in the table shall satisfy 4.1-4.6. Kind `pixel` implements `IPixelEffect`, `cell` implements `ICellEffect`. Fills `all` writes every pixel; `sparse` writes only its own pixels and leaves the rest as it found them.

4.1 When `SceneRunner.RenderAt` is run twice for `SceneSpec.Solo(E, its preferred mode)` at the same size, seed and t, E shall produce identical `ToAnsi()` output.
4.2 E's frames at t = 0, 1 and 4 s at 80×24 in its preferred mode shall equal the goldens `t0.ansi`, `t1.ansi` and `t4.ansi` in `tests/AnsiDemo.Tests/Effects/<Folder>/`, recorded by E's own task through `Golden.Assert`, checked against the "Must look like" column, and committed.
4.3 At t = 1 s and 80×24, E's frame shall differ from its t = 0 frame, and its `LitCount()` shall be ≥ 60% of the cells for `all` effects whose preferred mode is HalfBlock (fire and mandelbrot legitimately contain black), and ≥ 2% for every other effect.
4.4 E shall render without exception at 80×24 and 200×60 in both pixel modes.
4.5 `Perf.MeanFrameMs(E, preferred mode, 200, 60)` shall be ≤ 33 ms (aim for 15 ms: acceptance runs while other agents build); E may compute at a lower internal resolution and scale up.
4.6 E shall take randomness only from the `DemoRandom` given to `Reset`, and shall keep no static mutable state.

| Name | Folder | Kind | Fills | Mode | Must look like |
| --- | --- | --- | --- | --- | --- |
| plasma | Plasma | pixel | all | HalfBlock | Smooth, slowly moving colour blobs from a sum of sines; the palette cycles through the full hue range over time |
| fire | Fire | pixel | all | HalfBlock | Flames rising from the bottom edge and flickering randomly; black → red → yellow → white palette; cooled to black before the top |
| tunnel | Tunnel | pixel | all | HalfBlock | A textured tunnel seen from inside, rotating and flying forward; darker toward the centre |
| starfield | Starfield | pixel | sparse | Braille | ≥ 200 white-to-grey stars streaming outward from the centre, brighter when near; stars respawn when they leave the buffer |
| rotozoomer | Rotozoomer | pixel | all | HalfBlock | A tiled procedural texture rotating and zooming in and out continuously |
| cube | Cube | pixel | sparse | Braille | A wireframe cube (12 edges) rotating about two axes in perspective, spanning ≥ 1/3 of the height; edge colour cycles with time |
| metaballs | Metaballs | pixel | all | Braille | ≥ 4 glowing blobs that merge and split while moving on smooth paths over black |
| scroller | Scroller | cell | – | HalfBlock | `ScrollerText.Value` scrolling right-to-left at 20-30 columns/s in the middle third of the rows, each character on a sine wave of amplitude ≥ 3 rows, coloured by character position |
| mandelbrot | Mandelbrot | pixel | all | HalfBlock | A continuous zoom into a boundary point of the Mandelbrot set; smooth colour bands outside, black inside; zoom factor ≥ 2^10 by t = 20 s |
| voxel | Voxel | pixel | sparse | HalfBlock | A heightmap landscape flown over Comanche-style, drawn as columns from the ground up; green → brown → white by height with distance fog; sky pixels untouched |
| raymarch | Raymarch | pixel | all | Braille | ≥ 3 shaded spheres (one orbiting) over a checkered floor, one light with specular highlights, camera slowly moving |
| matrix | Matrix | cell | – | Braille | Columns of random ASCII glyphs falling at varied speeds, bright head, green tail fading to black; glyphs change while falling |

### 5. Transitions [transitions]
**Objective:** As a viewer, I want scenes to flow into each other.

5.1 `crossfade` shall output, per cell, `Ch` from `to` if progress ≥ 0.5 else from `from`, and `Fg`/`Bg` = `Rgb.Lerp(from, to, progress)`.
5.2 `wipe` shall output `to` for cells with x < floor(progress · Cols) and `from` elsewhere.
5.3 `dissolve` shall output `to` for cells with `DemoRandom.Hash(x, y) / 4294967296.0 < progress` and `from` elsewhere.
5.4 `push` shall output, with s = floor(progress · Cols), `from[x + s, y]` for x < Cols − s and `to[x − (Cols − s), y]` for x ≥ Cols − s.
5.5 Every transition shall output exactly `from` at progress 0 and exactly `to` at progress 1, clamp progress to 0..1, and throw `ArgumentException` when the three frames differ in size.

### 6. Synth [audio]
**Objective:** As the tracker, I want four chip channels to play notes on.

6.1 `Synth` shall have 4 channels: 0 and 1 pulse, 2 triangle, 3 noise, each silent (value 0) until `NoteOn`.
6.2 When `NoteOn(ch, f, instrument)` is called, the channel shall restart its phase at 0, set its envelope to 1 and play f Hz: pulse outputs +1 while the phase fraction < `Duty`, else −1; triangle rises linearly from −1 to +1 over the first half period and falls over the second; noise steps a 15-bit LFSR (feedback = bit 0 XOR bit 1 shifted in at the top, seed 1) 16·f times per second and outputs +1 when bit 0 is set, else −1.
6.3 When `Render(buffer)` is called, the Synth shall write per sample `clamp(0.25 · Σ value · Volume · envelope, −1, 1) · 32767` as `short`, where each envelope falls by `DecayPerSecond` per second from 1 and stops at 0, then advance every channel by one sample.
6.4 `SetFrequency(ch, f)` shall change the pitch without restarting phase or envelope; `NoteOff(ch)` shall silence the channel until the next `NoteOn`.
6.5 If ch is outside 0..3, f ≤ 0 or f > SampleRate / 2, the Synth shall throw `ArgumentException`.

### 7. Audio output [audio]
**Objective:** As a viewer, I want sound from the speakers; as a tester, I want the same samples in a file.

7.1 When `WaveOutSink.Start(source)` is called and `DeviceCount > 0`, the sink shall open `WAVE_MAPPER` as 16-bit mono 44100 Hz PCM through `winmm.dll`, keep ≥ 4 buffers of 2048 samples queued from a feeder thread, and be playing within 200 ms.
7.2 If `DeviceCount` is 0 or `waveOutOpen` fails, `Start` shall throw `AudioDeviceException` with the MMRESULT code; `Stop` and `Dispose` shall be safe to call at any time and shall return within 500 ms.
7.3 When `WavFileSink.Start(source)` is called, the sink shall render exactly `totalSamples` samples through `source.Render` and write a RIFF/WAVE PCM file with a canonical 44-byte header (16-bit mono 44100 Hz) and those samples, then return.
7.4 `NullSink` shall accept `Start` and `Stop` and never call `Render`.

### 8. Tracker [tracker]
**Objective:** As the soundtrack author, I want to write music as text and hear it on the synth.

8.1 When `SongParser.Parse(text)` is called with text in the "Song format" of 4.3, it shall return the `Song` with instruments, patterns and order in file order.
8.2 If the text breaks the format (unknown directive, instrument or pattern name, a row without exactly 4 cells, a bad note, value or duty, missing `bpm`, `rows-per-beat` or `order`, a duplicate name, or a pattern without `end`), `Parse` shall throw `SongFormatException` with the 1-based line number.
8.3 `Notes.Frequency(midi)` shall return 440 · 2^((midi − 69) / 12); `Notes.Parse` shall map `C-4` to 60, `C#4` to 61, `B-3` to 59 and `A-4` to 69 (sharps only, octaves 0-8) and throw `FormatException` otherwise.
8.4 When `SongPlayer.Render(buffer)` is called, the player shall walk the order list row by row at `SecondsPerRow`, and on each row's first sample, per channel: `NoteOn(ch, Frequency(Midi), instrument)` for a note cell (with `Volume · v / 9` when `v` is given), `NoteOff(ch)` for `off`, nothing for `---`; for a note with arpeggio (X, Y) ≠ (0, 0) it shall call `SetFrequency` with base + X semitones at 1/3 of the row and base + Y at 2/3; then render the samples through the synth. Row boundaries shall be sample-accurate.
8.5 When the order list ends, the player shall start over if `loop` is true, else render silence; `PositionSeconds` and `DurationSeconds` (= `Song.DurationSeconds`) shall be exact to the sample.
8.6 While `Paused` is true, `Render` shall write zeros and not advance; `Seek(t)` shall be applied at the next `Render` (callable from another thread), move to the first row at or after t and call `NoteOff` on every channel.

### 9. Soundtrack [soundtrack]
**Objective:** As a viewer, I want a real tune that stays in time with the scenes.

9.1 `Soundtrack.Text` shall parse with `SongParser` with `bpm 125`, `rows-per-beat 4` and `DurationSeconds ≥ DemoClock.DemoBars · DemoClock.SecondsPerBar`.
9.2 The song shall contain ≥ 6 distinct patterns, use every channel with ≥ 100 note cells each summed over the order list, and use `v` and `a` at least once each.
9.3 When the whole song is rendered through `Synth`, fewer than 1% of samples shall be at ±32767 and the peak shall be ≥ 8000.

### 10. CLI, host and demo script [demo]
**Objective:** As a viewer, I want to run the demo; as a tester, I want frames and audio without a terminal.

10.1 When run with no arguments, the app shall play `DemoScript.Scenes` from t = 0 at the terminal size, with `WaveOutSink` playing `Soundtrack.Text` through `SongPlayer`, at a target of 30 frames/s (sleep the rest of each 33 ms; a slow frame is not caught up; dt is measured and clamped to ≤ 0.1 s), and exit with code 0 at TotalSeconds; with `--loop` it shall restart at t = 0 with the song restarted.
10.2 `DemoScript.Scenes` shall be exactly the scene table of 4.3 and total `DemoClock.DemoBars` bars.
10.3 When Q, Escape or Ctrl+C is pressed, the app shall stop sound, restore the terminal and exit 0; Space shall pause and resume time and sound; Right and Left shall `Seek` to the start of the next or previous scene and seek the song to the same time.
10.4 Where `--no-sound` is given, or `WaveOutSink.Start` throws, the app shall play silently; in the second case it shall write the error to stderr after restoring the terminal.
10.5 Where `--scene <name>` or `--effect <name>` is given, the app shall loop only that scene (or `SceneSpec.Solo` of that effect in `EffectCatalog.PreferredMode`) without transitions and without music.
10.6 When `--frame <seconds>` is given, the app shall render `DemoRunner.RenderAt` (or the scene or effect of `--scene`/`--effect` with `SceneRunner.RenderAt`) at `--size <cols>x<rows>` (default 80x24) without touching the console mode, write `Frame.ToAnsi()` to stdout as UTF-8 without BOM and exit 0.
10.7 When `--wav <path>` is given, the app shall write the soundtrack for TotalSeconds through `WavFileSink` to that path and exit 0 without touching the console.
10.8 If an argument is unknown or malformed, names an unknown scene or effect, or `--frame` is outside 0 ≤ t < TotalSeconds of what is rendered, the app shall write the usage text of 4.3 to stderr and exit 2 (`DemoRunner.RenderAt` throws `ArgumentOutOfRangeException` for such t).
10.9 The whole demo's frames at 80×24 at t = StartOf(i) + 0.5·T and t = StartOf(i) + T + HeadlessStep (T = the scene's transition length; the second is just after the transition) for every scene i shall match goldens `scene<i>-mid.ansi` and `scene<i>-in.ansi` in `tests/AnsiDemo.Tests/Demo/`, and `README.md` shall list the keys and flags.

**Non-functional**
- N.1 Under `src/AnsiDemo/**`, `Contracts`, `Core`, `Effects.*` and `Transitions` shall not reference `System.Console`, `System.Random`, `DateTime` or `Stopwatch` (test support may). [each module]
- N.2 `--frame` of any timestamp at 80×24 shall finish within 10 s. [demo]

## 4. Design

### 4.1 Approach

One console project. Each frame, the active scene's pixel effects draw in order into an RGB
`PixelBuffer`, a renderer turns it into a `Frame` of cells (half-block: 1×2 full-colour pixels per
cell; Braille: 2×4 dithered dots per cell in one colour), and cell effects draw text on top. During a
transition the previous scene also renders and an `ITransition` combines the two frames. Everything
is deterministic for a seed and a fixed 1/30 s step, so a frame at a timestamp is reproducible, and
goldens are the ANSI text itself, which a terminal displays. Audio runs on its own thread: a
`SongPlayer` turns the song into synth calls and renders samples into a waveOut sink. Effects and
transitions register by attribute and are found by reflection, so no task edits a shared registry.
Rejected: a Core/App project split (nothing to separate), pixel-level goldens (not viewable in a
terminal), NAudio (one P/Invoke is enough).

### 4.2 Modules and boundaries

| Module | Responsibility | Paths | Uses | Requirements |
| --- | --- | --- | --- | --- |
| contracts | Everything under "Contracts" in 4.3, fully implemented, with tests of `Rgb`, `PixelBuffer`, `Frame`, `DemoRandom` (vectors in 4.3) and `Song` | `src/AnsiDemo/Contracts/**`, `tests/AnsiDemo.Tests/Contracts/**` | – | 1.4, N.1 |
| core | Renderers, catalogs, Timeline, SceneRunner, DemoRunner; test support `Golden` and `Perf`; test effects and transitions for its own tests | `src/AnsiDemo/Core/**`, `tests/AnsiDemo.Tests/Core/**`, `tests/AnsiDemo.Tests/Support/**` | contracts | 1.1-1.3, 3.1-3.9, N.1 |
| audio | Synth, WaveOutSink, WavFileSink, NullSink | `src/AnsiDemo/Audio/**`, `tests/AnsiDemo.Tests/Audio/**` | contracts | 6.1-6.5, 7.1-7.4 |
| tracker | Notes, SongParser, SongPlayer; test fake `RecordingSynth` | `src/AnsiDemo/Tracker/**`, `tests/AnsiDemo.Tests/Tracker/**` | contracts | 8.1-8.6 |
| transitions | `CrossfadeTransition`, `WipeTransition`, `DissolveTransition`, `PushTransition` | `src/AnsiDemo/Transitions/**`, `tests/AnsiDemo.Tests/Transitions/**` | contracts | 5.1-5.5, N.1 |
| soundtrack | `Soundtrack.Text` | `src/AnsiDemo/Music/**`, `tests/AnsiDemo.Tests/Music/**` | tracker, audio | 9.1-9.3 |
| effect-`<name>` (12 modules, one per row of the effect table) | class `<Folder>Effect` with `[Effect("<name>", mode)]`, its goldens and perf test | `src/AnsiDemo/Effects/<Folder>/**`, `tests/AnsiDemo.Tests/Effects/<Folder>/**` | core | 4.1-4.6, N.1 |
| demo | DemoScript, AppOptions, HeadlessHost, ConsoleHost, Program, demo goldens, README | `src/AnsiDemo/Shell/**`, `src/AnsiDemo/Program.cs`, `tests/AnsiDemo.Tests/Demo/**`, `README.md` | core, audio, tracker, transitions, soundtrack, every effect | 2.1-2.4, 10.1-10.9, N.2 |

Namespaces follow folders: `AnsiDemo.Contracts`, `AnsiDemo.Core`, `AnsiDemo.Music`,
`AnsiDemo.Effects.<Folder>`, `AnsiDemo.Shell`; tests `AnsiDemo.Tests.<Folder>` (effects:
`AnsiDemo.Tests.Effects.<Folder>`). No class may carry the name of a namespace segment.

**Existing files changed:** `src/AnsiDemo/Program.cs` and `README.md` – the skeleton's placeholders are replaced by demo.

### 4.3 Shared contracts

**Contracts** (`src/AnsiDemo/Contracts/*.cs`, one file per group, namespace `AnsiDemo.Contracts`, by contracts):

```csharp
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0), White = new(255, 255, 255);
    public int Luma => (R * 299 + G * 587 + B * 114) / 1000;          // 0..255
    public static Rgb Lerp(Rgb a, Rgb b, double t);                    // t clamped to 0..1; per channel round(a + (b − a)·t)
    public static Rgb FromHsv(double hueDegrees, double s, double v);  // standard HSV; hue wraps; s, v clamped; rounded to bytes
}
public enum PixelMode { HalfBlock, Braille }

public sealed class PixelBuffer
{
    public PixelBuffer(int width, int height);
    public int Width { get; }
    public int Height { get; }
    public Rgb[] Pixels { get; }                                       // row-major, index = y·Width + x
    public Rgb this[int x, int y] { get; set; }                        // outside: get Black, set ignored
    public void Clear(Rgb color);
    public static (int Width, int Height) SizeFor(PixelMode mode, int cols, int rows); // HalfBlock (cols, 2·rows); Braille (2·cols, 4·rows)
}

public readonly record struct Cell(char Ch, Rgb Fg, Rgb Bg)
{
    public static readonly Cell Blank = new(' ', Rgb.White, Rgb.Black);
}
public sealed class Frame
{
    public Frame(int cols, int rows);                                  // all Blank
    public int Cols { get; }
    public int Rows { get; }
    public Cell this[int x, int y] { get; set; }                       // outside: get Blank, set ignored
    public void Clear();
    public void Write(int x, int y, string text, Rgb fg, Rgb bg);     // clips at the edges
    public void CopyFrom(Frame other);                                 // ArgumentException on a size mismatch
    public int LitCount();                                             // cells with Ch != ' ' and (Fg != Black or Bg != Black)
    public string ToAnsi();                                            // "ANSI format" below
    public string ToText();                                            // rows of Ch joined by "\n", trailing spaces trimmed, final "\n"
}

public sealed class DemoRandom(uint seed)                              // xorshift32; a seed of 0 is replaced by 1
{
    public uint NextUInt();                                            // x ^= x << 13; x ^= x >> 17; x ^= x << 5; return x
    public double NextDouble();                                        // NextUInt() / 4294967296.0
    public int Next(int maxExclusive);                                 // (int)(NextDouble() · maxExclusive)
    public static uint Hash(int x, int y);                             // h = (uint)x·374761393u ^ (uint)y·668265263u; h ^= h >> 13; h *= 1274126177u; h ^= h >> 16
}
// Vectors: seed 1 → NextUInt 270369, 67634689, 2647435461. Hash(0,0) = 0, Hash(1,0) = 2182377942, Hash(3,7) = 1728803547.

public static class DemoClock
{
    public const int Bpm = 125, BeatsPerBar = 4, DemoBars = 74, HeadlessFps = 30;
    public const double SecondsPerBar = 60.0 * BeatsPerBar / Bpm;      // 1.92
    public const double HeadlessStep = 1.0 / HeadlessFps;
}
public sealed record FrameContext(
    double Time, double DeltaTime,             // seconds since Reset (scene-local); seconds since the previous Update (0 in Reset)
    double Beats,                              // Time · Bpm / 60; scene lengths are whole bars, so this is on the song's grid
    PixelMode Mode, int Cols, int Rows,
    int PixelWidth, int PixelHeight);          // PixelBuffer.SizeFor(Mode, Cols, Rows)

[AttributeUsage(AttributeTargets.Class)]
public sealed class EffectAttribute(string name, PixelMode preferredMode) : Attribute
{
    public string Name { get; } = name;
    public PixelMode PreferredMode { get; } = preferredMode;
}
public interface IEffect
{
    void Reset(FrameContext ctx, DemoRandom rng);  // initial state for this size; keep rng for later randomness
    void Update(FrameContext ctx);                 // advance by ctx.DeltaTime
}
public interface IPixelEffect : IEffect { void Render(PixelBuffer pixels); }   // draws over the current contents
public interface ICellEffect  : IEffect { void Render(Frame frame); }          // draws over the rendered cells
public static class ScrollerText
{
    public const string Value = "ANSIDEMO  *  TWELVE EFFECTS  *  EIGHT SCENES  *  ONE TERMINAL  *  CHIPTUNES INCLUDED  *  GREETINGS TO ALL PARTY PEOPLE  *  ";
}

public sealed record SceneSpec(string Name, PixelMode Mode, IReadOnlyList<string> Effects, int Bars,
                               string Transition, int TransitionBars)
{
    public static SceneSpec Solo(string effect, PixelMode mode) => new(effect, mode, [effect], 1000, "crossfade", 0);
}
public sealed record SceneCue(int Scene, double SceneTime, int? FromScene, double FromSceneTime, double Progress);

[AttributeUsage(AttributeTargets.Class)]
public sealed class TransitionAttribute(string name) : Attribute { public string Name { get; } = name; }
public interface ITransition { void Apply(Frame from, Frame to, double progress, Frame output); }

public static class AudioFormat { public const int SampleRate = 44100, Channels = 4; }
public sealed record Instrument(string Name, double Duty, double Volume, double DecayPerSecond);   // Duty: pulse channels only
public interface ISampleSource { void Render(Span<short> buffer); }     // mono 16-bit at SampleRate; fills the whole buffer
public interface ISynth : ISampleSource
{
    void NoteOn(int channel, double frequencyHz, Instrument instrument);
    void SetFrequency(int channel, double frequencyHz);
    void NoteOff(int channel);
}
public interface IAudioSink : IDisposable
{
    void Start(ISampleSource source);   // AudioDeviceException when no device; InvalidOperationException if already started
    void Stop();                        // idempotent
}
public sealed class AudioDeviceException(string message, int code) : Exception(message) { public int Code { get; } = code; }

public abstract record RowCell
{
    public sealed record Empty : RowCell;                                                // ---
    public sealed record Off : RowCell;                                                  // off
    public sealed record Note(int Midi, int Instrument, int? Volume, int ArpX, int ArpY) : RowCell; // Volume 0..9; Arp 0..15
}
public sealed record Pattern(string Name, IReadOnlyList<IReadOnlyList<RowCell>> Rows);  // every row has 4 cells, channel order
public sealed record Song(int Bpm, int RowsPerBeat, IReadOnlyList<Instrument> Instruments,
                          IReadOnlyList<Pattern> Patterns, IReadOnlyList<int> Order)      // Order = indexes into Patterns
{
    public double SecondsPerRow => 60.0 / (Bpm * RowsPerBeat);
    public int TotalRows => Order.Sum(i => Patterns[i].Rows.Count);
    public double DurationSeconds => TotalRows * SecondsPerRow;
}
public sealed class SongFormatException(string message, int line) : Exception(message) { public int Line { get; } = line; }
```

**Module APIs** (created by the named module; signatures fixed):

```csharp
// core, namespace AnsiDemo.Core
public static class HalfBlockRenderer { public static void Render(PixelBuffer pixels, Frame frame); }
public static class BrailleRenderer   { public static void Render(PixelBuffer pixels, Frame frame); }
public static class Renderers         { public static void Render(PixelMode mode, PixelBuffer pixels, Frame frame); }
public static class EffectCatalog     { public static IReadOnlyList<string> Names { get; } public static IEffect Create(string name); public static PixelMode PreferredMode(string name); }
public static class TransitionCatalog { public static IReadOnlyList<string> Names { get; } public static ITransition Create(string name); }
public static class Timeline
{
    public static double StartOf(IReadOnlyList<SceneSpec> scenes, int index);
    public static double TotalSeconds(IReadOnlyList<SceneSpec> scenes);
    public static SceneCue? CueAt(IReadOnlyList<SceneSpec> scenes, double seconds);
}
public sealed class SceneRunner
{
    public SceneRunner(SceneSpec scene, int cols, int rows, int seed = 1);
    public double Time { get; }
    public void Step(double dt);
    public void Resize(int cols, int rows);
    public void Render(Frame frame);
    public static Frame RenderAt(SceneSpec scene, int cols, int rows, double seconds, int seed = 1);
}
public sealed class DemoRunner
{
    public DemoRunner(IReadOnlyList<SceneSpec> scenes, int cols, int rows);
    public double Time { get; }
    public bool Finished { get; }                      // Time ≥ TotalSeconds
    public void Step(double dt);
    public void Seek(double seconds);
    public void Resize(int cols, int rows);
    public void Render(Frame frame);                   // the cue's scene's Transition applied when a from-scene exists; -1 = blank frame
    public static Frame RenderAt(IReadOnlyList<SceneSpec> scenes, int cols, int rows, double seconds);
}

// audio, namespace AnsiDemo.Audio
public sealed class Synth : ISynth { public Synth(); }
public sealed class WaveOutSink : IAudioSink { public WaveOutSink(); public static int DeviceCount { get; } }   // waveOutGetNumDevs
public sealed class WavFileSink : IAudioSink { public WavFileSink(string path, long totalSamples); }          // Start renders synchronously
public sealed class NullSink : IAudioSink { }

// tracker, namespace AnsiDemo.Tracker
public static class Notes      { public static double Frequency(int midi); public static int Parse(string note); }
public static class SongParser { public static Song Parse(string text); }
public sealed class SongPlayer : ISampleSource
{
    public SongPlayer(Song song, ISynth synth, bool loop);
    public double PositionSeconds { get; }
    public double DurationSeconds { get; }
    public bool Paused { get; set; }
    public void Seek(double seconds);
}

// soundtrack, namespace AnsiDemo.Music (not "Soundtrack": a class and its namespace may not share a name)
public static class Soundtrack { public const string Text = """ ... """; }

// demo, namespace AnsiDemo.Shell
public static class DemoScript { public static IReadOnlyList<SceneSpec> Scenes { get; } }
public sealed record AppOptions(bool NoSound, bool Loop, string? Scene, string? Effect, double? Frame, int Cols, int Rows, string? Wav);
public static class AppOptionsParser { public static AppOptions Parse(string[] args); }   // ArgumentException(usage) on error
public static class HeadlessHost { public static int Run(AppOptions o, TextWriter stdout, TextWriter stderr); } // --frame and --wav
public static class ConsoleHost  { public static int Run(AppOptions o); }
```

**Test support** (`tests/AnsiDemo.Tests/Support/`, namespace `AnsiDemo.Tests.Support`, by core):

```csharp
public static class Golden
{
    /// Compares frame.ToAnsi() with "<directory of callerFile>/<name>.ansi", ignoring '\r'. Missing file: writes it and
    /// fails with "golden recorded, rerun". Mismatch: writes "<name>.actual.ansi" beside it and fails with both paths.
    /// Environment variable ANSIDEMO_UPDATE_GOLDENS=1 overwrites and passes.
    public static void Assert(Frame frame, string name, [CallerFilePath] string callerFile = "");
}
public static class Perf
{
    /// SceneRunner for SceneSpec.Solo(effect, mode) at cols×rows; 10 warm-up frames; mean ms of Step(1/30) + Render over 60 frames.
    public static double MeanFrameMs(string effect, PixelMode mode, int cols, int rows);
}
```

**Tracker test fake** (`tests/AnsiDemo.Tests/Tracker/RecordingSynth.cs`, namespace `AnsiDemo.Tests.Tracker`, by tracker):

```csharp
public sealed class RecordingSynth : ISynth   // Calls: "On <ch> <hz:F2> <instrument name>", "Freq <ch> <hz:F2>", "Off <ch>"; Render writes zeros
{
    public List<string> Calls { get; }
    public long SamplesRendered { get; }
}
```

**ANSI format** (`Frame.ToAnsi`, ESC = U+001B): for each row top to bottom, for each cell left to
right: `ESC[38;2;R;G;Bm` if Fg differs from the previous cell of this row (always for the first
cell), then `ESC[48;2;R;G;Bm` likewise for Bg, then `Ch`; each row ends with `ESC[0m` and `\n`. No
cursor movement, no trimming.

**Bayer4** (indexed `[py & 3][px & 3]`): `{ {0, 8, 2, 10}, {12, 4, 14, 6}, {3, 11, 1, 9}, {15, 7, 13, 5} }`.
Braille dot (dx, dy) → bit: (0,0) 0x01, (0,1) 0x02, (0,2) 0x04, (1,0) 0x08, (1,1) 0x10, (1,2) 0x20,
(0,3) 0x40, (1,3) 0x80; character = `(char)(0x2800 + bits)`.

**Time model:** `Reset` gets DeltaTime 0 and Time = the scene time (0 at construction, the current
time on `Resize`). Headless stepping calls `Update` with DeltaTime = 1/30 and Time = k/30 for step
k; live, the ConsoleHost passes the measured dt. A from-scene keeps stepping past its own Bars until
the transition ends. The seed of `DemoRunner` is 1.

**Scene table** (`DemoScript.Scenes`, in this order; `Transition` is the transition into the scene):

| # | Name | Mode | Effects (back to front) | Bars | Transition | TransitionBars |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | boot | Braille | metaballs, matrix | 8 | crossfade | 1 |
| 1 | title | HalfBlock | plasma, scroller | 10 | dissolve | 1 |
| 2 | tunnel | HalfBlock | tunnel, cube | 10 | wipe | 1 |
| 3 | fire | HalfBlock | fire | 8 | crossfade | 1 |
| 4 | roto | HalfBlock | rotozoomer | 8 | push | 1 |
| 5 | flight | HalfBlock | starfield, voxel | 10 | dissolve | 1 |
| 6 | mandel | HalfBlock | mandelbrot | 10 | crossfade | 1 |
| 7 | spheres | Braille | raymarch | 10 | wipe | 1 |

**Song format** (`SongParser`; `;` starts a comment to the end of the line, anywhere (`#` is a sharp, never a comment); blank lines are ignored; a row is split on `|` and each cell trimmed; tokens inside a cell or directive are separated by spaces; names match `[A-Za-z0-9_]+`):

```
bpm <int>                                                      once, before the first pattern
rows-per-beat <int>                                            once, before the first pattern
instrument <name> duty=<0.125|0.25|0.5> vol=<0..1> decay=<0..>   decay in volume units per second; before the first pattern
pattern <name>                                                 rows follow until "end"
<cell> | <cell> | <cell> | <cell>                              channels 0 pulse, 1 pulse, 2 triangle, 3 noise
end
order <name> [<name> ...]                                      pattern names, after all patterns; several order lines append
cell := --- | off | <note> <instrument> [v<0-9>] [a<X><Y>]     X, Y hex digits = semitones
note := [A-G](#|-)[0-8]                                        C-4 = 60, A-4 = 440 Hz; on the noise channel it sets the LFSR rate
```

**Usage text** (stderr on error, exit 2):

```
Usage: AnsiDemo [--no-sound] [--loop] [--scene <name> | --effect <name>]
       AnsiDemo --frame <seconds> [--scene <name> | --effect <name>] [--size <cols>x<rows>]
       AnsiDemo --wav <path>
Keys: Q/Esc quit, Space pause, Left/Right previous/next scene
```

### 4.4 Error handling

Contract violations (bad sizes, unknown names, bad channels) throw `ArgumentException` at the call
site; effects and transitions never throw in `Update` or `Render` for valid sizes. `SongParser`
reports the first problem with its line. The ConsoleHost wraps the whole run in `try/finally` that
restores the terminal, then prints the exception message to stderr and exits 1. A missing or failing
audio device never stops the demo (10.4). Tests must not leave the alternate screen active: no test
calls `ConsoleHost.Run`.

## 5. Build order

1. contracts.
2. In parallel: core, audio, tracker, transitions.
3. In parallel: the 12 effect modules (after core) and soundtrack (after tracker and audio).
4. Last: demo.

## 6. Verification

- **Setup command:** `dotnet restore AnsiDemo.slnx`
- **Test runner and layout:** xUnit 2.9 in `tests/AnsiDemo.Tests`, tests in `tests/AnsiDemo.Tests/<Module>/*Tests.cs` with namespace `AnsiDemo.Tests.<Module>` (effects: `AnsiDemo.Tests.Effects.<Folder>`). Goldens are `.ansi` files beside the tests, found through `[CallerFilePath]`, so no project file entries. Test parallelization is off assembly-wide (bootstrap, section 7). Perf tests carry `[Trait("Category", "Perf")]`. Device tests (7.1) carry `[Trait("Category", "Device")]` and return early when `WaveOutSink.DeviceCount` is 0.
- **Per-module check:** `dotnet test AnsiDemo.slnx --filter "FullyQualifiedName~AnsiDemo.Tests.<Folder>"` (Folder = Contracts, Core, Audio, Tracker, Transitions, Music, Effects.<Folder>, Demo).
- **Whole-project check:** `dotnet build AnsiDemo.slnx -warnaserror && dotnet test AnsiDemo.slnx --no-build`
- **Manual checks:** `dotnet run --project src/AnsiDemo` in a maximized Windows Terminal: music starts with the picture, 8 scenes with transitions, keys work, the prompt is clean afterwards. `dotnet run --project src/AnsiDemo -- --frame 20` shows one frame; `Get-Content tests/AnsiDemo.Tests/Effects/Plasma/t4.ansi` shows a golden. 2.1-2.4, 10.1 and 10.3-10.5 are checked by review and these manual checks only: no test calls `ConsoleHost.Run`.

## 7. Constraints

- **Stack:** C# 13 on .NET SDK 10 (`net10.0`), file-scoped namespaces, nullable and implicit usings enabled, `InvariantGlobalization` true. One console project `src/AnsiDemo/AnsiDemo.csproj` (`OutputType` Exe, `AssemblyName` AnsiDemo) and `tests/AnsiDemo.Tests/AnsiDemo.Tests.csproj` referencing it, in `AnsiDemo.slnx`. Packages: `Microsoft.NET.Test.Sdk`, `xunit` 2.9.x, `xunit.runner.visualstudio` only. P/Invoke: `winmm.dll` in `Audio/**`, `kernel32.dll` (console modes) in `Shell/**`. No other dependencies.
- **Conventions:** one type per file; `double` math; `CultureInfo.InvariantCulture` for parsing and formatting; `Console` only in `src/AnsiDemo/Shell/**` and `Program.cs`; tests are `public class <Name>Tests` with `[Fact]`/`[Theory]`, following the skeleton's smoke test. Allocate in `Reset`; keep `Update` and `Render` allocation-free in the steady state.
- **Shared files:** none during the parallel phases. No `global using` directives outside the bootstrap. The bootstrap creates `AnsiDemo.slnx`, both project files, `.gitignore` (standard dotnet `bin/`, `obj/`, plus `*.actual.ansi`), `README.md`, `tests/AnsiDemo.Tests/SmokeTests.cs`, `tests/AnsiDemo.Tests/AssemblyInfo.cs` with `[assembly: CollectionBehavior(DisableTestParallelization = true)]`, and a placeholder `src/AnsiDemo/Program.cs` that prints one line; no task edits the solution or a project file.

**Always**
- Run the module check and `dotnet build AnsiDemo.slnx -warnaserror` before finishing.
- Record goldens by running the tests, view every `.ansi` with `Get-Content`, and fix the effect until it matches its "Must look like" line before committing.
- Keep every signature in 4.3 as written; add members only inside your own module.

**Stop and report blocked**
- The .NET 10 SDK is missing, a requirement cannot be met without changing a signature in 4.3, or an effect cannot meet 4.5 at any internal resolution.

**Never**
- Add NuGet packages, edit files outside your module's paths, or change `.slnx`/`.csproj` files.
- Use `System.Random`, the clock or `Console` in contracts, core, effects or transitions.
- Run `ConsoleHost` or open a wave-out device from a test other than the device tests of 7.1.
- Write outside the repository and the OS temp directory during tests.

## 8. Open questions

None.


