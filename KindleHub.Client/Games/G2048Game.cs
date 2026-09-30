using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>2048 — slide tiles, merge equal numbers, reach the target tile.</summary>
public sealed class G2048Game : GameBase
{
    private const int N = 4;

    private readonly List<GameCell> _cells = new();
    private readonly int[,] _board = new int[N, N];
    private int _score;
    private bool _won;
    private bool _over;

    public G2048Game()
    {
        for (int i = 0; i < N * N; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "g2048";
    public override string Name => "2048";
    public override int Columns => N;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _score;
    public override string? ResultText =>
        _won && !_over ? "2048 reached — keep going for a higher score." :
        _over ? (_won ? $"You reached 2048. Final score {_score}." : $"No moves left. Final score {_score}.") : null;

    public override string StatusText => _over
        ? $"Final score {_score}."
        : $"Score {_score} · arrows or WASD";

    public override void Reset()
    {
        Array.Clear(_board, 0, _board.Length);
        _score = 0;
        _won = false;
        _over = false;
        ScoreCounts = false;
        Spawn(); Spawn();
        Redraw();
    }

    private void Spawn()
    {
        var free = new List<(int x, int y)>();
        for (int cy = 0; cy < N; cy++)
            for (int cx = 0; cx < N; cx++)
                if (_board[cx, cy] == 0) free.Add((cx, cy));
        if (free.Count == 0) return;
        var (sx, sy) = free[Random.Shared.Next(free.Count)];
        _board[sx, sy] = Random.Shared.Next(10) == 0 ? 4 : 2;
    }

    public override bool OnKey(GameKey key)
    {
        if (_over) return false;
        // Sliding "left" collapses each row from the left, so we walk rows left→right.
        // Sliding "up" does the same down each column, so columns walk top→bottom.
        (int[] order, bool horizontal) = key switch
        {
            GameKey.Left => (new[] { 0, 1, 2, 3 }, true),
            GameKey.Right => (new[] { 3, 2, 1, 0 }, true),
            GameKey.Up => (new[] { 0, 1, 2, 3 }, false),
            GameKey.Down => (new[] { 3, 2, 1, 0 }, false),
            _ => (Array.Empty<int>(), true),
        };
        if (order.Length == 0) return false;
        if (!Move(order, horizontal)) return true;

        Redraw();
        if (_board.Cast<int>().Max() >= 2048) { _won = true; ScoreCounts = true; }
        if (!HasMoves()) { _over = true; ScoreCounts = _won; }
        return true;
    }

    private bool Move(int[] order, bool horizontal)
    {
        bool moved = false;
        foreach (var line in order)
        {
            var before = new int[N];
            for (int k = 0; k < N; k++)
                before[k] = horizontal ? _board[line, k] : _board[k, line];

            var (merged, gained) = Collapse(before);
            for (int k = 0; k < N; k++)
            {
                if (horizontal) _board[line, k] = merged[k]; else _board[k, line] = merged[k];
                if (before[k] != merged[k]) moved = true;
            }
            _score += gained;
        }
        if (moved) Spawn();
        return moved;
    }

    private static (int[] merged, int gained) Collapse(int[] line)
    {
        var vals = line.Where(v => v != 0).ToList();
        var outv = new List<int>();
        int gained = 0;
        for (int i = 0; i < vals.Count; i++)
        {
            if (i + 1 < vals.Count && vals[i] == vals[i + 1])
            {
                outv.Add(vals[i] * 2);
                gained += vals[i] * 2;
                i++;
            }
            else outv.Add(vals[i]);
        }
        while (outv.Count < line.Length) outv.Add(0);
        return (outv.ToArray(), gained);
    }

    private bool HasMoves()
    {
        for (int x = 0; x < N; x++)
            for (int y = 0; y < N; y++)
            {
                if (_board[x, y] == 0) return true;
                if (x + 1 < N && _board[x, y] == _board[x + 1, y]) return true;
                if (y + 1 < N && _board[x, y] == _board[x, y + 1]) return true;
            }
        return false;
    }

    /// <summary>2048 is played entirely from the arrow keys, so the board isn't tappable.</summary>
    public override bool OnTap(int index) => false;

    public override void Redraw()    {
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            int v = _board[x, y];
            var c = _cells[y * N + x];
            if (v == 0) { c.Text = ""; c.Background = Board; c.IsEnabled = true; c.Foreground = Ink; c.FontSize = 20; }
            else
            {
                c.Text = v.ToString();
                c.Background = Tiles4[Math.Clamp((int)Math.Log2(v) - 1, 0, Tiles4.Length - 1)];
                c.Foreground = v >= 64 ? Cell : Ink;
                c.FontSize = v >= 1000 ? 15 : v >= 128 ? 19 : 24;
                c.Bold = true;
                c.IsEnabled = true;
            }
        }
    }
}
