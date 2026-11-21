using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Nerdle, ported from the official client — Wordle with maths: guess the hidden
/// 8-character equation (like 12+35=47) in six tries. Every guess must itself be a
/// true single-operator equation.
/// </summary>
public sealed class NerdleGame : GameBase
{
    private const int Rows = 6;
    private const int Cols = 10;       // board width, like Wordle
    private const int EqLen = 8;       // equation length
    private const int Start = 1;       // board left margin so 8 cells sit centred
    private const int BoardCells = Rows * Cols;
    private const int KeyBase = BoardCells;
    private const int EnterCell = KeyBase + 20;      // bottom row, col 0
    private const int BackspaceCell = KeyBase + 29;  // bottom row, col 9

    private const string DigitRow = "1234567890";
    private const string OpRow = "+-*/=";

    private enum Mark { None, Correct, Present, Absent }

    private readonly List<GameCell> _cells = new();
    private readonly char[,] _board = new char[Rows, EqLen];
    private readonly Mark[,] _marks = new Mark[Rows, EqLen];
    private readonly Dictionary<char, Mark> _keyState = new();
    private string _answer = "";
    private int _row;
    private int _col;
    private bool _won;
    private bool _lost;
    private string _message = "";

    public NerdleGame()
    {
        for (int i = 0; i < Cols * 9; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "nerdle";
    public override string Name => "Nerdle";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _won ? (7 - (_row + 1)) * 10 + 8 : 0;

    public override string? ResultText => _won
        ? $"Solved in {_row + 1} guesses."
        : _lost ? $"It was {_answer}." : null;

    public override string StatusText
    {
        get
        {
            if (_won) return $"Correct — {_answer} in {_row + 1}.";
            if (_lost) return $"The equation was {_answer}.";
            if (!string.IsNullOrEmpty(_message)) return _message;
            return _col == 0
                ? $"Guess {_row + 1} of {Rows} — tap or type an 8-char equation"
                : $"Guess {_row + 1} of {Rows} — {EqLen - _col} to go · ↵ to submit";
        }
    }

    public override void Reset()
    {
        _answer = GenerateAnswer();
        Array.Clear(_board, 0, _board.Length);
        Array.Clear(_marks, 0, _marks.Length);
        _keyState.Clear();
        _row = 0;
        _col = 0;
        _won = _lost = false;
        _message = "";
        ScoreCounts = false;
        Redraw();
    }

    /// <summary>
    /// Builds a true equation that is exactly 8 characters: NN+NN=NN, NN-NN=NN,
    /// N+NN=NNN, N*NN=NNN and the two mirrored forms. (NN*NN is always 9+ chars and
    /// NN/NN can never have a two-digit quotient, so those shapes are impossible.)
    /// </summary>
    private static string GenerateAnswer()
    {
        while (true)
        {
            int a, b;
            switch (Random.Shared.Next(6))
            {
                case 0:
                    a = Random.Shared.Next(10, 100);
                    b = Random.Shared.Next(10, 100);
                    if (a + b <= 99) return $"{a}+{b}={a + b}";
                    break;
                case 1:
                    a = Random.Shared.Next(10, 100);
                    b = Random.Shared.Next(10, 100);
                    if (a - b >= 10) return $"{a}-{b}={a - b}";
                    break;
                case 2:
                    a = Random.Shared.Next(1, 10);
                    b = Random.Shared.Next(10, 100);
                    if (a + b >= 100) return $"{a}+{b}={a + b}";
                    break;
                case 3:
                    a = Random.Shared.Next(2, 10);
                    b = Random.Shared.Next(10, 100);
                    if (a * b >= 100 && a * b <= 999) return $"{a}*{b}={a * b}";
                    break;
                case 4:
                    a = Random.Shared.Next(10, 100);
                    b = Random.Shared.Next(1, 10);
                    if (a + b >= 100) return $"{a}+{b}={a + b}";
                    break;
                default:
                    a = Random.Shared.Next(10, 100);
                    b = Random.Shared.Next(2, 10);
                    if (a * b >= 100 && a * b <= 999) return $"{a}*{b}={a * b}";
                    break;
            }
        }
    }

    public override bool OnKey(GameKey key)
    {
        if (key == GameKey.Confirm) return Submit();
        if (key == GameKey.Backspace) return Delete();
        return base.OnKey(key);
    }

    public override bool OnTap(int index)
    {
        if (_won || _lost || index < 0 || index >= _cells.Count) return false;

        if (index < BoardCells)
        {
            // Tapping the live row moves the cursor, like Wordle's board.
            int r = index / Cols, col = index % Cols;
            if (r != _row || col < Start || col >= Start + EqLen) return false;
            _col = Math.Clamp(col - Start + 1, 0, EqLen);
            Redraw();
            return true;
        }
        if (index == EnterCell) return Submit();
        if (index == BackspaceCell) return Delete();

        char key = KeyAt(index);
        return key != '\0' && Type(key);
    }

    private static char KeyAt(int index)
    {
        int k = index - KeyBase;
        if (k >= 0 && k < Cols) return DigitRow[k];
        if (k >= 12 && k < 12 + OpRow.Length) return OpRow[k - 12];
        return '\0';
    }

    private bool Type(char ch)
    {
        if (_won || _lost || _col >= EqLen) return false;
        _board[_row, _col++] = ch;
        _message = "";
        Redraw();
        return true;
    }

    private bool Delete()
    {
        if (_won || _lost || _col <= 0) return false;
        _col--;
        _board[_row, _col] = '\0';
        _message = "";
        Redraw();
        return true;
    }

    private bool Submit()
    {
        if (_won || _lost) return false;
        if (_col < EqLen)
        {
            _message = "Eight characters, please.";
            Redraw();
            return true;
        }

        var guess = new string(Enumerable.Range(0, EqLen).Select(c => _board[_row, c]).ToArray());
        if (!IsValidEquation(guess, out var error))
        {
            _message = error;
            Redraw();
            return true;
        }

        MarkRow(guess);
        if (guess == _answer)
        {
            _won = true;
            ScoreCounts = true;
        }
        else
        {
            _row++;
            if (_row >= Rows) _lost = true;
        }
        _col = 0;
        _message = "";
        Redraw();
        return true;
    }

    /// <summary>True when the 8 characters form a true equation, operator on either side.</summary>
    private static bool IsValidEquation(string guess, out string error)
    {
        error = "";
        if (guess.Count(ch => ch == '=') != 1) { error = "Use exactly one = sign."; return false; }
        int at = guess.IndexOf('=');
        string lhs = guess[..at], rhs = guess[(at + 1)..];
        if (lhs.Length == 0 || rhs.Length == 0) { error = "Both sides need numbers."; return false; }

        if (!TryEvaluate(lhs, out int x, out error) ||
            !TryEvaluate(rhs, out int y, out error))
            return false;
        if (x != y) { error = "That equation is not true."; return false; }
        return true;
    }

    /// <summary>One side is a plain number or a single a-op-b expression.</summary>
    private static bool TryEvaluate(string side, out int value, out string error)
    {
        value = 0;
        error = "";
        int ops = side.Count(ch => ch is '+' or '-' or '*' or '/');
        if (ops == 0)
        {
            if (!Digits(side)) { error = "Digits and + - * / only."; return false; }
            if (LeadingZero(side)) { error = "No leading zeros."; return false; }
            value = int.Parse(side);
            return true;
        }
        if (ops > 1) { error = "Use a single operator."; return false; }

        int opPos = side.IndexOfAny(new[] { '+', '-', '*', '/' });
        if (opPos <= 0 || opPos >= side.Length - 1) { error = "Use a single operator."; return false; }

        string a = side[..opPos], b = side[(opPos + 1)..];
        if (!Digits(a) || !Digits(b)) { error = "Digits and + - * / only."; return false; }
        if (LeadingZero(a) || LeadingZero(b)) { error = "No leading zeros."; return false; }

        int x = int.Parse(a), z = int.Parse(b);
        switch (side[opPos])
        {
            case '+': value = x + z; break;
            case '-': value = x - z; break;
            case '*': value = x * z; break;
            default:
                if (z == 0 || x % z != 0) { error = "Division must be exact."; return false; }
                value = x / z;
                break;
        }
        return true;
    }

    private static bool Digits(string s) => s.Length > 0 && s.All(char.IsDigit);
    private static bool LeadingZero(string s) => s.Length > 1 && s[0] == '0';

    /// <summary>Wordle-style two-pass feedback: exact position first, then "elsewhere".</summary>
    private void MarkRow(string guess)
    {
        var remaining = _answer.ToList();
        for (int c = 0; c < EqLen; c++)
        {
            if (guess[c] != _answer[c]) continue;
            _marks[_row, c] = Mark.Correct;
            remaining[c] = '\0';
        }
        for (int c = 0; c < EqLen; c++)
        {
            if (_marks[_row, c] == Mark.Correct) continue;
            int at = remaining.IndexOf(guess[c]);
            if (at >= 0)
            {
                _marks[_row, c] = Mark.Present;
                remaining[at] = '\0';
            }
            else
            {
                _marks[_row, c] = Mark.Absent;
            }
        }
        for (int c = 0; c < EqLen; c++)
        {
            char ch = guess[c];
            if (!_keyState.TryGetValue(ch, out var prev) || Rank(_marks[_row, c]) > Rank(prev))
                _keyState[ch] = _marks[_row, c];
        }
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
        foreach (var c in _cells)
        {
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = false;
            c.Bold = true;
            c.FontSize = 16;
            c.Span = 1;
        }

        bool active = !_won && !_lost;
        for (int r = 0; r < Rows; r++)
        {
            for (int i = 0; i < EqLen; i++)
            {
                var cell = _cells[r * Cols + Start + i];
                char ch = _board[r, i];
                if (ch == '\0') continue;
                cell.Text = ch.ToString();
                cell.FontSize = 20;
                switch (_marks[r, i])
                {
                    case Mark.Correct: cell.Background = Good; cell.Foreground = Cell; break;
                    case Mark.Present: cell.Background = Accent; cell.Foreground = Cell; break;
                    case Mark.Absent: cell.Background = Board; cell.Foreground = Muted; break;
                    default:
                        cell.Background = r == _row ? Cell : Board;
                        cell.Foreground = r == _row ? Ink : Muted;
                        break;
                }
            }
        }

        // Keyboard: 10 digits, the five operators centred, then enter and delete.
        for (int col = 0; col < Cols; col++)
        {
            DrawKey(KeyBase + col, DigitRow[col], active);
            int opRow = KeyBase + Cols + col;
            if (col >= 2 && col < 2 + OpRow.Length)
                DrawKey(opRow, OpRow[col - 2], active);
            int bottom = KeyBase + Cols * 2 + col;
            if (col == 0 || col == Cols - 1)
            {
                var cell = _cells[bottom];
                cell.Text = col == 0 ? "↵" : "✕";
                cell.FontSize = 16;
                cell.Background = Cell;
                cell.Foreground = Ink;
                cell.IsEnabled = active;
            }
        }
    }

    private void DrawKey(int index, char key, bool active)
    {
        var cell = _cells[index];
        cell.Text = key.ToString();
        cell.FontSize = 15;
        var state = _keyState.TryGetValue(key, out var s) ? s : Mark.None;
        cell.Background = state switch
        {
            Mark.Correct => Good,
            Mark.Present => Accent,
            Mark.Absent => Board,
            _ => Cell,
        };
        cell.Foreground = state == Mark.None ? Ink : state == Mark.Absent ? Muted : Cell;
        cell.IsEnabled = active;
    }
}
