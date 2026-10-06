using System.Collections.Immutable;
using OrchDash.Contracts;
using OrchDash.Core.Model;
using OrchDash.Pages.Conversation;
using OrchDash.Pages.GitView.Format;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace OrchDash.Pages.GitView;

/// <summary>
/// The visuals and the selection state of one <see cref="GitPage"/>. Everything shown is a function of
/// <see cref="IAppContext.Snapshot"/> and the task, commit and file the user selected last, kept by id, sha and path
/// so that a new snapshot keeps them (25.7); the rows are cached until the snapshot or the selected task changes.
/// </summary>
internal sealed class GitView
{
    private const int MarkerWidth = 2;        // the selection marker of a list row and a space
    private const int TaskShare = 3;          // the task table takes 3 parts of the height, the commits and files 2
    private const int ListShare = 2;

    private readonly IAppContext _context;
    private readonly State<string?> _taskId = new(null);
    private readonly State<string?> _commitSha = new(null);
    private readonly State<string?> _filePath = new(null);
    private readonly SelectableList _taskList;
    private readonly SelectableList _commitList;
    private readonly SelectableList _fileList;
    private readonly ScrollViewer[] _focusOrder;

    // The keys of the task, commit and file shown last (see Find).
    private string? _shownTaskId;
    private string? _shownSha;
    private string? _shownPath;

    // Whether the click being handled hit the task that was already selected; null outside a click (see SelectTask).
    private bool? _taskClickOnSelected;

    private RunSnapshot? _tasksFor;
    private ImmutableArray<(GitTask Git, TaskView Task)> _tasks = [];
    private ImmutableArray<ImmutableArray<string>> _taskRows = [];
    private string _taskHeader = "";
    private GitTask? _rowsFor;
    private ImmutableArray<ImmutableArray<string>> _commitRows = [];
    private ImmutableArray<GitFileRow> _files = [];
    private ImmutableArray<ImmutableArray<string>> _fileRows = [];

    public GitView(IAppContext context)
    {
        _context = context;
        _taskList = new SelectableList(
            "git.tasks", TaskRows, TaskSelection, SelectTask, OpenTask,
            activateLabel: "Diff", activateOnClick: true, emptyText: GitText.NoPlan);
        _commitList = new SelectableList(
            "git.commits", CommitRows, CommitSelection, SelectCommit, OpenCommit,
            activateLabel: "Commit", activateOnClick: true, emptyText: GitText.NoCommits);
        _fileList = new SelectableList(
            "git.files", FileRows, FileSelection, SelectFile, OpenFile,
            activateLabel: "File diff", activateOnClick: true, emptyText: GitText.NoChanges);
        _taskList.View.AutoFocus(true);
        _focusOrder = [_taskList.View, _commitList.View, _fileList.View];

        var lists = new Grid()
            .Rows(
                new RowDefinition().Height(GridLength.Star(TaskShare)),
                new RowDefinition().Height(GridLength.Star(ListShare)))
            .Columns(
                new ColumnDefinition().Width(GridLength.Star()),
                new ColumnDefinition().Width(GridLength.Star()))
            .Cell(Pane("Tasks", TaskTable()), 0, 0, columnSpan: 2)
            .Cell(Pane("Commits", _commitList.View), 1, 0)
            .Cell(Pane("Files", _fileList.View), 1, 1);
        Root = new DockLayout()
            .Top(new Markup(() => string.Join('\n', GitText.HeaderLines(_context.Snapshot.Value.Git))))
            .Content(lists);
        AddCommands(Root);
    }

    public Visual Root { get; }

    private static Group Pane(string title, Visual content) =>
        new Group().TopLeftText(new Markup(Look.Tag("bold", title))).Content(content).Stretch();

    /// <summary>
    /// 25.2: the column titles, lined up with the rows after the selection marker, above the task list; left out while
    /// the list shows "No plan yet".
    /// </summary>
    private Visual TaskTable() => new VStack(
        new Markup(() => new string(' ', MarkerWidth) + TaskHeader())
            .IsSelectable(false)
            .IsVisible(() => !Tasks().IsEmpty),
        _taskList.View.Stretch());

    /// <summary>
    /// The page's keys, on the root so that they work from each of the three lists: Tab and Shift+Tab move the focus
    /// (25.7).
    /// </summary>
    private void AddCommands(Visual root)
    {
        root.AddCommand(new Command
        {
            Id = "git.switch",
            LabelMarkup = "Switch list",
            Gesture = new KeyGesture(TerminalKey.Tab),
            Execute = _ => MoveFocus(1),
        });
        root.AddCommand(new Command
        {
            Id = "git.switchback",
            LabelMarkup = "Switch list",
            Gesture = new KeyGesture(TerminalKey.Tab, TerminalModifiers.Shift),
            Presentation = CommandPresentation.None,
            Execute = _ => MoveFocus(-1),
        });
    }

    /// <summary>Moves the focus to the next (1) or previous (-1) of task list, commit list and file list.</summary>
    private void MoveFocus(int direction)
    {
        var current = Array.FindIndex(_focusOrder, view => view.HasFocus);
        var next = current >= 0 ? (current + direction + _focusOrder.Length) % _focusOrder.Length
            : direction > 0 ? 0
            : _focusOrder.Length - 1;
        Root.App?.Focus(_focusOrder[next]);
    }

    /// <summary>
    /// The snapshot's tasks in their order, each with its git state; a task the git read has not reached yet gets an
    /// empty one. Empty while there is no plan (25.2).
    /// </summary>
    private ImmutableArray<(GitTask Git, TaskView Task)> Tasks()
    {
        var snapshot = _context.Snapshot.Value;
        if (!ReferenceEquals(snapshot, _tasksFor))
        {
            var git = snapshot.Git.Tasks.ToDictionary(g => g.TaskId, StringComparer.Ordinal);
            _tasks = snapshot.Plan is null
                ? []
                : [.. snapshot.Tasks.Select(t => (git.GetValueOrDefault(t.Id) ?? Unread(t.Id), t))];
            var widths = GitText.Widths(_tasks.Select(p => GitText.TaskCells(p.Git, p.Task)));
            _taskRows = [.. _tasks.Select(p => ImmutableArray.Create(GitText.TaskRow(p.Git, p.Task, widths)))];
            _taskHeader = GitText.TaskHeader(widths);
            _tasksFor = snapshot;
        }
        return _tasks;
    }

    /// <summary>The git state of a task before git has been read: nothing known.</summary>
    private static GitTask Unread(string taskId) =>
        new(taskId, null, null, null, false, [], null, null, [], null, [], null, null);

    private ImmutableArray<ImmutableArray<string>> TaskRows()
    {
        Tasks();
        return _taskRows;
    }

    private string TaskHeader()
    {
        Tasks();
        return _taskHeader;
    }

    /// <summary>The task with the selected id, else the one shown last, else the first one; -1 without tasks.</summary>
    private int SelectedTaskIndex() => Find(Tasks(), p => p.Task.Id, _taskId.Value, ref _shownTaskId);

    /// <summary>
    /// The index of the item whose key is <paramref name="picked"/>, else of the one shown last, else 0; -1 without
    /// items. Remembering the shown key keeps an item the user never picked, such as the first task at the start, when
    /// a new snapshot moves it.
    /// </summary>
    private static int Find<T>(ImmutableArray<T> items, Func<T, string> key, string? picked, ref string? shown)
    {
        if (items.IsEmpty)
        {
            return -1;
        }
        var index = IndexOf(items, key, picked);
        if (index < 0)
        {
            index = Math.Max(0, IndexOf(items, key, shown));
        }
        shown = key(items[index]);
        return index;
    }

    private static int IndexOf<T>(ImmutableArray<T> items, Func<T, string> key, string? value)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (key(items[i]) == value)
            {
                return i;
            }
        }
        return -1;
    }

    private GitTask? SelectedGit() => SelectedTaskIndex() is var index and >= 0 ? Tasks()[index].Git : null;

    private ListSelection TaskSelection() => new(null, SelectedTaskIndex());

    /// <summary>
    /// The task list opens the diff on Enter and on a click on the selected row, while a click on another row only
    /// selects it (25.4). The list calls <c>select</c> and then <c>activate</c> for a click, but Enter calls only
    /// <c>activate</c>; so <c>select</c> notes whether the row was already selected, <see cref="OpenTask"/> skips the
    /// diff when it was not, and the note is cleared once the click has been handled. Up and Down also come here and
    /// leave a note that nothing reads.
    /// </summary>
    private void SelectTask(int index)
    {
        _taskClickOnSelected = index == SelectedTaskIndex();
        Root.App?.Post(() => _taskClickOnSelected = null);
        _taskId.Value = Tasks()[index].Task.Id;
    }

    private void OpenTask(int index)
    {
        if (_taskClickOnSelected is false)
        {
            return;
        }
        var git = Tasks()[index].Git;
        _context.ShowPopup(GitText.DiffTitle(git.TaskId), GitText.DiffPopup(git));
    }

    /// <summary>The commit and file rows of the selected task, cached per git state of the task.</summary>
    private void SelectedRows()
    {
        var git = SelectedGit();
        if (git is null)
        {
            _rowsFor = null;
            _commitRows = [];
            _files = [];
            _fileRows = [];
        }
        else if (!ReferenceEquals(git, _rowsFor))
        {
            _commitRows = [.. git.Commits.Select(c => ImmutableArray.Create(GitText.CommitRow(c)))];
            _files = GitText.FileRows(git);
            _fileRows = [.. _files.Select(f => ImmutableArray.Create(f.Text))];
            _rowsFor = git;
        }
    }

    private ImmutableArray<ImmutableArray<string>> CommitRows()
    {
        SelectedRows();
        return _commitRows;
    }

    /// <summary>
    /// The commit with the selected sha within the selected task, else the one shown last, else its first; -1 without
    /// commits.
    /// </summary>
    private ListSelection CommitSelection()
    {
        var git = SelectedGit();
        var commits = git?.Commits ?? [];
        return new ListSelection(git?.TaskId, Find(commits, c => c.Sha, _commitSha.Value, ref _shownSha));
    }

    private void SelectCommit(int index)
    {
        if (SelectedGit() is { } git)
        {
            _commitSha.Value = git.Commits[index].Sha;
        }
    }

    private void OpenCommit(int index)
    {
        if (SelectedGit() is { } git && index < git.Commits.Length)
        {
            var commit = git.Commits[index];
            _context.ShowPopup(GitText.CommitTitle(commit), GitText.CommitPopup(commit));
        }
    }

    private ImmutableArray<GitFileRow> Files()
    {
        SelectedRows();
        return _files;
    }

    private ImmutableArray<ImmutableArray<string>> FileRows()
    {
        SelectedRows();
        return _fileRows;
    }

    /// <summary>
    /// The file with the selected path within the selected task, else the one shown last, else its first; -1 without
    /// files.
    /// </summary>
    private ListSelection FileSelection() =>
        new(SelectedGit()?.TaskId, Find(Files(), f => f.Path, _filePath.Value, ref _shownPath));

    private void SelectFile(int index) => _filePath.Value = Files()[index].Path;

    private void OpenFile(int index)
    {
        var files = Files();
        if (SelectedGit() is { } git && index < files.Length)
        {
            var file = files[index];
            _context.ShowPopup(GitText.FileDiffTitle(git.TaskId, file.Path), GitText.FileDiffPopup(git, file));
        }
    }
}
