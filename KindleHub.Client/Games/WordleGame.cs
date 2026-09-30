using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Wordle — guess the word in six tries, with green/amber/grey per letter.
///
/// The 6x5 board sits on top and the QWERTY keyboard underneath, all in one grid
/// so it still renders through the shared board view.
/// </summary>
public sealed class WordleGame : GameBase, IWordEntry
{
    private const int Rows = 6;
    private const int Cols = 5;

    private static readonly string[] Answers =
    {
        "CRANE","SLATE","TRACE","AUDIO","ROUTE","STONE","LIGHT","MOUNT","TRAIN",
        "SHARE","PLANT","GRAPE","HOUSE","FROST","SPARK","BLADE","CHARM","FLAME",
    };

    private const string Keyboard =
        "QWERTYUIOP" +
        "ASDFGHJKL" +
        "ZXCVBNM";

    // The board is 5 squares wide but a QWERTY row is 10, so the whole grid is
    // KeyboardCols wide and the board is centred inside it. This used to be
    // Columns (5), which silently pushed 11 letters and the Enter key past the end
    // of the cell list — EnterCell resolved to index 61 in a 50-cell board, so
    // there was no way to submit a guess by tapping.
    private const int KeyboardCols = 10;
    private const int BoardOffset = (KeyboardCols - Cols) / 2;   // 2

    public const int TotalColumns = KeyboardCols;

    private enum Mark { None, Correct, Present, Absent }

    private readonly List<GameCell> _cells = new();
    private readonly char[,] _board = new char[Rows, Cols];
    private readonly Mark[,] _marks = new Mark[Rows, Cols];
    private readonly Dictionary<char, Mark> _keyState = new();
    private string _answer = "";
    private int _row;
    private int _col;
    private bool _won;

    public WordleGame()
    {
        // Board rows, one gutter row, then three keyboard rows, all KeyboardCols wide.
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < KeyboardCols; c++)
                _cells.Add(Blank());
        for (int c = 0; c < KeyboardCols; c++) _cells.Add(Blank());          // gutter row
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < KeyboardCols; c++)
                _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "wordle";
    public override string Name => "Wordle";
    public override int Columns => TotalColumns;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override string? ResultText
    {
        get
        {
            if (_won) return $"Solved in {_row + 1}.";
            return _row >= Rows ? $"Out of guesses — it was {_answer}." : null;
        }
    }

    public override string StatusText
    {
        get
        {
            if (_won) return $"Correct — {_answer} in {_row + 1}.";
            if (_row >= Rows) return $"The word was {_answer}.";
            return _col == 0 ? $"Guess {_row + 1} of {Rows} — tap letters below."
                              : $"Guess {_row + 1} of {Rows} — press Enter to submit.";
        }
    }

    /// <summary>Where the keyboard block starts inside <see cref="Cells"/>.</summary>
    private static int KeyboardBase => Rows * KeyboardCols + KeyboardCols;

    /// <summary>The cell index of the Enter key, for the view and for tests.</summary>
    public static int EnterCell => KeyboardBase + 26;

    /// <summary>The cell index of the backspace key.</summary>
    public static int BackspaceCell => EnterCell - 1;

    /// <summary>The cell index of a board square, which is centred in the wide grid.</summary>
    public static int BoardCell(int row, int col) => row * KeyboardCols + BoardOffset + col;

    /// <summary>The cell index of a letter key, or -1 if it isn't on the board.</summary>
    public static int IndexOfKey(char key)
    {
        int at = Keyboard.IndexOf(char.ToUpperInvariant(key));
        return at < 0 ? -1 : KeyboardBase + at;
    }

    /// <summary>The key at a keyboard cell, or '\0' for the padding blanks.</summary>
    public static char KeyAt(int index)
    {
        int k = index - KeyboardBase;
        return k >= 0 && k < Keyboard.Length ? Keyboard[k] : '\0';
    }

    public override void Reset()
    {
        _answer = Answers[Random.Shared.Next(Answers.Length)];
        Array.Clear(_board, 0, _board.Length);
        Array.Clear(_marks, 0, _marks.Length);
        _keyState.Clear();
        _row = 0;
        _col = 0;
        _won = false;
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (_won || _row >= Rows) return false;

        if (index < Rows * KeyboardCols)
        {
            int r = index / KeyboardCols, c = index - BoardOffset - r * KeyboardCols;
            if (r == _row && c >= 0 && c < Cols) _col = Math.Clamp(c, 0, Cols - 1);
            Redraw();
            return true;
        }

        // Submit lives on a padding blank at the end of the bottom keyboard row,
        // so it never steals a letter from QWERTY. It has to be tested BEFORE the
        // blank check, because that slot legitimately has no letter on it.
        if (IsEnterCell(index)) return Submit();
        if (IsBackspaceCell(index)) return Backspace();

        char key = KeyAt(index);
        if (key == '\0') return false;
        return Type(key);
    }

    private static bool IsEnterCell(int index) => index == EnterCell;

    private static bool IsBackspaceCell(int index) => index == BackspaceCell;

    /// <summary>Types one letter into the current guess, if there is room.</summary>
    private bool Type(char key)
    {
        if (_won || _row >= Rows) return false;
        if (_col < Cols)
        {
            _board[_row, _col++] = key;
            Redraw();
        }
        return true;
    }

    /// <summary>
    /// Deletes the last letter. This exists on the physical keyboard and as a
    /// visible key — without it a mistyped guess could not be corrected even by
    /// tapping, which is what made Wordle feel broken rather than merely awkward.
    /// </summary>
    public override bool OnChar(char c)
    {
        char up = char.ToUpperInvariant(c);
        return char.IsAsciiLetterUpper(up) && Type(up);
    }

    public override bool OnBackspace()
    {
        if (_won || _row >= Rows || _col == 0) return false;
        _col--;
        _board[_row, _col] = '\0';
        Redraw();
        return true;
    }

    private bool Backspace() => OnBackspace();

    /// <summary>Submits the current row. Public so Enter on a physical keyboard can reach it.</summary>
    public bool OnSubmit() => Submit();

    private bool Submit()
    {
        if (_col < Cols) return false;   // need a full word

        var guess = new string(Enumerable.Range(0, Cols).Select(c => _board[_row, c]).ToArray());
        var remaining = _answer.ToCharArray().ToList();

        // First pass: exact matches consume both sides.
        for (int c = 0; c < Cols; c++)
        {
            if (guess[c] != _answer[c]) continue;
            _marks[_row, c] = Mark.Correct;
            remaining[c] = '\0';
        }
        // Second pass: present-but-misplaced, honouring counts.
        for (int c = 0; c < Cols; c++)
        {
            if (_marks[_row, c] == Mark.Correct) continue;
            int at = remaining.IndexOf(guess[c]);
            if (at >= 0) { _marks[_row, c] = Mark.Present; remaining[at] = '\0'; }
            else _marks[_row, c] = Mark.Absent;
        }

        for (int c = 0; c < Cols; c++)
        {
            char ch = guess[c];
            var m = _marks[_row, c];
            if (!_keyState.TryGetValue(ch, out var prev) || Rank(m) > Rank(prev)) _keyState[ch] = m;
        }

        if (guess == _answer) { _won = true; ScoreCounts = true; }
        else { _row++; if (_row >= Rows) ScoreCounts = false; }
        _col = 0;
        Redraw();
        return true;
    }

    private static int Rank(Mark m) => m switch
    {
        Mark.Correct => 3, Mark.Present => 2, Mark.Absent => 1, _ => 0,
    };

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Bold = true;
            c.FontSize = 18;
            c.IsEnabled = false;
            c.Foreground = Ink;
            c.Background = Board;

            if (i < Rows * KeyboardCols)
            {
                int r = i / KeyboardCols, raw = i - r * KeyboardCols;
                int cc = raw - BoardOffset;
                if (cc < 0 || cc >= Cols) continue;   // centring padding beside the board
                char ch = _board[r, cc];
                var m = _marks[r, cc];
                c.Text = ch == '\0' ? "" : ch.ToString();
                c.FontSize = 22;
                switch (m)
                {
                    case Mark.Correct: c.Background = Good; c.Foreground = Cell; break;
                    case Mark.Present: c.Background = Warn; c.Foreground = Cell; break;
                    case Mark.Absent: c.Background = Board; c.Foreground = Muted; break;
                    default:
                        // An empty square on the current row is the one you can type
                        // into, so it reads as fillable. "live" alone meant "has a
                        // letter in it", which left the row you're meant to fill
                        // the same colour as the padding around the board.
                        bool fillable = r == _row && !_won && _row < Rows;
                        c.Background = fillable ? Slot : Board;
                        c.Foreground = fillable ? Ink : Muted;
                        break;
                }
                continue;
            }

            if (i < Rows * KeyboardCols + KeyboardCols) { c.Text = ""; c.Background = Board; continue; } // gutter

            if (IsBackspaceCell(i)) { c.Text = "⌫"; c.FontSize = 15; c.IsEnabled = !_won && _row < Rows; continue; }
            if (IsEnterCell(i)) { c.Text = "↵"; c.FontSize = 15; c.IsEnabled = !_won && _row < Rows; continue; }

            char key = KeyAt(i);
            if (key == '\0') { c.Text = ""; c.Background = Board; continue; }
            c.Text = key.ToString();
            c.FontSize = 14;
            var st = _keyState.TryGetValue(key, out var s) ? s : Mark.None;
            c.Background = st switch
            {
                Mark.Correct => Good, Mark.Present => Warn, Mark.Absent => Board, _ => Slot,
            };
            c.Foreground = st == Mark.None ? Ink : st == Mark.Absent ? Muted : Cell;
            c.IsEnabled = !_won && _row < Rows;
        }
    }
}
