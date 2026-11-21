using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// 8-Ball practice, ported from the official client — one-player mode on a 10×5
/// table with six pockets. Tap a ball, then one of the eight neighbours to shoot:
/// the ball slides cell by cell until it drops in a pocket, stops at a rail, or
/// bumps another ball one square along. Clear all fifteen in as few shots as you can.
/// </summary>
public sealed class EightBallGame : GameBase
{
    private const int Cols = 10;
    private const int Rows = 5;
    private const int BallCount = 15;
    private const int ShotCap = 30;
    private const int Cue = 0;
    private static readonly TimeSpan StepTime = TimeSpan.FromMilliseconds(60);

    // Corners plus the two side-rail midpoints.
    private static readonly HashSet<int> Pockets = new() { 0, 9, 40, 49, 4, 44 };

    private readonly List<GameCell> _cells = new();
    private readonly int[] _x = new int[BallCount + 1];
    private readonly int[] _y = new int[BallCount + 1];
    private readonly bool[] _gone = new bool[BallCount + 1];
    private int _sel = -1;
    private int _animBall = -1;
    private int _adx, _ady;
    private TimeSpan _animTime;
    private int _shots;
    private int _fouls;
    private int _potted;
    private bool _over;
    private string _msg = "Tap a ball.";

    public EightBallGame()
    {
        for (int i = 0; i < Cols * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "eightball";
    public override string Name => "8-Ball";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool NeedsTicks => _animBall >= 0;

    public override int Score => _over ? Math.Max(20, 100 * _potted - 10 * _shots - 50 * _fouls) : 0;

    public override string? ResultText => !_over ? null
        : _potted >= BallCount ? $"Table cleared in {_shots} shots."
        : "Out of shots.";

    public override string StatusText
    {
        get
        {
            if (_over) return ResultText ?? "Practice over.";
            if (_animBall >= 0) return "Rolling…";
            return $"Shots {_shots}/{ShotCap} · potted {_potted}/15 · fouls {_fouls} · ◆ means diagonal";
        }
    }

    public override void Reset()
    {
        _x[Cue] = 1;
        _y[Cue] = 2;
        for (int b = 1; b <= BallCount; b++)
        {
            _x[b] = 6 + (b - 1) % 3;
            _y[b] = (b - 1) / 3;
        }
        Array.Clear(_gone, 0, _gone.Length);
        _sel = -1;
        _animBall = -1;
        _animTime = TimeSpan.Zero;
        _shots = _fouls = _potted = 0;
        _over = false;
        ScoreCounts = false;
        _msg = "Tap a ball, then a neighbour cell.";
        Redraw();
    }

    public override bool Tick(TimeSpan elapsed)
    {
        if (_animBall < 0) return false;
        _animTime += elapsed;
        bool changed = false;
        int guard = 0;
        while (_animTime >= StepTime && _animBall >= 0 && guard++ < 15)
        {
            _animTime -= StepTime;
            StepAnimation();
            Redraw(); // each step is one visible cell of travel
            changed = true;
        }
        if (guard >= 15) _animTime = TimeSpan.Zero; // a slow frame can't teleport a ball
        return changed;
    }

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _cells.Count || _over || _animBall >= 0) return false;
        int x = index % Cols, y = index / Cols;

        if (_sel < 0)
        {
            int ball = BallAt(x, y);
            if (ball < 0) return false;
            _sel = ball;
            Redraw();
            return true;
        }

        int dx = x - _x[_sel], dy = y - _y[_sel];
        if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 && (dx != 0 || dy != 0))
        {
            _animBall = _sel;
            _adx = dx;
            _ady = dy;
            _sel = -1;
            _shots++;
            _animTime = StepTime; // first step fires on the next tick
            Redraw();
            return true;
        }

        int other = BallAt(x, y);
        if (other >= 0)
        {
            _sel = other;
            Redraw();
            return true;
        }
        _sel = -1;
        Redraw();
        return true;
    }

    private void StepAnimation()
    {
        int b = _animBall;
        int nx = _x[b] + _adx, ny = _y[b] + _ady;
        if (!In(nx, ny))
        {
            _msg = "Rail.";
            EndAnimation();
            return;
        }

        int cell = ny * Cols + nx;
        if (Pockets.Contains(cell))
        {
            _x[b] = nx;
            _y[b] = ny;
            Pot(b);
            EndAnimation();
            return;
        }

        int other = BallAt(nx, ny);
        if (other >= 0)
        {
            if (Push(other, _adx, _ady))
            {
                _x[b] = nx;
                _y[b] = ny;
                _msg = "Bump!";
            }
            else _msg = "Blocked.";
            EndAnimation();
            return;
        }

        _x[b] = nx;
        _y[b] = ny;
    }

    /// <summary>Shoves a ball one square along the shot direction, chains included.</summary>
    private bool Push(int ball, int dx, int dy)
    {
        int nx = _x[ball] + dx, ny = _y[ball] + dy;
        if (!In(nx, ny)) return false;

        if (Pockets.Contains(ny * Cols + nx))
        {
            _x[ball] = nx;
            _y[ball] = ny;
            Pot(ball);
            return true;
        }

        int next = BallAt(nx, ny);
        if (next >= 0 && !Push(next, dx, dy)) return false;

        _x[ball] = nx;
        _y[ball] = ny;
        return true;
    }

    private void Pot(int ball)
    {
        _gone[ball] = true;
        if (ball == Cue)
        {
            _fouls++;
            _msg = "Scratch! Cue respotted.";
            RespotCue();
        }
        else
        {
            _potted++;
            _msg = $"Ball {ball} potted!";
        }
    }

    private void RespotCue()
    {
        if (BallAt(1, 2) < 0 && !Pockets.Contains(2 * Cols + 1))
        {
            _x[Cue] = 1;
            _y[Cue] = 2;
            _gone[Cue] = false;
            return;
        }
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                if (!Pockets.Contains(y * Cols + x) && BallAt(x, y) < 0)
                {
                    _x[Cue] = x;
                    _y[Cue] = y;
                    _gone[Cue] = false;
                    return;
                }
    }

    private void EndAnimation()
    {
        _animBall = -1;
        _animTime = TimeSpan.Zero;
        if (_potted >= BallCount || _shots >= ShotCap)
        {
            _over = true;
            ScoreCounts = true;
        }
        Redraw();
    }

    private int BallAt(int x, int y)
    {
        for (int b = 0; b <= BallCount; b++)
            if (!_gone[b] && _x[b] == x && _y[b] == y) return b;
        return -1;
    }

    private static bool In(int x, int y) => x >= 0 && x < Cols && y >= 0 && y < Rows;

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = false;
            c.FontSize = 14;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
        }

        foreach (var p in Pockets)
        {
            _cells[p].Background = Ink;
            _cells[p].IsEnabled = _sel >= 0; // a neighbouring pocket can be a target
        }

        if (_sel >= 0)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = _x[_sel] + dx, ny = _y[_sel] + dy;
                    if (!In(nx, ny)) continue;
                    var c = _cells[ny * Cols + nx];
                    c.BorderBrush = Accent;
                    c.BorderThickness = new Avalonia.Thickness(1);
                    c.IsEnabled = true;
                    if (BallAt(nx, ny) < 0 && !Pockets.Contains(ny * Cols + nx))
                    {
                        c.Text = dx == 0 ? (dy < 0 ? "↑" : "↓")
                            : dy == 0 ? (dx < 0 ? "←" : "→")
                            : "◆";
                        c.Foreground = Muted;
                    }
                }
        }

        for (int b = 0; b <= BallCount; b++)
        {
            if (_gone[b]) continue;
            var c = _cells[_y[b] * Cols + _x[b]];
            if (b == Cue)
            {
                c.Text = "●";
                c.Background = Cell;
                c.FontSize = 16;
                c.Bold = true;
            }
            else
            {
                c.Text = b.ToString();
                c.Background = b == 8 ? Ink : Cell;
                c.Foreground = b == 8 ? Cell : Ink;
                c.FontSize = 14;
                c.Bold = true;
            }
            c.IsEnabled |= !_over && _animBall < 0 && _sel < 0;
            if (_sel == b)
            {
                c.BorderBrush = Accent;
                c.BorderThickness = new Avalonia.Thickness(2);
            }
        }
    }
}
