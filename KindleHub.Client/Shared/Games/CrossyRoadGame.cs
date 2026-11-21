using System;
using System.Collections.Generic;

namespace KindleHub.Client.Games;

/// <summary>
/// Crossy Road — hop a chicken up across endless lanes of grass, traffic and
/// railway, ported from the official client. The world scrolls down one row per
/// forward hop so the chicken always sits near the bottom of the board.
/// </summary>
public sealed class CrossyRoadGame : GameBase
{
    private const int Width = 11;
    private const int Height = 13;
    private const int HomeRow = 10;       // the row the chicken is scrolled back to

    private const int FrameMs = 50;       // fixed simulation frame
    private const int MaxFrames = 6;      // catch-up guard: at most 6 frames per Tick
    private const int TrainMs = 60;       // train sweep speed: one cell per step
    private const int WarnMs = 800;       // rail warning blink before the train
    private const int BlinkMs = 200;      // warning blink toggle
    private const int PointsPerRow = 10;  // score for every row advanced

    private enum LaneKind { Grass, Road, Rail }

    private sealed class Lane
    {
        public LaneKind Kind;
        public bool[] Trees = new bool[Width];
        public int Dir;                    // road: travel direction
        public int SpeedMs;                // road: ms per cell
        public List<int> Cars = new();     // road: car column positions
        public int CarIn;                  // road: countdown to the next car step
        public int Phase;                  // rail: 0 idle, 1 warning, 2 train
        public int StateIn;                // rail: countdown for the current phase
        public int TrainX;                 // rail: front cell of the 3-cell train
        public int TrainDir;               // rail: sweep direction
    }

    private readonly List<GameCell> _cells = new();
    private readonly List<Lane> _lanes = new();
    private int _cx, _cy;
    private int _rows;
    private int _score;
    private string _cause = "";
    private TimeSpan _acc;
    private bool _over;

    public CrossyRoadGame()
    {
        for (int i = 0; i < Width * Height; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "crossyroad";
    public override string Name => "Crossy Road";
    public override int Columns => Width;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => true;

    public override int Score => Math.Clamp(_score, 0, 1_000_000);

    public override string? ResultText => _over ? $"{_cause} — {Score} points." : null;

    public override string StatusText => _over
        ? $"Final score {Score}."
        : $"Score {_score} · tap or ↑ to hop";

    public override void Reset()
    {
        _lanes.Clear();
        // Bottom lanes are safe grass; everything above is generated endlessly.
        for (int y = 0; y < Height; y++)
        {
            var lane = y >= HomeRow ? GrassLane(safe: true) : MakeLane();
            _lanes.Add(lane);
        }
        _cx = Width / 2;
        _cy = HomeRow;
        _rows = 0;
        _score = 0;
        _cause = "";
        _acc = TimeSpan.Zero;
        _over = false;
        ScoreCounts = false;
        Redraw();
    }

    private static Lane GrassLane(bool safe)
    {
        var lane = new Lane { Kind = LaneKind.Grass };
        int trees = 0;
        for (int x = 0; x < Width; x++)
        {
            if (!safe && x != Width / 2 && trees < 3 && Random.Shared.Next(100) < 12)
            {
                lane.Trees[x] = true;
                trees++;
            }
        }
        return lane;
    }

    private static Lane MakeLane()
    {
        int roll = Random.Shared.Next(100);
        if (roll < 60) // 60% road
        {
            var lane = new Lane
            {
                Kind = LaneKind.Road,
                Dir = Random.Shared.Next(2) == 0 ? 1 : -1,
                SpeedMs = 300 + Random.Shared.Next(5) * 50, // 300-500ms per cell
                CarIn = 300,
            };
            int cars = 2 + Random.Shared.Next(2);
            while (lane.Cars.Count < cars)
            {
                int x = Random.Shared.Next(Width);
                if (!lane.Cars.Contains(x)) lane.Cars.Add(x);
            }
            return lane;
        }
        if (roll < 85) return GrassLane(safe: false); // 25% grass
        return new Lane // 15% rail
        {
            Kind = LaneKind.Rail,
            Phase = 0,
            StateIn = 2000 + Random.Shared.Next(2000),
            TrainDir = Random.Shared.Next(2) == 0 ? 1 : -1,
            TrainX = -10,
        };
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
        return changed;
    }

    private void FrameStep()
    {
        foreach (var lane in _lanes)
        {
            if (lane.Kind == LaneKind.Road)
            {
                lane.CarIn -= FrameMs;
                if (lane.CarIn <= 0)
                {
                    lane.CarIn += lane.SpeedMs;
                    for (int i = 0; i < lane.Cars.Count; i++)
                        lane.Cars[i] = (lane.Cars[i] + lane.Dir + Width) % Width;
                }
            }
            else if (lane.Kind == LaneKind.Rail)
            {
                StepRail(lane);
            }
        }
        CheckDeath();
        Redraw();
    }

    private void StepRail(Lane lane)
    {
        lane.StateIn -= FrameMs;
        if (lane.StateIn > 0) return;

        switch (lane.Phase)
        {
            case 0: // idle → warning blink
                lane.Phase = 1;
                lane.StateIn = WarnMs;
                lane.TrainDir = Random.Shared.Next(2) == 0 ? 1 : -1;
                lane.TrainX = lane.TrainDir > 0 ? -3 : Width;
                break;
            case 1: // warning → train enters
                lane.Phase = 2;
                lane.StateIn = TrainMs;
                break;
            default: // train sweeps one cell per step
                lane.TrainX += lane.TrainDir;
                bool done = lane.TrainDir > 0 ? lane.TrainX > Width : lane.TrainX + 3 <= 0;
                if (done)
                {
                    lane.Phase = 0;
                    lane.StateIn = 2600 + Random.Shared.Next(1600);
                    lane.TrainX = -10;
                }
                else
                {
                    lane.StateIn += TrainMs;
                }
                break;
        }
    }

    private static bool TrainCovers(Lane lane, int x) =>
        lane.Phase == 2 && x >= lane.TrainX && x < lane.TrainX + 3;

    private void CheckDeath()
    {
        if (_over) return;
        var lane = _lanes[_cy];
        if (lane.Kind == LaneKind.Road && lane.Cars.Contains(_cx)) Die("Squashed");
        else if (lane.Kind == LaneKind.Rail && TrainCovers(lane, _cx)) Die("Hit by the train");
    }

    private void Die(string cause)
    {
        if (_over) return;
        _over = true;
        _cause = cause;
        _score = Math.Max(1, _rows * PointsPerRow);
        ScoreCounts = true;
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (_over) return false;
        switch (key)
        {
            case GameKey.Up:
            case GameKey.Confirm: return Hop(0, -1);
            case GameKey.Down: return Hop(0, 1);
            case GameKey.Left: return Hop(-1, 0);
            case GameKey.Right: return Hop(1, 0);
            default: return false;
        }
    }

    public override bool OnTap(int index)
    {
        // Tapping steers the hop toward the tapped cell — the middle of the board
        // is above the chicken, so that hops forward.
        if (_over || index < 0 || index >= _cells.Count) return false;
        int tx = index % Width, ty = index / Width;
        int vx = tx - _cx, vy = ty - _cy;
        if (Math.Abs(vx) > Math.Abs(vy)) return Hop(vx > 0 ? 1 : -1, 0);
        return Hop(0, vy > 0 ? 1 : -1); // a tap on the chicken itself hops forward
    }

    private bool Hop(int dx, int dy)
    {
        if (_over) return false;
        int nx = _cx + dx, ny = _cy + dy;
        if (nx < 0 || nx >= Width || ny < HomeRow || ny >= Height) return true; // edge: no move

        if (dy < 0)
        {
            if (_cy > HomeRow)
            {
                if (_lanes[ny].Trees[_cx]) return true; // tree blocks the hop
                _cy = ny;
            }
            else
            {
                if (_lanes[HomeRow - 1].Trees[_cx]) return true;
                // Forward hop: scroll the world down one row instead of moving.
                _lanes.Insert(0, MakeLane());
                _lanes.RemoveAt(Height);
                _rows++;
                _score = _rows * PointsPerRow;
            }
        }
        else if (dy > 0)
        {
            if (_lanes[ny].Trees[_cx]) return true;
            _cy = ny;
        }
        else
        {
            if (_lanes[_cy].Trees[nx]) return true;
            _cx = nx;
        }

        CheckDeath();
        Redraw();
        return true;
    }

    public override void Redraw()
    {
        for (int y = 0; y < Height; y++)
        {
            var lane = _lanes[y];
            // The warning blink derives from the phase countdown, no extra timer.
            bool blink = lane.Phase == 1 && (WarnMs - lane.StateIn) / BlinkMs % 2 == 0;
            for (int x = 0; x < Width; x++)
            {
                var c = _cells[y * Width + x];
                c.Text = "";
                c.Foreground = Ink;
                c.FontSize = 15;
                c.Bold = false;
                c.IsEnabled = !_over;

                switch (lane.Kind)
                {
                    case LaneKind.Grass:
                        c.Background = Cell;
                        if (lane.Trees[x]) { c.Text = "█"; c.Foreground = Ink; }
                        break;
                    case LaneKind.Road:
                        c.Background = Muted;
                        if (lane.Cars.Contains(x)) { c.Text = "■"; c.Foreground = Ink; }
                        break;
                    default:
                        c.Background = blink ? Warn : Board;
                        c.Text = "▓";
                        c.Foreground = Muted;
                        if (TrainCovers(lane, x)) { c.Text = "═"; c.Background = Ink; c.Foreground = Cell; }
                        break;
                }
            }
        }

        var chick = _cells[_cy * Width + _cx];
        chick.Text = _over ? "✗" : "●";
        chick.Background = _over ? Bad : Accent;
        chick.Foreground = Cell;
        chick.Bold = true;
    }
}
