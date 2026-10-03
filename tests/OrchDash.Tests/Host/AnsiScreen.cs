using System.Globalization;
using System.Text;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;

namespace OrchDash.Tests.Host;

/// <summary>
/// A minimal terminal screen that interprets the ANSI output of a fullscreen XenoAtom app: cursor positioning,
/// erase, the alternate screen and wide text elements. Styles, links and terminal modes are ignored.
/// </summary>
/// <remarks>
/// The screen models the surface the app draws on. Leaving the alternate screen freezes it, so the last
/// frame of an app stays readable after the app has ended. All members are thread-safe.
/// </remarks>
internal sealed class AnsiScreen : TextWriter
{
    private const string Continuation = "";

    private enum ParserState { Ground, Escape, Charset, Csi, OscString, OscEscape }

    private readonly Lock _lock = new();
    private readonly string[][] _cells;
    private readonly StringBuilder _pendingText = new();
    private readonly StringBuilder _parameters = new();
    private ParserState _state;
    private int _row;
    private int _column;
    private int _savedRow;
    private int _savedColumn;
    private string _lastElement = " ";
    private bool _frozen;
    private long _writes;

    public AnsiScreen(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
        _cells = new string[height][];
        for (var row = 0; row < height; row++)
        {
            _cells[row] = new string[width];
        }
        ClearAll();
    }

    public int Width { get; }

    public int Height { get; }

    public override Encoding Encoding => Encoding.UTF8;

    /// <summary>The number of writes so far; it changes whenever output arrives.</summary>
    public long Writes
    {
        get
        {
            lock (_lock)
            {
                return _writes;
            }
        }
    }

    public override void Write(char value)
    {
        lock (_lock)
        {
            _writes++;
            Feed(value);
        }
    }

    public override void Write(char[] buffer, int index, int count) => Write(buffer.AsSpan(index, count));

    public override void Write(string? value) => Write(value.AsSpan());

    public override void Write(ReadOnlySpan<char> buffer)
    {
        lock (_lock)
        {
            _writes++;
            foreach (var c in buffer)
            {
                Feed(c);
            }
        }
    }

    /// <summary>Returns the screen rows, each without trailing spaces.</summary>
    public string[] Snapshot()
    {
        lock (_lock)
        {
            FlushText();
            var rows = new string[Height];
            var line = new StringBuilder(Width);
            for (var row = 0; row < Height; row++)
            {
                line.Clear();
                foreach (var cell in _cells[row])
                {
                    line.Append(cell);
                }
                rows[row] = line.ToString().TrimEnd(' ');
            }
            return rows;
        }
    }

    /// <summary>Returns the cell column at which the character at <paramref name="index"/> of a snapshot row is shown.</summary>
    public static int CellColumn(string row, int index)
    {
        var column = 0;
        var position = 0;
        while (position < index)
        {
            var length = StringInfo.GetNextTextElementLength(row, position);
            column += TerminalTextUtility.GetWidth(row.AsSpan(position, length), TerminalWideRuneResolvers.Default);
            position += length;
        }
        return column;
    }

    private void Feed(char c)
    {
        switch (_state)
        {
            case ParserState.Ground:
                if (c == '\u001b')
                {
                    FlushText();
                    _state = ParserState.Escape;
                }
                else if (char.IsControl(c))
                {
                    FlushText();
                    ExecuteControl(c);
                }
                else
                {
                    _pendingText.Append(c);
                }
                break;
            case ParserState.Escape:
                ExecuteEscape(c);
                break;
            case ParserState.Charset:
                _state = ParserState.Ground;
                break;
            case ParserState.Csi:
                if (c is >= '@' and <= '~')
                {
                    ExecuteCsi(c);
                    _state = ParserState.Ground;
                }
                else if (c == '\u001b')
                {
                    _state = ParserState.Escape;
                }
                else
                {
                    _parameters.Append(c);
                }
                break;
            case ParserState.OscString:
                if (c == '\u0007')
                {
                    _state = ParserState.Ground;
                }
                else if (c == '\u001b')
                {
                    _state = ParserState.OscEscape;
                }
                break;
            case ParserState.OscEscape:
                _state = c == '\\' ? ParserState.Ground : ParserState.OscString;
                break;
        }
    }

    private void ExecuteControl(char c)
    {
        switch (c)
        {
            case '\r':
                _column = 0;
                break;
            case '\n':
                LineFeed();
                break;
            case '\b':
                _column = Math.Max(0, Math.Min(_column, Width - 1) - 1);
                break;
            case '\t':
                _column = Math.Min(Width - 1, (_column / 8 + 1) * 8);
                break;
        }
    }

    private void ExecuteEscape(char c)
    {
        _state = ParserState.Ground;
        switch (c)
        {
            case '[':
                _parameters.Clear();
                _state = ParserState.Csi;
                break;
            case ']':
            case 'P':
            case '_':
            case '^':
                _state = ParserState.OscString;
                break;
            case '(':
            case ')':
            case '*':
            case '+':
                _state = ParserState.Charset;
                break;
            case '7':
                SaveCursor();
                break;
            case '8':
                RestoreCursor();
                break;
            case 'D':
                LineFeed();
                break;
            case 'E':
                _column = 0;
                LineFeed();
                break;
            case 'M':
                _row = Math.Max(0, _row - 1);
                break;
            case 'c':
                ClearAll();
                _row = 0;
                _column = 0;
                break;
        }
    }

    private void ExecuteCsi(char final)
    {
        var text = _parameters.ToString();
        var isPrivate = text.StartsWith('?');
        var args = (isPrivate ? text[1..] : text).Split(';');

        switch (final)
        {
            case 'H':
            case 'f':
                MoveTo(Arg(args, 0, 1) - 1, Arg(args, 1, 1) - 1);
                break;
            case 'A':
                MoveTo(_row - Arg(args, 0, 1), _column);
                break;
            case 'B':
                MoveTo(_row + Arg(args, 0, 1), _column);
                break;
            case 'C':
                MoveTo(_row, _column + Arg(args, 0, 1));
                break;
            case 'D':
                MoveTo(_row, _column - Arg(args, 0, 1));
                break;
            case 'E':
                MoveTo(_row + Arg(args, 0, 1), 0);
                break;
            case 'F':
                MoveTo(_row - Arg(args, 0, 1), 0);
                break;
            case 'G':
            case '`':
                MoveTo(_row, Arg(args, 0, 1) - 1);
                break;
            case 'd':
                MoveTo(Arg(args, 0, 1) - 1, _column);
                break;
            case 'J':
                EraseInDisplay(Arg(args, 0, 0));
                break;
            case 'K':
                EraseInLine(Arg(args, 0, 0));
                break;
            case 'X':
                Erase(_row, _column, Math.Min(Width, _column + Arg(args, 0, 1)));
                break;
            case 'b':
                for (var i = Arg(args, 0, 1); i > 0; i--)
                {
                    Put(_lastElement);
                }
                break;
            case 's':
                SaveCursor();
                break;
            case 'u':
                RestoreCursor();
                break;
            case 'h' when isPrivate:
                SetPrivateModes(args, enabled: true);
                break;
            case 'l' when isPrivate:
                SetPrivateModes(args, enabled: false);
                break;
        }
    }

    private static int Arg(string[] args, int index, int defaultValue)
    {
        if (index < args.Length && int.TryParse(args[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return value;
        }
        return index < args.Length && args[index] == "0" ? 0 : defaultValue;
    }

    private void SetPrivateModes(string[] args, bool enabled)
    {
        foreach (var arg in args)
        {
            if (arg is "1049" or "1047" or "47")
            {
                if (enabled)
                {
                    _frozen = false;
                    ClearAll();
                }
                else
                {
                    _frozen = true;
                }
            }
        }
    }

    private void FlushText()
    {
        if (_pendingText.Length == 0)
        {
            return;
        }
        var text = _pendingText.ToString();
        _pendingText.Clear();
        var position = 0;
        while (position < text.Length)
        {
            var length = StringInfo.GetNextTextElementLength(text, position);
            Put(text.Substring(position, length));
            position += length;
        }
    }

    private void Put(string element)
    {
        var width = TerminalTextUtility.GetWidth(element, TerminalWideRuneResolvers.Default);
        if (width == 0)
        {
            AppendToPreviousCell(element);
            return;
        }
        width = Math.Min(width, 2);
        if (_column + width > Width)
        {
            _column = 0;
            LineFeed();
        }
        _lastElement = element;
        if (_frozen)
        {
            _column += width;
            return;
        }
        var row = _cells[_row];
        for (var column = _column; column < _column + width; column++)
        {
            ClearWideNeighbour(row, column);
        }
        row[_column] = element;
        if (width == 2)
        {
            row[_column + 1] = Continuation;
        }
        _column += width;
    }

    private void AppendToPreviousCell(string element)
    {
        if (_frozen || _column == 0)
        {
            return;
        }
        var row = _cells[_row];
        var column = Math.Min(_column, Width) - 1;
        if (row[column].Length == 0 && column > 0)
        {
            column--;
        }
        row[column] += element;
    }

    /// <summary>Blanks the other half of a wide element that overlaps <paramref name="column"/>.</summary>
    private void ClearWideNeighbour(string[] row, int column)
    {
        if (row[column].Length == 0 && column > 0)
        {
            row[column - 1] = " ";
        }
        if (column + 1 < Width && row[column + 1].Length == 0)
        {
            row[column + 1] = " ";
        }
    }

    private void LineFeed()
    {
        if (_row < Height - 1)
        {
            _row++;
            return;
        }
        if (_frozen)
        {
            return;
        }
        var top = _cells[0];
        Array.Copy(_cells, 1, _cells, 0, Height - 1);
        Array.Fill(top, " ");
        _cells[Height - 1] = top;
    }

    private void MoveTo(int row, int column)
    {
        _row = Math.Clamp(row, 0, Height - 1);
        _column = Math.Clamp(column, 0, Width - 1);
    }

    private void SaveCursor()
    {
        _savedRow = _row;
        _savedColumn = _column;
    }

    private void RestoreCursor() => MoveTo(_savedRow, _savedColumn);

    private void EraseInDisplay(int mode)
    {
        switch (mode)
        {
            case 0:
                Erase(_row, Math.Min(_column, Width), Width);
                for (var row = _row + 1; row < Height; row++)
                {
                    Erase(row, 0, Width);
                }
                break;
            case 1:
                for (var row = 0; row < _row; row++)
                {
                    Erase(row, 0, Width);
                }
                Erase(_row, 0, Math.Min(_column + 1, Width));
                break;
            default:
                if (!_frozen)
                {
                    ClearAll();
                }
                break;
        }
    }

    private void EraseInLine(int mode)
    {
        switch (mode)
        {
            case 0:
                Erase(_row, Math.Min(_column, Width), Width);
                break;
            case 1:
                Erase(_row, 0, Math.Min(_column + 1, Width));
                break;
            default:
                Erase(_row, 0, Width);
                break;
        }
    }

    private void Erase(int row, int from, int to)
    {
        if (_frozen || from >= to)
        {
            return;
        }
        var cells = _cells[row];
        ClearWideNeighbour(cells, from);
        ClearWideNeighbour(cells, to - 1);
        Array.Fill(cells, " ", from, to - from);
    }

    private void ClearAll()
    {
        foreach (var row in _cells)
        {
            Array.Fill(row, " ");
        }
    }
}
