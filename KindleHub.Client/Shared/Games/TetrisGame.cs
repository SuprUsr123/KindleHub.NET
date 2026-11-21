using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Tetris — move and rotate the falling pieces to complete full horizontal lines,
/// ported from the official client. It ends if the stack reaches the top.
/// </summary>
public sealed class TetrisGame : GameBase
{
    private const int BoardW = 10;
    private const int BoardH = 20;
    private const int Sidebar = 5;
    public const int TotalCols = BoardW + Sidebar;

    private const int MaxSteps = 6;   // catch-up guard: at most 6 gravity steps per Tick
    private const int StartMs = 800;  // level 1 gravity: one row per step
    private const double LevelFactor = 0.85; // gravity multiplier per level
    private const int MinMs = 60;     // gravity floor
    private const int HardDropPoints = 2;    // per cell hard-dropped

    private static readonly int[] ClearPoints = { 0, 40, 100, 300, 1200 };
    private static readonly (int X, int Y)[][] Shapes =
    {
        new[] { (0, 1), (1, 1), (2, 1), (3, 1) }, // I
        new[] { (0, 0), (1, 0), (0, 1), (1, 1) }, // O
        new[] { (1, 0), (0, 1), (1, 1), (2, 1) }, // T
        new[] { (1, 0), (2, 0), (0, 1), (1, 1) }, // S
        new[] { (0, 0), (1, 0), (1, 1), (2, 1) }, // Z
        new[] { (0, 0), (0, 1), (1, 1), (2, 1) }, // J
        new[] { (2, 0), (0, 1), (1, 1), (2, 1) }, // L
    };
    private static readonly int[] MatrixSize = { 4, 2, 3, 3, 3, 3, 3 };
    private static readonly IBrush[] PieceBrush =
    {
        Accent, Tiles4[5], Tiles4[2], Good, Bad, Tiles4[4], Tiles4[0],
    };

    private readonly List<GameCell> _cells = new();
    private readonly int[,] _board = new int[BoardW, BoardH]; // 0 empty, else type + 1
    private readonly List<int> _bag = new();
    private int _type, _rot, _px, _py, _nextType;
    private int _score, _lines;
    private TimeSpan _acc;
    private bool _over;

    public TetrisGame()
    {
        for (int i = 0; i < TotalCols * BoardH; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "tetris";
    public override string Name => "Tetris";
    public override int Columns => TotalCols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => true;

    public override int Score => Math.Clamp(_score, 0, 1_000_000);

    public override string? ResultText => _over ? $"Game over — {Score} points." : null;

    public override string StatusText => _over
        ? $"Final score {Score}."
        : $"Score {_score} · lines {_lines} · level {Level}";

    private int Level => _lines / 10 + 1;

    private int DropMs => Math.Max(MinMs, (int)(StartMs * Math.Pow(LevelFactor, Level - 1)));

    public override void Reset()
    {
        Array.Clear(_board, 0, _board.Length);
        _bag.Clear();
        _score = 0;
        _lines = 0;
        _acc = TimeSpan.Zero;
        _over = false;
        _nextType = -1; // force the first piece to come out of a fresh bag
        ScoreCounts = false;
        Spawn();
        Redraw();
    }

    private int PullBag()
    {
        // 7-bag randomizer: every shape appears once per bag.
        if (_bag.Count == 0)
            foreach (var shape in Enumerable.Range(0, 7).OrderBy(_ => Random.Shared.Next()))
                _bag.Add(shape);
        int t = _bag[^1];
        _bag.RemoveAt(_bag.Count - 1);
        return t;
    }

    private void Spawn()
    {
        _type = _nextType == -1 ? PullBag() : _nextType;
        _nextType = PullBag();
        _rot = 0;
        int n = MatrixSize[_type];
        _px = n == 2 ? 4 : 3;
        _py = 0;
        if (Collides(_type, _rot, _px, _py)) GameOver();
    }

    private static (int X, int Y)[] CellsOf(int type, int rot)
    {
        int n = MatrixSize[type];
        var cells = new (int X, int Y)[Shapes[type].Length];
        for (int i = 0; i < cells.Length; i++)
        {
            var (x, y) = Shapes[type][i];
            for (int r = 0; r < rot; r++) (x, y) = (n - 1 - y, x); // clockwise rotation
            cells[i] = (x, y);
        }
        return cells;
    }

    private bool Collides(int type, int rot, int px, int py)
    {
        foreach (var (cx, cy) in CellsOf(type, rot))
        {
            int bx = px + cx, by = py + cy;
            if (bx < 0 || bx >= BoardW || by >= BoardH) return true;
            if (by >= 0 && _board[bx, by] != 0) return true;
        }
        return false;
    }

    public override bool Tick(TimeSpan elapsed)
    {
        if (_over) return false;
        _acc += elapsed;
        int guard = 0;
        bool changed = false;
        while (_acc.TotalMilliseconds >= DropMs && guard++ < MaxSteps)
        {
            _acc -= TimeSpan.FromMilliseconds(DropMs);
            StepDown();
            changed = true;
            if (_over) break;
        }
        if (_acc.TotalMilliseconds > DropMs * MaxSteps) _acc = TimeSpan.FromMilliseconds(DropMs * MaxSteps);
        if (changed) Redraw();
        return changed;
    }

    private void StepDown()
    {
        if (!Collides(_type, _rot, _px, _py + 1))
        {
            _py++;
            return;
        }
        Lock();
    }

    private void Lock()
    {
        foreach (var (cx, cy) in CellsOf(_type, _rot))
        {
            int bx = _px + cx, by = _py + cy;
            if (by < 0)
            {
                GameOver(); // the stack reached the top
                return;
            }
            _board[bx, by] = _type + 1;
        }

        int cleared = 0;
        for (int y = BoardH - 1; y >= 0; y--)
        {
            bool full = true;
            for (int x = 0; x < BoardW; x++)
                if (_board[x, y] == 0) { full = false; break; }
            if (!full) continue;

            cleared++;
            for (int yy = y; yy > 0; yy--)
                for (int x = 0; x < BoardW; x++)
                    _board[x, yy] = _board[x, yy - 1];
            for (int x = 0; x < BoardW; x++) _board[x, 0] = 0;
        }

        if (cleared > 0)
        {
            _score += ClearPoints[cleared] * Level;
            _lines += cleared;
        }

        Spawn();
    }

    private void GameOver()
    {
        if (_over) return;
        _over = true;
        ScoreCounts = _score > 0;
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (_over) return false;
        switch (key)
        {
            case GameKey.Left:
                Move(-1);
                break;
            case GameKey.Right:
                Move(1);
                break;
            case GameKey.Up:
                Rotate();
                break;
            case GameKey.Down:
                SoftDrop();
                break;
            case GameKey.Confirm:
                HardDrop();
                break;
            default:
                return false;
        }
        Redraw();
        return true;
    }

    public override bool OnTap(int index)
    {
        // Board taps steer the piece: left/right of it moves, below soft drops,
        // on or above it rotates.
        if (_over || index < 0 || index >= _cells.Count) return false;
        int tx = index % TotalCols, ty = index / TotalCols;
        if (tx >= BoardW) return false;

        var cells = CellsOf(_type, _rot);
        int minX = cells.Min(c => c.X) + _px, maxX = cells.Max(c => c.X) + _px;
        int maxY = cells.Max(c => c.Y) + _py;
        if (tx < minX) Move(-1);
        else if (tx > maxX) Move(1);
        else if (ty > maxY) SoftDrop();
        else Rotate();
        Redraw();
        return true;
    }

    private void Move(int dx)
    {
        if (!Collides(_type, _rot, _px + dx, _py)) _px += dx;
    }

    private void Rotate()
    {
        int nr = (_rot + 1) % 4;
        // Simple wall kicks: nudge the piece back into the well.
        foreach (int off in new[] { 0, -1, 1, -2, 2 })
        {
            if (Collides(_type, nr, _px + off, _py)) continue;
            _rot = nr;
            _px += off;
            return;
        }
    }

    private void SoftDrop()
    {
        _acc = TimeSpan.Zero;
        if (!Collides(_type, _rot, _px, _py + 1))
        {
            _py++;
            return;
        }
        Lock();
    }

    private void HardDrop()
    {
        int dropped = 0;
        while (!Collides(_type, _rot, _px, _py + 1))
        {
            _py++;
            dropped++;
        }
        _score += HardDropPoints * dropped;
        _acc = TimeSpan.Zero;
        Lock();
    }

    public override void Redraw()
    {
        for (int y = 0; y < BoardH; y++)
        for (int x = 0; x < TotalCols; x++)
        {
            var c = _cells[y * TotalCols + x];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = !_over;
            c.FontSize = 14;
            c.Bold = false;
        }

        for (int y = 0; y < BoardH; y++)
        for (int x = 0; x < BoardW; x++)
        {
            int v = _board[x, y];
            if (v > 0) _cells[y * TotalCols + x].Background = PieceBrush[v - 1];
        }

        if (!_over)
        {
            foreach (var (cx, cy) in CellsOf(_type, _rot))
            {
                int bx = _px + cx, by = _py + cy;
                if (by < 0) continue;
                var c = _cells[by * TotalCols + bx];
                c.Background = PieceBrush[_type];
                c.Text = "■";
                c.Foreground = Cell;
            }
        }

        // Sidebar: next-piece preview plus the running tallies.
        Side(0, 0, "NEXT");
        foreach (var (cx, cy) in CellsOf(_nextType, 0))
            _cells[(1 + cy) * TotalCols + 10 + cx].Background = PieceBrush[_nextType];
        Side(6, 0, "SCORE");
        Side(6, 1, _score.ToString());
        Side(8, 0, "LINES");
        Side(8, 1, _lines.ToString());
        Side(10, 0, "LEVEL");
        Side(10, 1, Level.ToString());
    }

    private void Side(int y, int x, string text)
    {
        var c = _cells[y * TotalCols + 10 + x];
        c.Text = text;
        c.Foreground = Muted;
        c.FontSize = text.Length > 3 ? 8 : 11;
        if (char.IsDigit(text.Length > 0 ? text[0] : ' ')) c.Bold = true;
    }
}
