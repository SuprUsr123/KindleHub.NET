using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Kakuro — cross-sums. A run of white squares must add up to the clue in the
/// black square before it; the ↓ clue is the column below, the → clue the row
/// to the right. Runs are filled with distinct digits when the puzzle is built,
/// so every generated board is solvable and any valid fill is accepted.
/// </summary>
public sealed class KakuroGame : GameBase
{
    private const int Side = 6;      // 5×5 play area plus a black border
    private const int Rows = 7;      // board + one button row
    private const int Cols = 6;
    private const int CheckCell = 6 * Cols + 2;
    private const int Levels = 3;

    // Hand-designed black-cell masks (B/W per interior cell, rows 1..5 × cols 1..5).
    // Every row and column splits into runs of length 2-3, so clues always fit
    // and no run ever needs more distinct digits than 1-9 can supply.
    private static readonly string[][] Masks =
    {
        new[] { "BBWWW", "BBWWW", "WWBWW", "WWWBB", "WWWBB" },
        new[] { "BBWWW", "BBWWW", "BBBWW", "WWWBB", "WWWBB" },
        new[] { "WWWBB", "WWWBB", "WWBBB", "BBWWW", "BBWWW" },
    };

    private readonly List<GameCell> _cells = new();
    private readonly bool[,] _black = new bool[Side, Side];
    private readonly int[,] _sol = new int[Side, Side];     // generated solution
    private readonly int[,] _entry = new int[Side, Side];   // player entries
    private readonly int[,] _rowClue = new int[Side, Side];
    private readonly int[,] _colClue = new int[Side, Side];
    private readonly List<(bool horizontal, int x, int y, int len, int clue)> _runs = new();
    private int _level = 1;
    private int _score;
    private bool _done;
    private string _message = "";
    private string _result = "";

    public KakuroGame()
    {
        for (int i = 0; i < Cols * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "kakuro";
    public override string Name => "Kakuro";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _score;
    public override string? ResultText => _done ? _result : null;

    public override string StatusText
    {
        get
        {
            if (_done) return $"Done — {_score} points.";
            if (_message.Length > 0) return _message;
            return $"Level {_level}/{Levels} · runs sum to their clues, no repeats";
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
        string[] mask = Masks[_level - 1];
        _runs.Clear();
        _message = "";
        for (int r = 0; r < Side; r++)
            for (int c = 0; c < Side; c++)
            {
                _black[r, c] = r == 0 || c == 0 || mask[r - 1][c - 1] == 'B';
                _sol[r, c] = 0;
                _entry[r, c] = 0;
                _rowClue[r, c] = 0;
                _colClue[r, c] = 0;
            }

        // Collect the horizontal and vertical runs defined by the mask.
        for (int r = 1; r < Side; r++)
        {
            int c = 1;
            while (c < Side)
            {
                if (_black[r, c]) { c++; continue; }
                int start = c;
                while (c < Side && !_black[r, c]) c++;
                _runs.Add((true, start, r, c - start, 0));
            }
        }
        for (int c = 1; c < Side; c++)
        {
            int r = 1;
            while (r < Side)
            {
                if (_black[r, c]) { r++; continue; }
                int start = r;
                while (r < Side && !_black[r, c]) r++;
                _runs.Add((false, c, start, r - start, 0));
            }
        }

        FillSolution();

        for (int i = 0; i < _runs.Count; i++)
        {
            var run = _runs[i];
            int sum = 0;
            for (int k = 0; k < run.len; k++)
                sum += run.horizontal ? _sol[run.y, run.x + k] : _sol[run.y + k, run.x];
            run.clue = sum;
            _runs[i] = run;
            if (run.horizontal) _rowClue[run.y, run.x - 1] = sum;
            else _colClue[run.y - 1, run.x] = sum;
        }
        Redraw();
    }

    /// <summary>
    /// Fills the whites in reading order with digits that are unused in the
    /// cell's row run and column run so far. Runs are at most three long, so at
    /// most four digits are banned and a choice always exists.
    /// </summary>
    private void FillSolution()
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            bool ok = true;
            for (int r = 1; r < Side && ok; r++)
                for (int c = 1; c < Side && ok; c++)
                {
                    if (_black[r, c]) continue;
                    var used = new HashSet<int>();
                    for (int k = c - 1; k >= 1 && !_black[r, k]; k--) used.Add(_sol[r, k]);
                    for (int k = r - 1; k >= 1 && !_black[k, c]; k--) used.Add(_sol[k, c]);
                    var options = Enumerable.Range(1, 9).Where(d => !used.Contains(d)).ToList();
                    if (options.Count == 0) { ok = false; break; }
                    _sol[r, c] = options[Random.Shared.Next(options.Count)];
                }
            if (ok) return;
            for (int r = 1; r < Side; r++)
                for (int c = 1; c < Side; c++)
                    _sol[r, c] = 0;
        }
    }

    public override bool OnTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;

        if (index == CheckCell) return Validate();

        int r = index / Cols, c = index % Cols;
        if (r >= Side || _black[r, c]) return false;
        _entry[r, c] = (_entry[r, c] + 1) % 10;   // blank → 1 … 9 → blank
        _message = "";
        Redraw();
        return true;
    }

    /// <summary>A secondary tap empties a square straight away.</summary>
    public override bool OnSecondaryTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;
        int r = index / Cols, c = index % Cols;
        if (r >= Side || _black[r, c]) return false;
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
        for (int r = 1; r < Side; r++)
            for (int c = 1; c < Side; c++)
                if (!_black[r, c] && _entry[r, c] == 0)
                    return Fail("Fill every white square.");

        foreach (var run in _runs)
        {
            var values = new List<int>();
            for (int k = 0; k < run.len; k++)
                values.Add(run.horizontal ? _entry[run.y, run.x + k] : _entry[run.y + k, run.x]);
            if (values.Distinct().Count() != values.Count)
                return Fail("No digit repeats inside a run.");
            int sum = values.Sum();
            if (sum != run.clue)
                return Fail(run.horizontal
                    ? $"Row clue →{run.clue} has {sum}."
                    : $"Column clue ↓{run.clue} has {sum}.");
        }

        _score += 150 * _level;
        if (_level == Levels)
        {
            _done = true;
            ScoreCounts = true;
            _result = $"All {Levels} boards solved — {_score} points.";
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
                var cell = _cells[r * Cols + c];
                if (_black[r, c])
                {
                    cell.Background = Ink;
                    string text = "";
                    if (_colClue[r, c] > 0) text = "↓" + _colClue[r, c];
                    if (_rowClue[r, c] > 0) text += (text.Length > 0 ? "\n" : "") + "→" + _rowClue[r, c];
                    cell.Text = text;
                    cell.Foreground = Cell;
                    cell.FontSize = 10;
                    cell.Bold = true;
                }
                else
                {
                    cell.Background = Cell;
                    cell.Text = _entry[r, c] == 0 ? "" : _entry[r, c].ToString();
                    cell.Foreground = Ink;
                    cell.IsEnabled = !_done;
                    cell.FontSize = 20;
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
