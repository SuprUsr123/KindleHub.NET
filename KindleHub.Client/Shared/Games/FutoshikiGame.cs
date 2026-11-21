using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Futoshiki — a 5×5 Latin square with inequality arrows: every row and column
/// uses 1 to 5 exactly once and each arrow points at the smaller number. The
/// puzzle is generated from a random Latin square, so it always has a solution;
/// any fill that satisfies all rows, columns and arrows is accepted.
/// </summary>
public sealed class FutoshikiGame : GameBase
{
    private const int N = 5;
    private const int Cols = 11;          // 1 clue column + 10 data columns
    private const int Rows = 11;          // 1 clue row + 10 data rows
    private const int CheckCell = 10 * Cols + 5;
    private const int Levels = 3;

    private readonly List<GameCell> _cells = new();
    private readonly int[,] _grid = new int[N, N];   // 0 blank, 1..5 entries
    private readonly List<(int ax, int ay, int bx, int by)> _constraints = new(); // a < b
    private int _level = 1;
    private int _score;
    private bool _done;
    private string _message = "";
    private string _result = "";

    public FutoshikiGame()
    {
        for (int i = 0; i < Cols * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "futoshiki";
    public override string Name => "Futoshiki";
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
            return $"Level {_level}/{Levels} · taps cycle 1-5 · Check to verify";
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
    /// Random Latin square via a cyclic base plus row, column and digit
    /// permutations, then ~8 arrow clues read off the solution.
    /// </summary>
    private void Generate()
    {
        int[] rowPerm = { 0, 1, 2, 3, 4 };
        int[] colPerm = { 0, 1, 2, 3, 4 };
        int[] digits = { 1, 2, 3, 4, 5 };
        Shuffle(rowPerm);
        Shuffle(colPerm);
        Shuffle(digits);

        var sol = new int[N, N];
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                sol[r, c] = digits[(rowPerm[r] + colPerm[c]) % N];

        var pairs = new List<(int ax, int ay, int bx, int by)>();
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N - 1; x++) pairs.Add((x, y, x + 1, y));
        for (int y = 0; y < N - 1; y++)
            for (int x = 0; x < N; x++) pairs.Add((x, y, x, y + 1));
        Shuffle(pairs);

        _constraints.Clear();
        foreach (var (ax, ay, bx, by) in pairs.Take(8))
        {
            // Store the pair so that cell a holds the smaller solution value.
            if (sol[ay, ax] < sol[by, bx]) _constraints.Add((ax, ay, bx, by));
            else _constraints.Add((bx, by, ax, ay));
        }

        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                _grid[r, c] = 0;
        _message = "";
        Redraw();
    }

    private static void Shuffle(int[] a)
    {
        for (int i = a.Length - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
    }

    private static void Shuffle(List<(int ax, int ay, int bx, int by)> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public override bool OnTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;

        if (index == CheckCell) return Validate();

        int r = index / Cols, c = index % Cols;
        if (r < 1 || r > 9 || c < 1 || c > 9 || r % 2 == 0 || c % 2 == 0) return false;
        int gy = (r - 1) / 2, gx = (c - 1) / 2;
        _grid[gy, gx] = (_grid[gy, gx] + 1) % (N + 1);   // blank → 1 … 5 → blank
        _message = "";
        Redraw();
        return true;
    }

    /// <summary>A secondary tap empties a square straight away.</summary>
    public override bool OnSecondaryTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;
        int r = index / Cols, c = index % Cols;
        if (r < 1 || r > 9 || c < 1 || c > 9 || r % 2 == 0 || c % 2 == 0) return false;
        _grid[(r - 1) / 2, (c - 1) / 2] = 0;
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
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                if (_grid[r, c] == 0)
                    return Fail("Fill every square first.");

        for (int r = 0; r < N; r++)
            if (!IsPermutation(Enumerable.Range(0, N).Select(c => _grid[r, c])))
                return Fail("Every row and column needs 1 to 5.");
        for (int c = 0; c < N; c++)
            if (!IsPermutation(Enumerable.Range(0, N).Select(r => _grid[r, c])))
                return Fail("Every row and column needs 1 to 5.");

        foreach (var (ax, ay, bx, by) in _constraints)
            if (_grid[ay, ax] >= _grid[by, bx])
                return Fail("An arrow rule is broken.");

        _score += 150 * _level;
        if (_level == Levels)
        {
            _done = true;
            ScoreCounts = true;
            _result = $"All {Levels} squares solved — {_score} points.";
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

    private static bool IsPermutation(IEnumerable<int> values)
    {
        var set = values.ToHashSet();
        return set.Count == N && set.SetEquals(Enumerable.Range(1, N));
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

        for (int gy = 0; gy < N; gy++)
            for (int gx = 0; gx < N; gx++)
            {
                var c = _cells[(2 * gy + 1) * Cols + 2 * gx + 1];
                int v = _grid[gy, gx];
                c.Text = v == 0 ? "" : v.ToString();
                c.Background = Cell;
                c.Foreground = Ink;
                c.IsEnabled = !_done;
                c.FontSize = 22;
                c.Bold = true;
            }

        foreach (var (ax, ay, bx, by) in _constraints)
        {
            int row, col;
            string arrow;
            if (ay == by)
            {
                row = 2 * ay + 1;
                col = 2 * Math.Min(ax, bx) + 2;
                arrow = ax < bx ? "←" : "→";   // the arrow points at the smaller cell
            }
            else
            {
                col = 2 * ax + 1;
                row = 2 * Math.Min(ay, by) + 2;
                arrow = ay < by ? "↑" : "↓";
            }
            var c = _cells[row * Cols + col];
            c.Text = arrow;
            c.Foreground = Muted;
            c.FontSize = 18;
            c.Bold = true;
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
