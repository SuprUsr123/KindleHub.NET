using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Galaga — move, let the auto-cannon fire and blast the alien formation, ported
/// from the official client. Watch for divers that peel off and swoop at you in
/// a sine path: they are worth triple but they collide.
/// </summary>
public sealed class GalagaGame : GameBase
{
    private const int Width = 11;
    private const int Height = 13;
    private const int FormCols = 6;
    private const int FormRows = 4;
    private const int BaseX = 2;          // formation slot origin
    private const int BaseY = 1;
    private const int TotalAliens = FormCols * FormRows;
    private const int Lives = 3;
    private const int PlayerRow = Height - 1;

    private const int FrameMs = 50;       // fixed simulation frame
    private const int MaxFrames = 6;      // catch-up guard: at most 6 frames per Tick
    private const int FireMs = 400;       // auto-fire cadence
    private const int BoltMs = 80;        // bolt speed: one cell per step
    private const int DiveMs = 120;       // diver speed: one cell per step
    private const int MaxBolts = 3;
    private const int FormationPoints = 10;
    private const int DiverPoints = 30;
    private const int WaveBonus = 100;      // cleared formation bonus

    private sealed class Alien
    {
        public int SlotX, SlotY;
        public bool Alive = true;
        public bool Diving;
        public int X, Y;                  // live position while diving
        public int BaseCol;               // column the dive started from
        public int Home;                  // homing drift accumulated while diving
        public double Phase;              // sine phase while diving
        public int Steps;
    }

    private readonly List<GameCell> _cells = new();
    private readonly List<Alien> _aliens = new();
    private readonly List<(int X, int Y)> _bolts = new();
    private int _px;
    private int _formX, _formY, _formDir;
    private int _formIn, _fireIn, _boltIn, _diveIn, _diveMoveIn;
    private int _wave;
    private int _lives;
    private int _score;
    private string _cause = "";
    private TimeSpan _acc;
    private bool _over;

    public GalagaGame()
    {
        for (int i = 0; i < Width * Height; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "galaga";
    public override string Name => "Galaga";
    public override int Columns => Width;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => true;

    public override int Score => Math.Clamp(_score, 0, 1_000_000);

    public override string? ResultText => _over ? $"{_cause} — {Score} points." : null;

    public override string StatusText => _over
        ? $"Final score {Score}."
        : $"Score {_score} · wave {_wave} · lives {_lives}";

    public override void Reset()
    {
        _bolts.Clear();
        _px = Width / 2;
        _wave = 1;
        _lives = Lives;
        _score = 0;
        _cause = "";
        _acc = TimeSpan.Zero;
        _over = false;
        ScoreCounts = false;
        BuildWave();
        Redraw();
    }

    private void BuildWave()
    {
        _aliens.Clear();
        for (int sy = 0; sy < FormRows; sy++)
        for (int sx = 0; sx < FormCols; sx++)
            _aliens.Add(new Alien { SlotX = sx, SlotY = sy });
        _formX = 0;
        _formY = Math.Min(1 + (_wave - 1) / 2, 3); // later waves start lower
        _formDir = 1;
        _formIn = FormStepMs();
        _fireIn = FireMs;
        _boltIn = BoltMs;
        _diveIn = DiveGapMs();
        _diveMoveIn = DiveMs;
    }

    private int FormStepMs()
    {
        // The formation quickens as its ranks thin and as waves advance.
        int alive = _aliens.Count(a => a.Alive);
        return Math.Clamp(900 - (_wave - 1) * 40 - (TotalAliens - alive) * 20, 220, 900);
    }

    private int DiveGapMs() => Math.Max(1500, 2500 - (_wave - 1) * 100 + Random.Shared.Next(1500)); // a dive every 2.5-4s

    private static int FormXOf(Alien a, int formX) => BaseX + a.SlotX + formX;
    private static int FormYOf(Alien a, int formY) => BaseY + a.SlotY + formY;

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
        _formIn -= FrameMs;
        if (_formIn <= 0)
        {
            _formIn += FormStepMs();
            StepFormation();
            if (_over) return;
        }

        _diveIn -= FrameMs;
        if (_diveIn <= 0)
        {
            _diveIn += DiveGapMs();
            LaunchDiver();
        }

        _diveMoveIn -= FrameMs;
        if (_diveMoveIn <= 0)
        {
            _diveMoveIn += DiveMs;
            StepDivers();
            if (_over) return;
        }

        _fireIn -= FrameMs;
        if (_fireIn <= 0)
        {
            _fireIn += FireMs;
            Fire();
        }

        _boltIn -= FrameMs;
        if (_boltIn <= 0)
        {
            _boltIn += BoltMs;
            MoveBolts();
        }
    }

    private void StepFormation()
    {
        var seated = _aliens.Where(a => a.Alive && !a.Diving).ToList();
        if (seated.Count == 0) return;

        int minX = seated.Min(a => FormXOf(a, _formX));
        int maxX = seated.Max(a => FormXOf(a, _formX));
        if (_formDir > 0 && maxX + 1 > Width - 1)
        {
            _formDir = -1;
            _formY++;
        }
        else if (_formDir < 0 && minX - 1 < 0)
        {
            _formDir = 1;
            _formY++;
        }
        else
        {
            _formX += _formDir;
        }

        if (seated.Any(a => FormYOf(a, _formY) >= Height - 2))
            GameOver("The formation landed");
    }

    private void LaunchDiver()
    {
        var candidates = _aliens.Where(a => a.Alive && !a.Diving).ToList();
        if (candidates.Count == 0) return;
        var a = candidates[Random.Shared.Next(candidates.Count)];
        a.Diving = true;
        a.X = FormXOf(a, _formX);
        a.Y = FormYOf(a, _formY);
        a.BaseCol = a.X;
        a.Home = 0;
        a.Phase = Random.Shared.Next(7);
        a.Steps = 0;
    }

    private void StepDivers()
    {
        foreach (var a in _aliens)
        {
            if (!a.Alive || !a.Diving) continue;
            a.Phase += 0.7;
            a.Steps++;
            if (a.Steps % 3 == 0) a.Home += Math.Sign(_px - a.X); // gentle homing
            a.Y++;
            a.X = Math.Clamp((int)Math.Round(a.BaseCol + Math.Sin(a.Phase) * 3) + a.Home, 0, Width - 1);

            if (a.Y >= PlayerRow)
            {
                a.Diving = false; // off the bottom: back to the formation
                if (a.X == _px) HitPlayer();
                if (_over) return;
            }
        }
    }

    private void HitPlayer()
    {
        _lives--;
        if (_lives <= 0) GameOver("Game over");
    }

    private void Fire()
    {
        if (_bolts.Count >= MaxBolts) return;
        int y = PlayerRow - 1;
        if (HitAlien(_px, y)) return; // alien already sitting on the muzzle
        _bolts.Add((_px, y));
    }

    private bool HitAlien(int bx, int by)
    {
        var hit = _aliens.FirstOrDefault(a =>
            a.Alive && by == (a.Diving ? a.Y : FormYOf(a, _formY)) && bx == (a.Diving ? a.X : FormXOf(a, _formX)));
        if (hit == null) return false;
        hit.Alive = false;
        _score += hit.Diving ? DiverPoints : FormationPoints;
        if (_aliens.All(a => !a.Alive))
        {
            _score += WaveBonus;
            _wave++;
            BuildWave();
        }
        return true;
    }

    private void MoveBolts()
    {
        for (int i = _bolts.Count - 1; i >= 0; i--)
        {
            var (bx, by) = _bolts[i];
            by--;
            if (by < 0)
            {
                _bolts.RemoveAt(i);
                continue;
            }

            if (HitAlien(bx, by))
            {
                _bolts.RemoveAt(i);
                continue;
            }

            _bolts[i] = (bx, by);
        }
    }

    private void GameOver(string cause)
    {
        if (_over) return;
        _over = true;
        _cause = cause;
        _bolts.Clear();
        ScoreCounts = _score > 0;
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (_over) return false;
        switch (key)
        {
            case GameKey.Left:
                _px = Math.Max(0, _px - 1);
                Redraw();
                return true;
            case GameKey.Right:
                _px = Math.Min(Width - 1, _px + 1);
                Redraw();
                return true;
            case GameKey.Confirm:
                Fire();
                Redraw();
                return true;
            default:
                return false;
        }
    }

    public override bool OnTap(int index)
    {
        // Tapping steers the ship toward the tapped column; tapping your own
        // column fires at once.
        if (_over || index < 0 || index >= _cells.Count) return false;
        int tx = index % Width;
        if (tx < _px) return OnKey(GameKey.Left);
        if (tx > _px) return OnKey(GameKey.Right);
        return OnKey(GameKey.Confirm);
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = !_over;
            c.FontSize = 15;
            c.Bold = false;
        }

        foreach (var (bx, by) in _bolts)
        {
            var c = _cells[by * Width + bx];
            c.Text = "↑";
            c.Foreground = Good;
            c.Bold = true;
        }

        foreach (var a in _aliens)
        {
            if (!a.Alive) continue;
            int x = a.Diving ? a.X : FormXOf(a, _formX);
            int y = a.Diving ? a.Y : FormYOf(a, _formY);
            if (y < 0 || y >= Height) continue;
            var c = _cells[y * Width + x];
            c.Text = "▲";
            if (a.Diving)
            {
                c.Background = Bad;
                c.Foreground = Cell;
                c.Bold = true;
            }
            else
            {
                c.Foreground = Ink;
                c.Bold = a.SlotY == 0; // front rank reads heavier
            }
        }

        var pc = _cells[PlayerRow * Width + _px];
        pc.Text = "●";
        pc.Background = Accent;
        pc.Foreground = Cell;
        pc.Bold = true;
    }
}
