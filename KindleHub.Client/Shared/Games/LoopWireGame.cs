using System;
using System.Collections.Generic;

namespace KindleHub.Client.Games;

/// <summary>
/// Loop Wire (Net) — every tile hides a wire piece and the centre tile is the
/// source ●. Tap a tile to quarter-turn it until every open end meets another
/// open end and all tiles link back to the source. Boards are generated as a
/// spanning tree from the source, so a solved orientation always exists.
/// </summary>
public sealed class LoopWireGame : GameBase
{
    private const int Side = 7;
    private const int Source = 24;   // centre of the 7×7 board

    private readonly List<GameCell> _cells = new();
    private readonly int[] _mask = new int[Side * Side];   // open sides as bits N,E,S,W
    private int _taps;
    private bool _solved;
    private string _result = "";

    public LoopWireGame()
    {
        for (int i = 0; i < Side * Side; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "loopwire";
    public override string Name => "Loop Wire";
    public override int Columns => Side;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => Math.Max(50, 300 - 2 * _taps);
    public override string? ResultText => _solved ? _result : null;

    public override string StatusText => _solved
        ? $"All wired — {Score} points."
        : $"Rotations {_taps} · link every tile to ●";

    public override void Reset()
    {
        _taps = 0;
        _solved = false;
        _result = "";
        ScoreCounts = false;
        Generate();
        Redraw();
    }

    /// <summary>
    /// Randomised iterative depth-first search from the source: every tree edge
    /// opens one side of each tile it touches, leaves become endpoints, and the
    /// source stays fixed as the orientation anchor.
    /// </summary>
    private void Generate()
    {
        Array.Clear(_mask, 0, _mask.Length);
        var visited = new bool[Side, Side];
        var stack = new Stack<(int x, int y)>();
        int sx = Source % Side, sy = Source / Side;
        visited[sx, sy] = true;
        stack.Push((sx, sy));

        while (stack.Count > 0)
        {
            var (x, y) = stack.Peek();
            var dirs = new[] { 0, 1, 2, 3 };
            for (int i = dirs.Length - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
            }

            bool advanced = false;
            foreach (int d in dirs)
            {
                var (dx, dy) = Delta(d);
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= Side || ny >= Side) continue;
                if (visited[nx, ny]) continue;
                visited[nx, ny] = true;
                _mask[y * Side + x] |= Bit(d);
                _mask[ny * Side + nx] |= Bit((d + 2) % 4);
                stack.Push((nx, ny));
                advanced = true;
                break;
            }
            if (!advanced) stack.Pop();
        }

        // Scramble every tile but the source by 0-3 quarter turns.
        for (int i = 0; i < _mask.Length; i++)
        {
            if (i == Source) continue;
            int turns = Random.Shared.Next(4);
            for (int t = 0; t < turns; t++) _mask[i] = Rotate(_mask[i]);
        }
        // Turning the first tile that isn't a four-way cross guarantees the
        // start is not already solved (a cross is the only piece a turn keeps).
        if (IsSolved())
            for (int i = 0; i < _mask.Length; i++)
                if (_mask[i] != 15)
                {
                    _mask[i] = Rotate(_mask[i]);
                    break;
                }
    }

    private static (int dx, int dy) Delta(int dir) => dir switch
    {
        0 => (0, -1),   // north
        1 => (1, 0),    // east
        2 => (0, 1),    // south
        _ => (-1, 0),   // west
    };

    private static int Bit(int dir) => 1 << dir;

    /// <summary>One clockwise quarter turn: N→E→S→W→N.</summary>
    private static int Rotate(int mask) => ((mask << 1) | (mask >> 3)) & 0xF;

    public override bool OnTap(int index)
    {
        if (_solved || index < 0 || index >= _cells.Count || index == Source) return false;
        _mask[index] = Rotate(_mask[index]);
        _taps++;
        if (IsSolved())
        {
            _solved = true;
            ScoreCounts = true;
            _result = $"Wired up in {_taps} rotations.";
        }
        Redraw();
        return true;
    }

    /// <summary>
    /// Solved means every open side faces a neighbour that answers it (nothing
    /// dangles into a wall or a closed side), every tile reaches the source,
    /// and the network is a tree — exactly tiles−1 connections, so no loops.
    /// </summary>
    private bool IsSolved()
    {
        for (int i = 0; i < _mask.Length; i++)
            for (int d = 0; d < 4; d++)
            {
                if ((_mask[i] & Bit(d)) == 0) continue;
                var (dx, dy) = Delta(d);
                int x = i % Side + dx, y = i / Side + dy;
                if (x < 0 || y < 0 || x >= Side || y >= Side) return false;
                if ((_mask[y * Side + x] & Bit((d + 2) % 4)) == 0) return false;
            }

        // Flood from the source, counting tiles reached and matched joins;
        // each join is seen from both of its tiles, so joins/2 is the number
        // of distinct connections in the wired-up network.
        var seen = new bool[_mask.Length];
        var queue = new Queue<int>();
        queue.Enqueue(Source);
        seen[Source] = true;
        int tiles = 0, joins = 0;
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            tiles++;
            for (int d = 0; d < 4; d++)
            {
                if ((_mask[cur] & Bit(d)) == 0) continue;
                var (dx, dy) = Delta(d);
                int x = cur % Side + dx, y = cur / Side + dy;
                if (x < 0 || y < 0 || x >= Side || y >= Side) continue;
                int n = y * Side + x;
                if ((_mask[n] & Bit((d + 2) % 4)) == 0) continue;
                joins++;
                if (!seen[n])
                {
                    seen[n] = true;
                    queue.Enqueue(n);
                }
            }
        }
        return tiles == _mask.Length && joins / 2 == _mask.Length - 1;
    }

    /// <summary>Box-drawing glyph for a connection mask; endpoints use arrows.</summary>
    private static string Glyph(int mask) => mask switch
    {
        1 => "↑", 2 => "→", 4 => "↓", 8 => "←",
        5 => "│", 10 => "─",
        3 => "└", 6 => "┌", 12 => "┐", 9 => "┘",
        7 => "├", 14 => "┬", 13 => "┤", 11 => "┴",
        15 => "┼",
        _ => "",
    };

    public override void Redraw()
    {
        var lit = new bool[_mask.Length];
        // Light up everything reachable from the source — the classic Net feedback.
        var queue = new Queue<int>();
        queue.Enqueue(Source);
        lit[Source] = true;
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                if ((_mask[cur] & Bit(d)) == 0) continue;
                var (dx, dy) = Delta(d);
                int x = cur % Side + dx, y = cur / Side + dy;
                if (x < 0 || y < 0 || x >= Side || y >= Side) continue;
                int n = y * Side + x;
                if (lit[n] || (_mask[n] & Bit((d + 2) % 4)) == 0) continue;
                lit[n] = true;
                queue.Enqueue(n);
            }
        }

        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = i == Source ? "●" : Glyph(_mask[i]);
            c.Background = i == Source ? Warn : lit[i] ? Cell : Board;
            c.Foreground = i == Source ? Cell : Ink;
            c.IsEnabled = !_solved && i != Source;
            c.FontSize = 20;
            c.Bold = true;
        }
    }
}
