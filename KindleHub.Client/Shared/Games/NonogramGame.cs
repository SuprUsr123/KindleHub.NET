using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Nonograms — a 10×10 picture puzzle. The clues on the edge give the runs of
/// filled squares in each row and column; tap squares to cycle blank, filled and
/// ruled-out, then Check. Any fill that matches every clue is accepted.
/// </summary>
public sealed class NonogramGame : GameBase
{
    private const int Data = 10;
    private const int Cols = 12;   // 1 clue column + 10 data columns
    private const int Rows = 12;   // 1 clue row + 10 data rows + 1 button row
    private const int CheckCell = 11 * Cols + 5;
    private const int Levels = 3;

    private readonly List<GameCell> _cells = new();
    private readonly bool[,] _sol = new bool[Data, Data];
    private readonly int[,] _state = new int[Data, Data];   // 0 blank, 1 filled, 2 ruled out
    private readonly int[][] _rowClues = new int[Data][];
    private readonly int[][] _colClues = new int[Data][];
    private int _level = 1;
    private int _score;
    private bool _done;
    private string _message = "";
    private string _result = "";

    public NonogramGame()
    {
        for (int i = 0; i < Cols * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "nonogram";
    public override string Name => "Nonograms";
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
            return $"Level {_level}/{Levels} · fill rows to match the clues";
        }
    }

    public override void Reset()
    {
        _level = 1;
        _score = 0;
        _done = false;
        _result = "";
        ScoreCounts = false;
        Generate();
    }

    /// <summary>
    /// A random picture built from 2×2 blocks and mirrored left-right, so it
    /// looks like a classic symmetric nonogram and every clue string stays
    /// short enough to fit its cell (at most four runs per line).
    /// </summary>
    private void Generate()
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            var block = new bool[5, 5];
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 3; c++)
                {
                    bool v = Random.Shared.Next(2) == 1;
                    block[r, c] = v;
                    block[r, 4 - c] = v;   // the mirror makes the picture symmetric
                }
            for (int r = 0; r < Data; r++)
                for (int c = 0; c < Data; c++)
                    _sol[r, c] = block[r / 2, c / 2];

            if (LinesFit())
            {
                DeriveClues();
                ClearEntries();
                Redraw();
                return;
            }
        }

        // Fallback: diagonal stripes — valid by construction.
        for (int r = 0; r < Data; r++)
            for (int c = 0; c < Data; c++)
                _sol[r, c] = (r + c) % 4 < 2;
        DeriveClues();
        ClearEntries();
        Redraw();
    }

    /// <summary>Every line has at least one filled square and at most four runs.</summary>
    private bool LinesFit()
    {
        for (int r = 0; r < Data; r++)
        {
            int runs = Runs(Enumerable.Range(0, Data).Select(c => _sol[r, c])).Count;
            if (runs == 0 || runs > 4) return false;
        }
        for (int c = 0; c < Data; c++)
        {
            int runs = Runs(Enumerable.Range(0, Data).Select(r => _sol[r, c])).Count;
            if (runs == 0 || runs > 4) return false;
        }
        return true;
    }

    private void DeriveClues()
    {
        for (int r = 0; r < Data; r++)
            _rowClues[r] = Runs(Enumerable.Range(0, Data).Select(c => _sol[r, c])).ToArray();
        for (int c = 0; c < Data; c++)
            _colClues[c] = Runs(Enumerable.Range(0, Data).Select(r => _sol[r, c])).ToArray();
    }

    private void ClearEntries()
    {
        for (int r = 0; r < Data; r++)
            for (int c = 0; c < Data; c++)
                _state[r, c] = 0;
        _message = "";
    }

    private static List<int> Runs(IEnumerable<bool> line)
    {
        var runs = new List<int>();
        int n = 0;
        foreach (bool b in line)
        {
            if (b) n++;
            else if (n > 0) { runs.Add(n); n = 0; }
        }
        if (n > 0) runs.Add(n);
        return runs;
    }

    public override bool OnTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;

        if (index == CheckCell) return Validate();

        int r = index / Cols - 1, c = index % Cols - 1;
        if (r < 0 || r >= Data || c < 0 || c >= Data) return false;
        _state[r, c] = (_state[r, c] + 1) % 3;   // blank → filled → ruled out → blank
        _message = "";
        Redraw();
        return true;
    }

    /// <summary>A secondary tap clears a square straight back to blank.</summary>
    public override bool OnSecondaryTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;
        int r = index / Cols - 1, c = index % Cols - 1;
        if (r < 0 || r >= Data || c < 0 || c >= Data) return false;
        _state[r, c] = 0;
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
        for (int r = 0; r < Data; r++)
        {
            var runs = Runs(Enumerable.Range(0, Data).Select(c => _state[r, c] == 1));
            if (!runs.SequenceEqual(_rowClues[r]))
                return Fail($"Row {r + 1} doesn't match its clue.");
        }
        for (int c = 0; c < Data; c++)
        {
            var runs = Runs(Enumerable.Range(0, Data).Select(r => _state[r, c] == 1));
            if (!runs.SequenceEqual(_colClues[c]))
                return Fail($"Column {c + 1} doesn't match its clue.");
        }

        _score += 150 * _level;
        if (_level == Levels)
        {
            _done = true;
            ScoreCounts = true;
            _result = $"All {Levels} pictures solved — {_score} points.";
            Redraw();
            return true;
        }
        _level++;
        Generate();
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

        for (int r = 0; r < Data; r++)
        {
            var clue = _cells[(r + 1) * Cols];
            clue.Text = string.Join(" ", _rowClues[r]);
            clue.Foreground = Ink;
            clue.FontSize = 8;
            clue.Bold = true;
        }
        for (int c = 0; c < Data; c++)
        {
            var clue = _cells[c + 1];
            clue.Text = string.Join(" ", _colClues[c]);
            clue.Foreground = Ink;
            clue.FontSize = 8;
            clue.Bold = true;
        }

        for (int r = 0; r < Data; r++)
            for (int c = 0; c < Data; c++)
            {
                var cell = _cells[(r + 1) * Cols + c + 1];
                int s = _state[r, c];
                if (s == 1)
                {
                    cell.Background = Ink;
                    cell.Text = "";
                }
                else
                {
                    cell.Background = Cell;
                    cell.Text = s == 2 ? "✗" : "";
                    cell.Foreground = Muted;
                    cell.FontSize = 14;
                }
                cell.IsEnabled = !_done;
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
