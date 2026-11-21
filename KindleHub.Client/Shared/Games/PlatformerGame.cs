using System;
using System.Collections.Generic;

namespace KindleHub.Client.Games;

/// <summary>
/// Pixel Hop — a Mario-style platformer with three hand-built levels, ported from
/// the official client. Run and jump across platforms and spikes to reach the flag;
/// five deaths end the run.
/// </summary>
public sealed class PlatformerGame : GameBase
{
    private const int Width = 20;
    private const int Height = 12;
    private const int LevelCount = 3;
    private const int MaxDeaths = 5;

    private const int FrameMs = 50;        // fixed simulation frame
    private const int MaxFrames = 6;       // catch-up guard: at most 6 frames per Tick
    private const double Dt = 0.05;        // frame length in seconds
    private const double Gravity = 37.5;   // cells/s²
    private const double JumpV = 15.0;     // cells/s up — clears three cells
    private const double RunSpeed = 5.0;   // cells/s while a run pulse lasts
    private const double MovePulse = 0.25; // seconds of run bought by one key press

    // '#' platform, '^' spike, 'c' coin, 'F' flag, 'P' start. Rows 8 and 5 are the
    // jump-reachable platform bands from the ground and from each other.
    private static readonly string[][] Levels =
    {
        new[]
        {
            "                    ",
            "                    ",
            "                    ",
            "                    ",
            "                    ",
            "                    ",
            "       c            ",
            "                    ",
            "     #####          ",
            "                    ",
            " Pc    ^^        F  ",
            "####################",
        },
        new[]
        {
            "                    ",
            "                    ",
            "                    ",
            "                    ",
            "     c              ",
            "    #####           ",
            "                    ",
            "                    ",
            "    ####     ####   ",
            "         c          ",
            " P        ^^    F   ",
            "####   #####   #####",
        },
        new[]
        {
            "                    ",
            "                    ",
            "           c        ",
            "         #####      ",
            "      c             ",
            "    #####           ",
            "                    ",
            "                    ",
            "  #####             ",
            "                c   ",
            " P    ^^      ^^  F ",
            "###  ######  #######",
        },
    };

    private readonly List<GameCell> _cells = new();
    private readonly char[,] _grid = new char[Width, Height];
    private readonly HashSet<(int x, int y)> _taken = new(); // coins already pocketed this level
    private double _x, _y, _vy;
    private double _moveIn;
    private int _moveDir;
    private int _level = 1;
    private int _deaths;
    private int _coinsTotal;
    private int _levelsDone;
    private int _score;
    private string _cause = "";
    private TimeSpan _acc;
    private bool _over;
    private bool _won;

    public PlatformerGame()
    {
        for (int i = 0; i < Width * Height; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "platformer";
    public override string Name => "Pixel Hop";
    public override int Columns => Width;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => true;

    public override int Score => Math.Clamp(_score, 0, 1_000_000);

    public override string? ResultText => !_over
        ? null
        : _won ? $"All levels cleared — {Score} points." : $"{_cause} — {Score} points.";

    public override string StatusText => _over
        ? $"Final score {Score}."
        : $"Level {_level}/{LevelCount} · coins {_coinsTotal} · deaths {_deaths}/{MaxDeaths}";

    public override void Reset()
    {
        _level = 1;
        _deaths = 0;
        _coinsTotal = 0;
        _levelsDone = 0;
        _score = 0;
        _cause = "";
        _acc = TimeSpan.Zero;
        _over = false;
        _won = false;
        ScoreCounts = false;
        LoadLevel(restart: false);
        Redraw();
    }

    private void LoadLevel(bool restart)
    {
        if (!restart) _taken.Clear();
        var rows = Levels[_level - 1];
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            char ch = rows[y][x];
            if (ch == 'P')
            {
                _x = x;
                _y = y;
                ch = ' ';
            }
            if (ch == 'c' && _taken.Contains((x, y))) ch = ' ';
            _grid[x, y] = ch;
        }
        _vy = 0;
        _moveDir = 0;
        _moveIn = 0;
        _acc = TimeSpan.Zero;
    }

    private bool Solid(int x, int y)
    {
        if (x < 0 || x >= Width) return true; // side walls stop the runner
        if (y < 0 || y >= Height) return false;
        return _grid[x, y] == '#';
    }

    public override bool Tick(TimeSpan elapsed)
    {
        if (_over) return false;
        _acc += elapsed;
        bool changed = false;
        int guard = 0;
        while (_acc.TotalMilliseconds >= FrameMs && guard++ < MaxFrames)
        {
            _acc -= TimeSpan.FromMilliseconds(FrameMs);
            FrameStep();
            changed = true;
            if (_over) break;
        }
        if (_acc.TotalMilliseconds > FrameMs * MaxFrames) _acc = TimeSpan.FromMilliseconds(FrameMs * MaxFrames);
        if (changed) Redraw();
        return changed;
    }

    private void FrameStep()
    {
        if (_moveIn > 0) _moveIn -= Dt;
        else _moveDir = 0;

        // Horizontal run; solid cell faces stop the runner.
        double nx = _x + _moveDir * RunSpeed * Dt;
        int curCellX = (int)Math.Round(_x);
        int newCellX = (int)Math.Round(nx);
        int cy = (int)Math.Round(_y);
        if (_moveDir != 0 && newCellX != curCellX && Solid(newCellX, cy)) nx = _x;
        _x = Math.Clamp(nx, 0.5, Width - 1.5);

        // Vertical: platforms are one-way — you only land when falling onto them.
        double prevY = _y;
        _vy += Gravity * Dt;
        double ny = _y + _vy * Dt;
        if (_vy > 0)
        {
            int pcx = (int)Math.Round(_x);
            int newCell = (int)Math.Round(ny);
            if (newCell > (int)Math.Round(prevY) && Solid(pcx, newCell))
            {
                ny = newCell - 1;
                _vy = 0;
            }
        }
        _y = ny;

        if (_y > Height + 0.5) { Die(); return; } // fell into a pit

        int px = (int)Math.Round(_x);
        int py = Math.Clamp((int)Math.Round(_y), 0, Height - 1);
        char here = _grid[px, py];
        if (here == '^') { Die(); return; }
        if (here == 'c') TakeCoin(px, py);
        else if (py > 0 && _grid[px, py - 1] == 'c') TakeCoin(px, py - 1); // coin overhead
        if (here == 'F') LevelDone();
    }

    private void TakeCoin(int x, int y)
    {
        _grid[x, y] = ' ';
        _taken.Add((x, y));
        _coinsTotal++;
    }

    private void Die()
    {
        _deaths++;
        if (_deaths >= MaxDeaths)
        {
            _over = true;
            _won = false;
            _cause = "Game over";
            _score = Math.Max(1, 100 * _levelsDone + 10 * _coinsTotal - 10 * _deaths);
            ScoreCounts = _levelsDone > 0 || _coinsTotal > 0; // only runs with some progress
            Redraw();
            return;
        }
        LoadLevel(restart: true);
        Redraw();
    }

    private void LevelDone()
    {
        _levelsDone++;
        if (_levelsDone >= LevelCount)
        {
            _over = true;
            _won = true;
            _score = Math.Max(20, 100 * _levelsDone + 10 * _coinsTotal - 10 * _deaths);
            ScoreCounts = true;
            Redraw();
            return;
        }
        _level++;
        LoadLevel(restart: false);
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (_over) return false;
        switch (key)
        {
            case GameKey.Left:
                _moveDir = -1;
                _moveIn = MovePulse;
                return true;
            case GameKey.Right:
                _moveDir = 1;
                _moveIn = MovePulse;
                return true;
            case GameKey.Up:
            case GameKey.Confirm:
                return Jump();
            default:
                return false;
        }
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;
        return Jump();
    }

    private bool Jump()
    {
        int px = (int)Math.Round(_x), py = Math.Clamp((int)Math.Round(_y), 0, Height - 1);
        if (_vy != 0 || !Solid(px, py + 1)) return true; // consumed, but airborne
        _vy = -JumpV;
        return true;
    }

    public override void Redraw()
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            var c = _cells[y * Width + x];
            c.Text = "";
            c.Background = Cell;
            c.Foreground = Ink;
            c.IsEnabled = !_over;
            c.FontSize = 14;
            c.Bold = false;

            switch (_grid[x, y])
            {
                case '#':
                    c.Text = "█";
                    c.Background = Ink;
                    c.Foreground = Cell;
                    break;
                case '^':
                    c.Text = "▲";
                    c.Foreground = Bad;
                    c.Bold = true;
                    break;
                case 'c':
                    c.Text = "♦";
                    c.Foreground = Warn;
                    c.Bold = true;
                    break;
                case 'F':
                    c.Text = "★";
                    c.Foreground = Good;
                    c.Bold = true;
                    break;
            }
        }

        int px = Math.Clamp((int)Math.Round(_x), 0, Width - 1);
        int py = Math.Clamp((int)Math.Round(_y), 0, Height - 1);
        var pc = _cells[py * Width + px];
        pc.Text = "●";
        pc.Background = Accent;
        pc.Foreground = Cell;
        pc.Bold = true;
    }
}
