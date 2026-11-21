using System;
using System.Collections.Generic;

namespace KindleHub.Client.Games;

/// <summary>
/// Flight Simulator — an instrument-panel flight, ported from the official client.
/// Pick one of three aircraft, then juggle pitch and throttle to fly through each
/// gate's altitude band without stalling, burying or overspeeding the plane.
/// </summary>
public sealed class FlightSimGame : GameBase
{
    private const int Width = 10;
    private const int Height = 10;

    private const int FrameMs = 50;      // fixed simulation frame
    private const int MaxFrames = 6;     // catch-up guard: at most 6 frames per Tick
    private const double Dt = 0.05;      // frame length in seconds
    private const double GravityPull = 0.5;  // speed bleed per pitch unit, per frame
    private const int GateSpacing = 800;     // distance between gates
    private const int GateBand = 300;        // hit window around a gate's altitude
    private const int MinSpeed = 60;         // stall line when climbing
    private const int MaxSpeed = 200;        // overspeed line
    private const int OverspeedTickMs = 500; // one damage point per this long over max
    private const int MaxDamage = 3;
    private const int Ceiling = 6000;
    private const int GatePoints = 50;

    private enum Phase { Pick, Fly }

    private static readonly (char Key, string Name, int MaxSpeed, int Climb, int Gates)[] Planes =
    {
        ('C', "CESSNA",   140, 250, 10), // easy and forgiving
        ('A', "ARROW",    170, 350, 10), // faster
        ('K', "KING AIR", 200, 500, 10), // twin turboprop, fastest climb
    };

    private readonly GameCell[] _cells = new GameCell[Width * Height];
    private Phase _phase;
    private int _selIdx;
    private int _plane;
    private int[] _gates = Array.Empty<int>();
    private bool[] _gateHit = Array.Empty<bool>();
    private int _gateIdx;
    private int _gatesHit;
    private double _speed, _alt, _dist;
    private int _pitch;
    private int _throttle;
    private int _damage;
    private int _overIn;
    private int _score;
    private string _cause = "";
    private TimeSpan _acc;
    private bool _over;

    public FlightSimGame()
    {
        for (int i = 0; i < _cells.Length; i++) _cells[i] = Blank();
        Reset();
    }

    public override string Slug => "flightsim";
    public override string Name => "Flight Simulator";
    public override int Columns => Width;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => true;

    public override int Score => Math.Clamp(_score, 0, 1_000_000);

    public override string? ResultText => _over ? $"{_cause} — {Score} points." : null;

    public override string StatusText
    {
        get
        {
            if (_over) return $"Final score {Score}.";
            if (_phase == Phase.Pick) return "Pick an aircraft · C, A or K";
            int gate = Math.Min(_gateIdx + 1, _gates.Length);
            return $"SPD {(int)_speed} · ALT {(int)_alt} · gate {gate}/{_gates.Length}";
        }
    }

    public override void Reset()
    {
        _phase = Phase.Pick;
        _selIdx = 0;
        _plane = 0;
        _gates = Array.Empty<int>();
        _gateHit = Array.Empty<bool>();
        _gateIdx = 0;
        _gatesHit = 0;
        _speed = 0;
        _alt = 1500;
        _dist = 0;
        _pitch = 0;
        _throttle = 3;
        _damage = 0;
        _overIn = 0;
        _score = 0;
        _cause = "";
        _acc = TimeSpan.Zero;
        _over = false;
        ScoreCounts = false;
        Redraw();
    }

    private void StartFlight(int plane)
    {
        _plane = plane;
        var p = Planes[plane];
        _gates = new int[p.Gates];
        _gateHit = new bool[p.Gates];
        for (int i = 0; i < p.Gates; i++) _gates[i] = 800 + Random.Shared.Next(13) * 250;
        _gateIdx = 0;
        _gatesHit = 0;
        _speed = p.MaxSpeed * 0.6;
        _alt = 1500;
        _dist = 0;
        _pitch = 0;
        _throttle = 3;
        _damage = 0;
        _overIn = 0;
        _acc = TimeSpan.Zero;
        _phase = Phase.Fly;
        Redraw();
    }

    public override bool Tick(TimeSpan elapsed)
    {
        if (_over || _phase != Phase.Fly) return false;
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
        var p = Planes[_plane];
        double target = 40 + _throttle * (p.MaxSpeed - 40) / 5.0;
        _speed += (target - _speed) * 0.15 - _pitch * GravityPull;
        if (_speed < 0) _speed = 0;
        _alt += _pitch * p.Climb * Dt;
        if (_alt > Ceiling) _alt = Ceiling;
        _dist += _speed * Dt;

        if (_speed < MinSpeed && _pitch > 0) { End("Stalled"); return; }
        if (_alt <= 0) { End("Flew into the ground"); return; }
        if (_speed > MaxSpeed)
        {
            _overIn += FrameMs;
            if (_overIn >= OverspeedTickMs)
            {
                _overIn = 0;
                _damage++;
                if (_damage >= MaxDamage) { End("Broke apart"); return; }
            }
        }
        else
        {
            _overIn = 0;
        }

        if (_gateIdx < _gates.Length && _dist >= (_gateIdx + 1) * GateSpacing)
        {
            _gateHit[_gateIdx] = Math.Abs(_alt - _gates[_gateIdx]) <= GateBand;
            if (_gateHit[_gateIdx]) _gatesHit++;
            _gateIdx++;
            if (_gateIdx >= _gates.Length) { End("Flight complete"); return; }
        }
    }

    private void End(string cause)
    {
        if (_over) return;
        _over = true;
        _cause = cause;
        _score = GatePoints * _gatesHit;
        ScoreCounts = _score > 0;
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (_over) return false;
        if (_phase == Phase.Pick)
        {
            switch (key)
            {
                case GameKey.Left:
                    _selIdx = (_selIdx + Planes.Length - 1) % Planes.Length;
                    Redraw();
                    return true;
                case GameKey.Right:
                    _selIdx = (_selIdx + 1) % Planes.Length;
                    Redraw();
                    return true;
                case GameKey.Confirm:
                    StartFlight(_selIdx);
                    return true;
                default:
                    return false;
            }
        }

        switch (key)
        {
            case GameKey.Up:
                _pitch = Math.Min(3, _pitch + 1);
                break;
            case GameKey.Down:
                _pitch = Math.Max(-3, _pitch - 1);
                break;
            case GameKey.Left:
                _throttle = Math.Max(1, _throttle - 1);
                break;
            case GameKey.Right:
                _throttle = Math.Min(5, _throttle + 1);
                break;
            case GameKey.Confirm:
                _pitch = 0; // level off
                break;
            default:
                return false;
        }
        Redraw();
        return true;
    }

    public override bool OnLetter(char letter)
    {
        if (_over || _phase != Phase.Pick) return false;
        int idx = letter switch
        {
            'c' or 'C' => 0,
            'a' or 'A' => 1,
            'k' or 'K' => 2,
            _ => -1,
        };
        if (idx < 0) return false;
        StartFlight(idx);
        return true;
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Length) return false;
        int x = index % Width, y = index / Width;

        if (_phase == Phase.Pick)
        {
            int plane = x switch { 2 => 0, 4 => 1, 6 => 2, _ => -1 };
            if (y != 3 || plane < 0) return false;
            StartFlight(plane);
            return true;
        }

        if (y == 6 && x >= 1 && x <= 5)
        {
            _throttle = x; // direct throttle taps
        }
        else if (y < 5)
        {
            _pitch = Math.Min(3, _pitch + 1);
        }
        else if (y == 5)
        {
            _pitch = 0;
        }
        else
        {
            _pitch = Math.Max(-3, _pitch - 1);
        }
        Redraw();
        return true;
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Length; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = !_over;
            c.FontSize = 12;
            c.Bold = false;
        }

        if (_phase == Phase.Pick)
        {
            DrawPick();
            return;
        }
        DrawFlight();
    }

    private void DrawPick()
    {
        Label(1, 0, "PICK");
        Label(2, 0, "YOUR");
        Label(3, 0, "PLANE");
        for (int i = 0; i < Planes.Length; i++)
        {
            int col = i * 2 + 2;
            var key = _cells[3 * Width + col];
            key.Text = Planes[i].Key.ToString();
            key.FontSize = 24;
            key.Bold = true;
            key.Background = i == _selIdx ? Accent : Board;
            key.Foreground = i == _selIdx ? Cell : Ink;

            Label(col - 1, 4, Planes[i].Name);
            Label(col - 1, 5, i == 0 ? "EASY" : i == 1 ? "FAST" : "TWIN");
            Label(col - 1, 6, $"{Planes[i].MaxSpeed}KT");
            Label(col - 1, 7, $"{Planes[i].Gates} GATES");
        }
        Label(2, 9, "←→ + OK");
        Label(6, 9, "or tap");
    }

    private void DrawFlight()
    {
        var p = Planes[_plane];

        Label(0, 0, "SPD");
        Value(1, 0, ((int)_speed).ToString());
        Label(3, 0, "ALT");
        Value(4, 0, ((int)_alt).ToString());
        Label(6, 0, "DMG");
        Value(7, 0, $"{_damage}/3");
        _cells[7].Foreground = _damage > 0 ? Bad : Ink;

        // Attitude indicator: the horizon bar shifts with pitch.
        int barRow = Math.Clamp(3 - _pitch, 1, 5);
        for (int y = 1; y <= 5; y++)
        for (int x = 0; x < Width; x++)
        {
            var c = _cells[y * Width + x];
            c.Background = y < barRow ? Cell : y > barRow ? Muted : Ink;
            if (y == barRow)
            {
                c.Text = x == 5 ? "→" : "▓";
                c.Foreground = Cell;
            }
        }

        Label(0, 6, "THR");
        for (int i = 1; i <= 5; i++)
        {
            var c = _cells[6 * Width + i];
            if (i <= _throttle)
            {
                c.Text = "■";
                c.Background = Good;
                c.Foreground = Cell;
            }
            else
            {
                c.Background = Board;
            }
        }
        Label(7, 6, "PIT");
        Value(8, 6, _pitch == 0 ? "0" : _pitch > 0 ? $"+{_pitch}" : _pitch.ToString());

        // Gate strip: hundreds of feet, green when made, red cross when missed.
        for (int i = 0; i < _gates.Length; i++)
        {
            int col = i < 10 ? i : i - 10;
            int row = i < 10 ? 7 : 8;
            var c = _cells[row * Width + col];
            if (i < _gateIdx)
            {
                c.Text = _gateHit[i] ? "✓" : "✗";
                c.Background = _gateHit[i] ? Good : Bad;
                c.Foreground = Cell;
            }
            else
            {
                c.Text = (_gates[i] / 100).ToString();
                c.Background = i == _gateIdx ? Warn : Board;
                c.Foreground = Ink;
                c.Bold = i == _gateIdx;
            }
            c.FontSize = 10;
        }

        // Progress to the next gate.
        double seg = _dist / GateSpacing;
        double frac = Math.Clamp(seg - _gateIdx, 0, 1);
        int filled = (int)(frac * 6);
        for (int i = 0; i < 6; i++)
        {
            var c = _cells[8 * Width + 4 + i];
            c.Text = i < filled ? "▓" : "";
            c.Foreground = Ink;
            c.FontSize = 10;
        }

        Label(0, 9, p.Name);
        int gate = Math.Min(_gateIdx + 1, _gates.Length);
        Label(8, 9, $"G{gate}");
    }

    private void Label(int x, int y, string text)
    {
        var c = _cells[y * Width + x];
        c.Text = text;
        c.Foreground = Muted;
        c.FontSize = 9;
    }

    private void Value(int x, int y, string text)
    {
        var c = _cells[y * Width + x];
        c.Text = text;
        c.Foreground = Ink;
        c.FontSize = 12;
        c.Bold = true;
    }
}
