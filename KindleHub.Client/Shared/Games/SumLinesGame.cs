using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Sum Lines — put digits in the white squares so each run adds up to its clue:
/// A11 means the row to its right totals 11, D7 the column below it totals 7.
/// Runs never share a square and are filled with distinct digits when the
/// puzzle is generated, so any fill matching every clue is accepted.
/// </summary>
public sealed class SumLinesGame : GameBase
{
    private const int Side = 7;
    private const int Rows = 8;      // board + one button row
    private const int CheckCell = 7 * Side + 3;
    private const int Levels = 3;

    // Each pattern: (horizontal?, start x, start y, run length). Every white
    // cell belongs to exactly one run and every clue cell is unique.
    private static readonly (bool h, int x, int y, int len)[][] Patterns =
    {
        new[]
        {
            (false, 1, 1, 3), (false, 3, 1, 3), (false, 5, 1, 3),
            (true, 1, 5, 2), (true, 4, 5, 2),
        },
        new[]
        {
            (false, 1, 1, 2), (false, 3, 1, 2), (false, 5, 1, 2),
            (true, 1, 3, 3), (true, 5, 3, 2),
            (true, 1, 5, 2), (true, 4, 5, 2),
        },
        new[]
        {
            (false, 1, 1, 2), (false, 2, 1, 2), (false, 4, 1, 2), (false, 5, 1, 2),
            (true, 4, 3, 2),
            (true, 1, 4, 2), (true, 5, 4, 2),
            (true, 1, 5, 3), (true, 5, 5, 2),
        },
    };

    private readonly List<GameCell> _cells = new();
    private readonly bool[,] _white = new bool[Side, Side];
    private readonly string[,] _clueText = new string[Side, Side];
    private readonly int[,] _entry = new int[Side, Side];
    private readonly List<(bool h, int x, int y, int len, int clue)> _runs = new();
    private int _level = 1;
    private int _score;
    private bool _done;
    private string _message = "";
    private string _result = "";

    public SumLinesGame()
    {
        for (int i = 0; i < Side * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "sumlines";
    public override string Name => "Sum Lines";
    public override int Columns => Side;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _score;
    public override string? ResultText => _done ? _result : null;

    public override string StatusText
    {
        get
        {
            if (_done) return $"Done — {_score} points.";
            if (_message.Length > 0) return _message;
            return $"Level {_level}/{Levels} · A = row clue, D = column clue";
        }
    }

    public override void Reset()
    {
        _level = 1;
        _score = 0;
        _done = false;
        _result = "";
        ScoreCounts = false;
        LoadLevel();
    }

    private void LoadLevel()
    {
        for (int r = 0; r < Side; r++)
            for (int c = 0; c < Side; c++)
            {
                _white[r, c] = false;
                _clueText[r, c] = "";
                _entry[r, c] = 0;
            }
        _runs.Clear();
        _message = "";

        foreach (var (h, x, y, len) in Patterns[_level - 1])
        {
            // Sample distinct digits for the run; the clue is simply their sum,
            // which is what makes the puzzle solvable by construction.
            var digits = Enumerable.Range(1, 9).OrderBy(_ => Random.Shared.Next()).Take(len).ToList();
            int sum = digits.Sum();
            _runs.Add((h, x, y, len, sum));
            for (int k = 0; k < len; k++)
                _white[h ? y : y + k, h ? x + k : x] = true;
            int cx = h ? x - 1 : x, cy = h ? y : y - 1;
            _clueText[cy, cx] = (h ? "A" : "D") + sum;
        }
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;

        if (index == CheckCell) return Validate();

        int r = index / Side, c = index % Side;
        if (r >= Side || !_white[r, c]) return false;
        _entry[r, c] = (_entry[r, c] + 1) % 10;   // blank → 1 … 9 → blank
        _message = "";
        Redraw();
        return true;
    }

    /// <summary>A secondary tap empties a square straight away.</summary>
    public override bool OnSecondaryTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;
        int r = index / Side, c = index % Side;
        if (r >= Side || !_white[r, c]) return false;
        _entry[r, c] = 0;
        _message = "";
        Redraw();
        return true;
    }

    public override bool OnKey(GameKey key)
    {
        if (_done) return false;
        return key == GameKey.Confirm && Validate();
    }

    private bool Validate()
    {
        foreach (var (h, x, y, len, clue) in _runs)
        {
            var values = new List<int>();
            for (int k = 0; k < len; k++)
            {
                int v = h ? _entry[y, x + k] : _entry[y + k, x];
                if (v == 0) return Fail("Fill every white square.");
                values.Add(v);
            }
            if (values.Distinct().Count() != values.Count)
                return Fail("A run cannot repeat a digit.");
            int sum = values.Sum();
            if (sum != clue)
                return Fail($"{(h ? 'A' : 'D')}{clue} run sums {sum}.");
        }

        _score += 150 * _level;
        if (_level == Levels)
        {
            _done = true;
            ScoreCounts = true;
            _result = $"All {Levels} line sets solved — {_score} points.";
            Redraw();
            return true;
        }
        _level++;
        LoadLevel();
        return true;
    }

    private bool Fail(string message)
    {
        _message = message;
        Redraw();
        return true;
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = false;
            c.FontSize = 16;
            c.Bold = false;
        }

        for (int r = 0; r < Side; r++)
            for (int c = 0; c < Side; c++)
            {
                var cell = _cells[r * Side + c];
                if (_white[r, c])
                {
                    cell.Background = Cell;
                    cell.Text = _entry[r, c] == 0 ? "" : _entry[r, c].ToString();
                    cell.Foreground = Ink;
                    cell.IsEnabled = !_done;
                    cell.FontSize = 20;
                    cell.Bold = true;
                }
                else if (_clueText[r, c].Length > 0)
                {
                    cell.Background = Ink;
                    cell.Text = _clueText[r, c];
                    cell.Foreground = Cell;
                    cell.FontSize = 11;
                    cell.Bold = true;
                }
            }

        var check = _cells[CheckCell];
        check.Text = "Check ✓";
        check.Background = Cell;
        check.Foreground = Ink;
        check.IsEnabled = !_done;
        check.Bold = true;
        check.FontSize = 15;
    }
}
