using System;
using System.Collections.Generic;

namespace KindleHub.Client.Games;

/// <summary>
/// Geometry Dash — an auto-runner, ported from the official client. The strip
/// scrolls left at a pace that climbs with your score; jump the spikes, hop the
/// blocks, clear the gaps and grab the floating diamonds.
/// </summary>
public sealed class GeometryDashGame : GameBase
{
    private const int Cols = 30;
    private const int Rows = 11;
    private const int PlayerCol = 5;
    private const int GroundRow = 8;       // the row the runner occupies on flat ground

    private const int FrameMs = 50;        // fixed simulation frame
    private const int MaxFrames = 6;       // catch-up guard: at most 6 frames per Tick
    private const double Dt = 0.05;        // frame length in seconds
    private const double Gravity = 37.5;   // cells/s²
    private const double JumpV = 15.0;     // cells/s up — apex about three cells
    private const double BaseSpeed = 6.0;  // cells/s
    private const double SpeedStep = 1.1;  // +10% speed per 100 points
    private const int StartFlat = 12;      // safe flat run before the first obstacle

    private sealed class Strip
    {
        public bool Ground = true;
        public int BlockH;
        public bool Spike;
        public bool Coin;
        public int CoinRow = 6;
    }

    private readonly List<GameCell> _cells = new();
    private readonly List<Strip> _world = new();
    private double _dist;
    private double _y = GroundRow;
    private double _vy;
    private bool _grounded = true;
    private int _coins;
    private int _runFlat = StartFlat;
    private int _finalScore;
    private TimeSpan _acc;
    private bool _over;

    public GeometryDashGame()
    {
        for (int i = 0; i < Cols * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "geometrydash";
    public override string Name => "Geometry Dash";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => true;

    public override int Score => Math.Clamp(_over ? _finalScore : LiveScore(), 0, 1_000_000);

    public override string? ResultText => _over ? $"Crashed — {Score} points." : null;

    public override string StatusText => _over
        ? $"Final score {Score}."
        : $"Score {LiveScore()} · tap or ↑ to jump";

    private int LiveScore() => (int)(_dist / 2) + 10 * _coins;

    public override void Reset()
    {
        _world.Clear();
        _runFlat = StartFlat;
        EnsureWorld();
        _dist = 0;
        _y = GroundRow;
        _vy = 0;
        _grounded = true;
        _coins = 0;
        _finalScore = 0;
        _acc = TimeSpan.Zero;
        _over = false;
        ScoreCounts = false;
        Redraw();
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
        // Speed ramps 10% for every 100 points on the clock, capped at 3x so the
        // runner can never skip a whole cell between frames.
        double speed = BaseSpeed * Math.Min(3.0, Math.Pow(SpeedStep, Math.Floor(LiveScore() / 100.0)));
        _dist += speed * Dt;
        EnsureWorld();

        int pc = (int)Math.Floor(_dist) + PlayerCol;
        var strip = _world[pc];
        double prevY = _y;
        _vy += Gravity * Dt;
        _y += _vy * Dt;

        // One-way landing: only falling onto a surface plants the runner.
        int surface = !strip.Ground ? int.MinValue : strip.BlockH > 0 ? 8 - strip.BlockH : GroundRow;
        if (_vy > 0 && surface > int.MinValue && prevY <= surface + 0.001 && _y >= surface)
        {
            _y = surface;
            _vy = 0;
            _grounded = true;
        }
        else
        {
            _grounded = surface > int.MinValue && _vy == 0 && Math.Abs(_y - surface) < 0.01;
        }

        if (strip.Spike && _y >= 7.4) { Die(); return; }          // spike tops out at row 8
        if (strip.BlockH > 0 && _y >= 8.5 - strip.BlockH) { Die(); return; } // ran into a block face
        if (surface == int.MinValue && _y > Rows - 0.4) { Die(); return; }   // fell into a gap

        if (strip.Coin && Math.Abs(_y - strip.CoinRow) <= 0.7)
        {
            strip.Coin = false;
            _coins++;
        }
    }

    private void Die()
    {
        if (_over) return;
        _over = true;
        _finalScore = Math.Max(1, LiveScore());
        ScoreCounts = true;
        Redraw();
    }

    private void EnsureWorld()
    {
        while (_world.Count <= (int)Math.Floor(_dist) + Cols + 10)
            GenerateOne();
    }

    private void GenerateOne()
    {
        if (_runFlat > 0)
        {
            _world.Add(new Strip());
            _runFlat--;
            return;
        }

        int roll = Random.Shared.Next(100);
        if (roll < 35)
        {
            _world.Add(new Strip { Spike = true }); // single spike, always clearable
            _runFlat = 2 + Random.Shared.Next(2);
        }
        else if (roll < 55)
        {
            _world.Add(new Strip { BlockH = 1 }); // hop on or over
            _runFlat = 2 + Random.Shared.Next(2);
        }
        else if (roll < 62)
        {
            _world.Add(new Strip { BlockH = 2 }); // tall block: needs a clean jump
            _runFlat = 3 + Random.Shared.Next(2);
        }
        else if (roll < 87)
        {
            int gap = 2 + Random.Shared.Next(2); // 2-3 wide gap
            for (int i = 0; i < gap; i++) _world.Add(new Strip { Ground = false });
            _runFlat = 2 + Random.Shared.Next(2);
        }
        else
        {
            _world.Add(new Strip { Coin = true, CoinRow = 6 + Random.Shared.Next(2) });
            _runFlat = 1 + Random.Shared.Next(2);
        }
    }

    public override bool OnKey(GameKey key)
    {
        if (_over) return false;
        if (key != GameKey.Up && key != GameKey.Confirm) return false;
        return Jump();
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;
        return Jump();
    }

    private bool Jump()
    {
        if (!_grounded) return true; // consumed but airborne
        _vy = -JumpV;
        _grounded = false;
        return true;
    }

    public override void Redraw()
    {
        int baseCol = (int)Math.Floor(_dist);
        for (int y = 0; y < Rows; y++)
        for (int x = 0; x < Cols; x++)
        {
            var c = _cells[y * Cols + x];
            c.Text = "";
            c.Foreground = Ink;
            c.IsEnabled = !_over;
            c.FontSize = 14;
            c.Bold = false;

            var strip = _world[Math.Min(baseCol + x, _world.Count - 1)];
            if (y >= 9)
            {
                c.Background = strip.Ground ? Muted : Cell; // gaps drop out of the ground band
            }
            else
            {
                c.Background = Board;
            }

            if (strip.Spike && y == GroundRow)
            {
                c.Text = "▲";
                c.Foreground = Ink;
            }
            else if (strip.BlockH > 0 && y >= 9 - strip.BlockH && y <= GroundRow)
            {
                c.Text = "█";
                c.Foreground = Cell;
                c.Background = Ink;
            }
            else if (strip.Coin && y == strip.CoinRow)
            {
                c.Text = "♦";
                c.Foreground = Warn;
                c.Bold = true;
            }
        }

        int py = Math.Clamp((int)Math.Round(_y), 0, Rows - 1);
        var player = _cells[py * Cols + PlayerCol];
        player.Text = "●";
        player.Background = Accent;
        player.Foreground = Cell;
        player.Bold = true;
    }
}
