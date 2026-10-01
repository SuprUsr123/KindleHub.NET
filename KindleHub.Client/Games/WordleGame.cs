using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Wordle — guess the word in six tries, with green/amber/grey per letter.
///
/// The board is kept separate from the QWERTY keyboard in the underlying 10-column
/// grid so the desktop/touch keyboard has room to breathe instead of being squeezed
/// into the five board columns.
/// </summary>
public sealed class WordleGame : GameBase
{
    private const int Rows = 6;
    private const int BoardColumns = 10;
    private const int Cols = 5;

    private static readonly string[] Answers =
    {
        "CRANE","SLATE","TRACE","AUDIO","ADIEU","ROUTE","STONE","LIGHT","MOUNT","TRAIN",
        "SHARE","PLANT","GRAPE","HOUSE","FROST","SPARK","BLADE","CHARM","FLAME",
    };

    private const string KeyboardRow1 = "QWERTYUIOP";
    private const string KeyboardRow2 = "ASDFGHJKL";
    private const string KeyboardRow3 = "ZXCVBNM";

    public const int TotalColumns = BoardColumns;
    private const int BoardCellCount = Rows * BoardColumns;
    private const int KeyboardBase = BoardCellCount + BoardColumns;
    private const int KeyboardRows = 3;

    private enum Mark { None, Correct, Present, Absent }

    private readonly List<GameCell> _cells = new();
    private readonly char[,] _board = new char[Rows, Cols];
    private readonly Mark[,] _marks = new Mark[Rows, Cols];
    private readonly Dictionary<char, Mark> _keyState = new();
    private string _answer = "";
    private int _row;
    private int _col;
    private bool _won;
    private string _message = "";

    public WordleGame()
    {
        for (int i = 0; i < BoardCellCount + BoardColumns + KeyboardRows * BoardColumns; i++)
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
            if (!string.IsNullOrEmpty(_message)) return _message;
            return _col == 0
                ? $"Guess {_row + 1} of {Rows} — type A–Z or use the keyboard below."
                : $"Guess {_row + 1} of {Rows} — Enter to submit · Backspace to edit";
        }
    }

    public static int EnterCell => KeyboardBase + 20;
    public static int BackspaceCell => KeyboardBase + 29;

    /// <summary>The cell index of a letter key, or -1 if it isn't on the keyboard.</summary>
    public static int IndexOfKey(char key)
    {
        key = char.ToUpperInvariant(key);
        int at = KeyboardRow1.IndexOf(key);
        if (at >= 0) return KeyboardBase + at;
        at = KeyboardRow2.IndexOf(key);
        if (at >= 0) return KeyboardBase + BoardColumns + at;
        at = KeyboardRow3.IndexOf(key);
        if (at >= 0) return KeyboardBase + BoardColumns * 2 + 1 + at;
        return -1;
    }

    /// <summary>The key at a keyboard cell, or '\0' for padding/command cells.</summary>
    public static char KeyAt(int index)
    {
        int k = index - KeyboardBase;
        if (k >= 0 && k < BoardColumns) return KeyboardRow1[k];
        if (k >= BoardColumns && k < BoardColumns * 2)
        {
            int at = k - BoardColumns;
            return at < KeyboardRow2.Length ? KeyboardRow2[at] : '\0';
        }
        if (k >= BoardColumns * 2 && k < BoardColumns * 3)
        {
            int at = k - BoardColumns * 2 - 1;
            return at >= 0 && at < KeyboardRow3.Length ? KeyboardRow3[at] : '\0';
        }
        return '\0';
    }

    private static bool IsEnterCell(int index) => index == EnterCell;
    private static bool IsBackspaceCell(int index) => index == BackspaceCell;

    public override void Reset()
    {
        _answer = Answers[Random.Shared.Next(Answers.Length)];
        Array.Clear(_board, 0, _board.Length);
        Array.Clear(_marks, 0, _marks.Length);
        _keyState.Clear();
        _row = 0;
        _col = 0;
        _won = false;
        _message = "";
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnLetter(char letter)
    {
        if (_won || _row >= Rows) return false;
        letter = char.ToUpperInvariant(letter);
        if (letter < 'A' || letter > 'Z' || _col >= Cols) return false;

        _board[_row, _col++] = letter;
        _message = "";
        Redraw();
        return true;
    }

    public override bool OnBackspace()
    {
        if (_won || _row >= Rows || _col <= 0) return false;
        _col--;
        _board[_row, _col] = '\0';
        _message = "";
        Redraw();
        return true;
    }

    public override bool OnKey(GameKey key)
    {
        if (key == GameKey.Confirm) return Submit();
        if (key == GameKey.Backspace) return OnBackspace();
        return base.OnKey(key);
    }

    public override bool OnTap(int index)
    {
        if (_won || _row >= Rows || index < 0 || index >= _cells.Count) return false;

        if (index < BoardCellCount)
        {
            int r = index / BoardColumns;
            int visualCol = index % BoardColumns;
            int start = (BoardColumns - Cols) / 2;
            if (r != _row || visualCol < start || visualCol >= start + Cols) return false;
            _col = visualCol - start + 1;
            _col = Math.Clamp(_col, 0, Cols);
            Redraw();
            return true;
        }

        if (IsEnterCell(index)) return Submit();
        if (IsBackspaceCell(index)) return OnBackspace();

        char key = KeyAt(index);
        return key != '\0' && OnLetter(key);
    }

    private bool Submit()
    {
        if (_won || _row >= Rows || _col < Cols) return false;

        var guess = new string(Enumerable.Range(0, Cols)
            .Select(c => _board[_row, c]).ToArray());

        if (!WordleWordList.Words.Contains(guess) && !Answers.Contains(guess, StringComparer.Ordinal))
        {
            _message = $"{guess} isn't in the word list.";
            Redraw();
            return true;
        }

        _message = "";
        var remaining = _answer.ToCharArray().ToList();

        for (int c = 0; c < Cols; c++)
        {
            if (guess[c] != _answer[c]) continue;
            _marks[_row, c] = Mark.Correct;
            remaining[c] = '\0';
        }
        for (int c = 0; c < Cols; c++)
        {
            if (_marks[_row, c] == Mark.Correct) continue;
            int at = remaining.IndexOf(guess[c]);
            if (at >= 0)
            {
                _marks[_row, c] = Mark.Present;
                remaining[at] = '\0';
            }
            else _marks[_row, c] = Mark.Absent;
        }

        for (int c = 0; c < Cols; c++)
        {
            char ch = guess[c];
            var mark = _marks[_row, c];
            if (!_keyState.TryGetValue(ch, out var previous) || Rank(mark) > Rank(previous))
                _keyState[ch] = mark;
        }

        if (guess == _answer)
        {
            _won = true;
            ScoreCounts = true;
        }
        else
        {
            _row++;
            ScoreCounts = false;
        }
        _col = 0;
        Redraw();
        return true;
    }

    private static int Rank(Mark mark) => mark switch
    {
        Mark.Correct => 3,
        Mark.Present => 2,
        Mark.Absent => 1,
        _ => 0,
    };

    public override void Redraw()
    {
        int start = (BoardColumns - Cols) / 2;

        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Bold = true;
            c.FontSize = 18;
            c.IsEnabled = false;
            c.Foreground = Ink;
            c.Background = Board;
        }

        // Six 5-cell guesses, centered inside a 10-column board row.
        for (int r = 0; r < Rows; r++)
        {
            for (int visualCol = start; visualCol < start + Cols; visualCol++)
            {
                int cIndex = visualCol - start;
                var c = _cells[r * BoardColumns + visualCol];
                char ch = _board[r, cIndex];
                var mark = _marks[r, cIndex];
                bool live = r == _row && ch != '\0';
                c.Text = ch == '\0' ? "" : ch.ToString();
                c.FontSize = 22;
                c.Bold = true;

                switch (mark)
                {
                    case Mark.Correct: c.Background = Good; c.Foreground = Cell; break;
                    case Mark.Present: c.Background = Warn; c.Foreground = Cell; break;
                    case Mark.Absent: c.Background = Board; c.Foreground = Muted; break;
                    default:
                        c.Background = live ? Cell : Board;
                        c.Foreground = live ? Ink : Muted;
                        break;
                }
                c.IsEnabled = r == _row && !_won;
            }
        }

        // QWERTY keyboard: 10 / 9 / 7 keys, with Enter and Backspace flanking row 3.
        for (int col = 0; col < BoardColumns; col++)
        {
            DrawKey(KeyboardBase + col, KeyboardRow1[col]);
            if (col < BoardColumns)
                DrawKey(KeyboardBase + BoardColumns + col,
                        col < KeyboardRow2.Length ? KeyboardRow2[col] : '\0');

            int bottom = KeyboardBase + BoardColumns * 2 + col;
            if (col == 0) DrawCommandKey(bottom, "↵", IsEnterCell(bottom));
            else if (col >= 1 && col <= 7) DrawKey(bottom, KeyboardRow3[col - 1]);
            else if (col == 9) DrawCommandKey(bottom, "⌫", false);
        }
    }

    private void DrawKey(int index, char key)
    {
        var c = _cells[index];
        if (key == '\0') { c.Text = ""; c.Background = Board; c.IsEnabled = false; return; }
        c.Text = key.ToString();
        c.FontSize = 15;
        var state = _keyState.TryGetValue(key, out var s) ? s : Mark.None;
        c.Background = state switch
        {
            Mark.Correct => Good,
            Mark.Present => Warn,
            Mark.Absent => Board,
            _ => Cell,
        };
        c.Foreground = state == Mark.None ? Ink : state == Mark.Absent ? Muted : Cell;
        c.IsEnabled = !_won && _row < Rows;
    }

    private void DrawCommandKey(int index, string text, bool _)
    {
        var c = _cells[index];
        c.Text = text;
        c.FontSize = 16;
        c.Background = Cell;
        c.Foreground = Ink;
        c.IsEnabled = !_won && _row < Rows;
    }
}
