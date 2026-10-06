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

        bool horizontal;
        bool reverse;
        switch (key)
        {
            case GameKey.Left:  horizontal = true;  reverse = false; break;
            case GameKey.Right: horizontal = true;  reverse = true;  break;
            case GameKey.Up:    horizontal = false; reverse = false; break;
            case GameKey.Down:  horizontal = false; reverse = true;  break;
            default: return false;
        }

        if (!Move(horizontal, reverse)) return true;

        Redraw();
        if (_board.Cast<int>().Max() >= 2048) { _won = true; ScoreCounts = true; }
        if (!HasMoves()) { _over = true; ScoreCounts = _won; }
        return true;
    }

    // 2048 is controlled with directional keys; tapping an individual tile has
    // no game action, but GameBase requires every game to define tap behavior.
    public override bool OnTap(int index) => false;

    private bool Move(bool horizontal, bool reverse)
    {
        bool moved = false;
        for (int line = 0; line < N; line++)
        {
            var before = new int[N];
            for (int k = 0; k < N; k++)
                before[k] = horizontal ? _board[k, line] : _board[line, k];

            if (reverse) Array.Reverse(before);
            var (merged, gained) = Collapse(before);
            if (reverse) Array.Reverse(merged);

            for (int k = 0; k < N; k++)
            {
                if (horizontal) _board[k, line] = merged[k];
                else _board[line, k] = merged[k];
                if (before[k] != merged[k]) moved = true;
            }
            _score += gained;
        }

        if (moved) Spawn();
        return moved;
    }

    private static (int[] merged, int gained) Collapse(int[] line)
    {
        var values = line.Where(value => value != 0).ToList();
        var result = new List<int>();
        int gained = 0;

        for (int i = 0; i < values.Count; i++)
        {
            if (i + 1 < values.Count && values[i] == values[i + 1])
            {
                int merged = values[i] * 2;
                result.Add(merged);
                gained += merged;
                i++;
            }
            else
            {
                result.Add(values[i]);
            }
        }

        while (result.Count < line.Length) result.Add(0);
        return (result.ToArray(), gained);
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
