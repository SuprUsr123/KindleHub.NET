using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Gomoku, ported from the official client — get five of your marks in a row, in
/// any direction, on a 13×13 board. You are X. The computer wins when it can,
/// blocks when it must, and otherwise builds and breaks open threes and fours.
/// </summary>
public sealed class GomokuGame : GameBase
{
    private const int Side = 13;
    private const int Win = 5;

    private const int Empty = 0, You = 1, Cpu = 2;
    private static readonly (int Dx, int Dy)[] Dirs = { (1, 0), (0, 1), (1, 1), (1, -1) };

    private readonly List<GameCell> _cells = new();
    private readonly int[,] _b = new int[Side, Side];
    private int _lastCpu = -1;
    private bool _over, _win, _draw;
    private string _msg = "Your move.";

    public GomokuGame()
    {
        for (int i = 0; i < Side * Side; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "gomoku";
    public override string Name => "Gomoku";
    public override int Columns => Side;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score => _win ? 200 : 0;

    public override string? ResultText => !_over ? null
        : _win ? "Five in a row — you win!"
        : _draw ? "Board full — a draw."
        : "The computer made five.";

    public override string StatusText => _over ? (ResultText ?? "Game over.") : _msg;

    public override void Reset()
    {
        Array.Clear(_b, 0, _b.Length);
        _lastCpu = -1;
        _over = _win = _draw = false;
        ScoreCounts = false;
        _msg = "Your move · get five X in a row.";
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _cells.Count || _over) return false;
        int x = index % Side, y = index / Side;
        if (_b[x, y] != Empty) return false;

        Place(x, y, You);
        if (WinsAt(x, y, You))
        {
            _over = _win = true;
            ScoreCounts = true;
            _msg = "Five in a row!";
            Redraw();
            return true;
        }

        var cpu = ComputerMove();
        if (cpu is { } c)
        {
            Place(c.X, c.Y, Cpu);
            _lastCpu = c.Y * Side + c.X;
            if (WinsAt(c.X, c.Y, Cpu))
            {
                _over = true;
                _msg = "The computer made five.";
                Redraw();
                return true;
            }
        }

        if (BoardFull())
        {
            _over = _draw = true;
            _msg = "Board full.";
        }
        else _msg = "Your move.";
        Redraw();
        return true;
    }

    private bool BoardFull()
    {
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
                if (_b[x, y] == Empty) return false;
        return true;
    }

    private (int X, int Y)? ComputerMove()
    {
        var win = WinningCell(Cpu);
        if (win is { } w) return w;
        var block = WinningCell(You);
        if (block is { } b) return b;

        int bestScore = int.MinValue;
        (int X, int Y) best = (Side / 2, Side / 2);
        bool any = false;
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
            {
                if (_b[x, y] != Empty || !NearStone(x, y)) continue;
                int score = LineScore(x, y, Cpu) + LineScore(x, y, You) * 9 / 10
                            + Random.Shared.Next(3);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = (x, y);
                    any = true;
                }
            }
        return any ? best : null;
    }

    private (int X, int Y)? WinningCell(int who)
    {
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
            {
                if (_b[x, y] != Empty) continue;
                _b[x, y] = who;
                bool wins = WinsAt(x, y, who);
                _b[x, y] = Empty;
                if (wins) return (x, y);
            }
        return null;
    }

    /// <summary>Pattern score for playing on (x, y): open fours beat threes beat twos.</summary>
    private int LineScore(int x, int y, int who)
    {
        int total = 0;
        foreach (var (dx, dy) in Dirs)
        {
            int run = 1, openEnds = 0;
            foreach (int sign in new[] { 1, -1 })
            {
                int step = 1;
                while (true)
                {
                    int cx = x + dx * step * sign, cy = y + dy * step * sign;
                    if (cx < 0 || cx >= Side || cy < 0 || cy >= Side) break;
                    if (_b[cx, cy] != who) break;
                    run++;
                    step++;
                }
                int ox = x + dx * sign, oy = y + dy * sign;
                if (ox >= 0 && ox < Side && oy >= 0 && oy < Side && _b[ox, oy] == Empty) openEnds++;
            }

            total += run >= Win ? 100000
                : run == 4 ? openEnds == 2 ? 10000 : openEnds == 1 ? 1500 : 0
                : run == 3 ? openEnds == 2 ? 800 : openEnds == 1 ? 80 : 0
                : run == 2 ? openEnds == 2 ? 60 : openEnds == 1 ? 10 : 0
                : openEnds == 2 ? 4 : openEnds == 1 ? 1 : 0;
        }
        return total;
    }

    private bool NearStone(int x, int y)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= Side || ny < 0 || ny >= Side) continue;
                if (_b[nx, ny] != Empty) return true;
            }
        return false;
    }

    private void Place(int x, int y, int who) => _b[x, y] = who;

    /// <summary>Five or more in a row through (x, y) — overlines count here.</summary>
    private bool WinsAt(int x, int y, int who)
    {
        foreach (var (dx, dy) in Dirs)
        {
            int run = 1;
            foreach (int sign in new[] { 1, -1 })
                for (int step = 1; ; step++)
                {
                    int cx = x + dx * step * sign, cy = y + dy * step * sign;
                    if (cx < 0 || cx >= Side || cy < 0 || cy >= Side) break;
                    if (_b[cx, cy] != who) break;
                    run++;
                }
            if (run >= Win) return true;
        }
        return false;
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            int v = _b[i % Side, i / Side];
            c.Text = v == You ? "X" : v == Cpu ? "O" : "";
            c.Background = Cell;
            c.Foreground = v == Cpu ? Bad : Ink;
            c.FontSize = 16;
            c.Bold = v != Empty;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
            c.IsEnabled = !_over && v == Empty;
            if (i == _lastCpu)
            {
                c.BorderBrush = Warn;
                c.BorderThickness = new Avalonia.Thickness(1);
            }
        }
    }
}
