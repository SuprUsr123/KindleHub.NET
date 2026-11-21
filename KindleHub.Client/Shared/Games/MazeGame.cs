using System;
using System.Collections.Generic;

namespace KindleHub.Client.Games;

/// <summary>
/// Maze — a perfect 15×15 maze carved by an iterative backtracker; find your way
/// from ● to the ★ exit. Three levels: plain, then fog that dims everything a
/// few steps away, then the same fog with start and exit on swapped corners.
/// </summary>
public sealed class MazeGame : GameBase
{
    private const int Size = 15;
    private const int Last = 13;    // last odd coordinate — a corner cell
    private const int Levels = 3;

    // Fog dims walls and floors slightly differently so the maze stays readable.
    private static readonly Avalonia.Media.IBrush DimWall =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#74747c"));
    private static readonly Avalonia.Media.IBrush DimFloor =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#c9c9d0"));

    private readonly List<GameCell> _cells = new();
    private readonly bool[,] _wall = new bool[Size, Size];
    private int _px = 1, _py = 1;
    private int _ex = Last, _ey = Last;   // the ★ exit, per level
    private int _steps;
    private int _level = 1;
    private int _score;
    private bool _done;
    private string _result = "";

    public MazeGame()
    {
        for (int i = 0; i < Size * Size; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "maze";
    public override string Name => "Maze";
    public override int Columns => Size;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _score;
    public override string? ResultText => _done ? _result : null;

    public override string StatusText => _done
        ? $"Escaped — {_score} points."
        : $"Level {_level}/{Levels} · {_steps} steps · reach ★";

    public override void Reset()
    {
        _level = 1;
        _score = 0;
        _steps = 0;
        _done = false;
        _result = "";
        ScoreCounts = false;
        Generate();
        Redraw();
    }

    /// <summary>Stack-based recursive backtracker — no recursion, always terminates.</summary>
    private void Generate()
    {
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                _wall[x, y] = true;

        var visited = new bool[7, 7];   // cells live on odd coordinates 1..13
        var stack = new Stack<(int cx, int cy)>();
        visited[0, 0] = true;
        _wall[1, 1] = false;
        stack.Push((0, 0));

        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Peek();
            var dirs = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
            for (int i = dirs.Length - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
            }

            bool advanced = false;
            foreach (var (dx, dy) in dirs)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx > 6 || ny > 6 || visited[nx, ny]) continue;
                visited[nx, ny] = true;
                _wall[2 * cx + 1 + dx, 2 * cy + 1 + dy] = false;   // the gap
                _wall[2 * nx + 1, 2 * ny + 1] = false;             // the cell
                stack.Push((nx, ny));
                advanced = true;
                break;
            }
            if (!advanced) stack.Pop();
        }

        // Levels 1-2 run top-left → bottom-right; level 3 swaps to different
        // corners so the trek through the fog is a brand-new route.
        if (_level == 3)
        {
            _px = Last; _py = Last;
            _ex = 1; _ey = 1;
        }
        else
        {
            _px = 1; _py = 1;
            _ex = Last; _ey = Last;
        }
        _steps = 0;
    }

    public override bool OnKey(GameKey key)
    {
        if (_done) return false;
        var (dx, dy) = key switch
        {
            GameKey.Up => (0, -1),
            GameKey.Down => (0, 1),
            GameKey.Left => (-1, 0),
            GameKey.Right => (1, 0),
            _ => (0, 0),
        };
        if (dx == 0 && dy == 0) return false;
        Move(dx, dy);
        Redraw();
        return true;
    }

    public override bool OnTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;
        int tx = index % Size, ty = index / Size;
        if (_wall[tx, ty]) return false;

        // A gap next to the player steps straight through it.
        if (Math.Abs(tx - _px) + Math.Abs(ty - _py) == 1)
        {
            Move(Math.Sign(tx - _px), Math.Sign(ty - _py));
            Redraw();
            return true;
        }
        // Any other corridor tap walks one cell along the shortest path.
        if (StepToward(tx, ty)) Redraw();
        return true;
    }

    /// <summary>Moves one cell through the gap in the given direction, if open.</summary>
    private bool Move(int dx, int dy)
    {
        int nx = _px + 2 * dx, ny = _py + 2 * dy;
        if (nx < 1 || ny < 1 || nx > Last || ny > Last) return false;
        if (_wall[_px + dx, _py + dy] || _wall[nx, ny]) return false;
        _px = nx;
        _py = ny;
        _steps++;
        if (_px == _ex && _py == _ey) LevelComplete();
        return true;
    }

    private void LevelComplete()
    {
        _score += Math.Max(25, 150 - _steps / 2);
        if (_level == Levels)
        {
            _done = true;
            ScoreCounts = true;
            _result = $"Out of the maze — {_score} points.";
            return;
        }
        _level++;
        Generate();
    }

    /// <summary>
    /// Breadth-first search from the player over the cell graph, then a single
    /// step along the path toward the tapped corridor cell.
    /// </summary>
    private bool StepToward(int tx, int ty)
    {
        var dist = new int[Size, Size];
        var prevX = new int[Size, Size];
        var prevY = new int[Size, Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++) dist[x, y] = -1;
        dist[_px, _py] = 0;

        var queue = new Queue<(int x, int y)>();
        queue.Enqueue((_px, _py));
        int[] dxs = { 0, 1, 0, -1 };
        int[] dys = { -1, 0, 1, 0 };
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int gx = x + dxs[d], gy = y + dys[d];
                int nx = x + 2 * dxs[d], ny = y + 2 * dys[d];
                if (nx < 1 || ny < 1 || nx > Last || ny > Last) continue;
                if (_wall[gx, gy] || dist[nx, ny] >= 0) continue;
                dist[nx, ny] = dist[x, y] + 1;
                prevX[nx, ny] = x;
                prevY[nx, ny] = y;
                queue.Enqueue((nx, ny));
            }
        }

        // A tapped gap stands for the cell on its far side; pick the nearer one.
        int targetX = tx, targetY = ty;
        if (tx % 2 == 0 || ty % 2 == 0)
        {
            int ax, ay, bx, by;
            if (tx % 2 == 0) { ax = tx - 1; ay = ty; bx = tx + 1; by = ty; }
            else { ax = tx; ay = ty - 1; bx = tx; by = ty + 1; }
            bool aOk = ax >= 1 && ay >= 1 && ax <= Last && ay <= Last && dist[ax, ay] >= 0;
            bool bOk = bx >= 1 && by >= 1 && bx <= Last && by <= Last && dist[bx, by] >= 0;
            if (!aOk && !bOk) return false;
            if (aOk && (!bOk || dist[bx, by] >= dist[ax, ay])) { targetX = ax; targetY = ay; }
            else { targetX = bx; targetY = by; }
        }

        if (dist[targetX, targetY] < 0) return false;
        if (targetX == _px && targetY == _py) return true;

        // Walk the parent chain back to the first cell after the player.
        int sx = targetX, sy = targetY;
        while (prevX[sx, sy] != _px || prevY[sx, sy] != _py)
        {
            int ox = prevX[sx, sy], oy = prevY[sx, sy];
            if (ox == 0 && oy == 0) return false;   // safety: off the BFS tree
            sx = ox;
            sy = oy;
        }
        return Move(Math.Sign(sx - _px), Math.Sign(sy - _py));
    }

    public override void Redraw()
    {
        // Fog of war: both foggy levels dim everything beyond four cells.
        int limit = _level >= 2 ? 8 : int.MaxValue;
        var dist = new int[Size, Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++) dist[x, y] = -1;
        if (limit != int.MaxValue)
        {
            var queue = new Queue<(int x, int y)>();
            dist[_px, _py] = 0;
            queue.Enqueue((_px, _py));
            int[] dxs = { 0, 1, 0, -1 };
            int[] dys = { -1, 0, 1, 0 };
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                if (dist[x, y] >= limit) continue;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dxs[d], ny = y + dys[d];
                    if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) continue;
                    if (_wall[nx, ny] || dist[nx, ny] >= 0) continue;
                    dist[nx, ny] = dist[x, y] + 1;
                    queue.Enqueue((nx, ny));
                }
            }
        }

        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var c = _cells[y * Size + x];
                bool fogged = limit != int.MaxValue && dist[x, y] < 0;
                c.Text = "";
                c.FontSize = 12;
                c.Bold = false;
                c.IsEnabled = !_done && !_wall[x, y];

                if (x == _px && y == _py)
                {
                    c.Text = "●";
                    c.Background = Accent;
                    c.Foreground = Cell;
                    c.Bold = true;
                }
                else if (x == _ex && y == _ey)
                {
                    c.Text = "★";
                    c.Background = Good;
                    c.Foreground = Cell;
                    c.Bold = true;
                }
                else if (fogged)
                {
                    c.Background = _wall[x, y] ? DimWall : DimFloor;
                    c.Foreground = Muted;
                    c.IsEnabled = false;
                }
                else if (_wall[x, y])
                {
                    c.Background = Ink;
                }
                else
                {
                    c.Background = Cell;
                }
            }
    }
}
