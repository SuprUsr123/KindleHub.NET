using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Stronghold - the official base-builder shrunk to one keep under siege. Mines
/// and barracks work on a three-second economy cycle, raid waves land every
/// twelve seconds and march a step every 600ms, all driven through Tick with
/// plain time accumulators - no threads. Build walls to funnel, towers to shoot,
/// barracks to train soldiers that meet raiders at the wall. Survive ten waves.
/// </summary>
public sealed class StrongholdGame : GameBase
{
    public const int Size = 9;
    private const int TotalWaves = 10;
    private const int CastleX = 4;          // the keep starts dead centre
    private const int CastleY = 4;
    private const int CastleHpMax = 30;
    private const int StartGold = 25;
    private const int SoldierCap = 6;
    private const int SoldierCost = 10;
    private const int RaiderHp = 3;
    private const int RaiderAtk = 3;
    private const int MenuBase = Size * Size;      // 81: build menu + readouts

    private static readonly TimeSpan EconCycle = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan WaveEvery = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan StepEvery = TimeSpan.FromMilliseconds(600);

    private enum Kind { Wall, Mine, Barracks, Tower }

    private sealed class Building
    {
        public Kind Kind;
        public int Hp;
    }

    private sealed class Raider
    {
        public int X, Y, Hp;
    }

    // cost, hp, glyph and tint per building kind
    private static readonly (int Cost, int Hp, string Glyph, IBrush Brush)[] BuildingInfo =
    {
        (5, 10, "▓", new SolidColorBrush(Color.Parse("#85858d"))),
        (15, 8, "$", new SolidColorBrush(Color.Parse("#9a6700"))),
        (25, 8, "B", new SolidColorBrush(Color.Parse("#2f6fed"))),
        (20, 8, "T", new SolidColorBrush(Color.Parse("#6b3fa0"))),
    };

    private readonly List<GameCell> _cells = new();
    private readonly Dictionary<int, Building> _buildings = new();
    private readonly List<Raider> _raiders = new();
    private int _gold;
    private int _soldiers;
    private int _castleHp;
    private int _wavesSpawned;
    private int _wavesDefeated;
    private int _sel = -1;
    private bool _over;
    private bool _fell;
    private bool _won;
    private string _msg = "";
    private TimeSpan _econ;
    private TimeSpan _waveClock;
    private TimeSpan _stepClock;

    public StrongholdGame()
    {
        for (int i = 0; i < MenuBase + Size; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "stronghold";
    public override string Name => "Stronghold";
    public override int Columns => Size;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => !_over;
    public override bool NeedsTicks => !_over;

    public override int Score => 100 * _wavesDefeated + _gold;

    public override string? ResultText => _fell
        ? $"The keep fell on wave {_wavesSpawned}."
        : _won ? "Ten waves repelled - the stronghold holds!" : null;

    public override string StatusText
    {
        get
        {
            if (_over) return _fell ? $"Keep lost · {Score} points." : $"Victory · {Score} points.";
            string extra = _msg.Length > 0 ? $" · {_msg}" : "";
            return $"Gold {_gold} · Sold {_soldiers} · Wave {_wavesSpawned}/{TotalWaves} · Keep {_castleHp}{extra}";
        }
    }

    public override void Reset()
    {
        _buildings.Clear();
        _raiders.Clear();
        _gold = StartGold;
        _soldiers = 0;
        _castleHp = CastleHpMax;
        _wavesSpawned = 0;
        _wavesDefeated = 0;
        _sel = -1;
        _over = false;
        _fell = false;
        _won = false;
        _msg = "build before wave 1";
        _econ = TimeSpan.Zero;
        _waveClock = TimeSpan.Zero;
        _stepClock = TimeSpan.Zero;
        ScoreCounts = false;
        Redraw();
    }

    /// <summary>Advances economy, raids and marching, capped at six cycles each.</summary>
    public override bool Tick(TimeSpan elapsed)
    {
        if (_over) return false;
        _econ += elapsed;
        _waveClock += elapsed;
        _stepClock += elapsed;
        bool changed = false;

        int guard = 0;
        while (_stepClock >= StepEvery && guard++ < 6)
        {
            _stepClock -= StepEvery;
            RaiderTick();
            changed = true;
            if (_over) return true;
        }

        guard = 0;
        while (_econ >= EconCycle && guard++ < 6)
        {
            _econ -= EconCycle;
            EconomyTick();
            changed = true;
        }

        guard = 0;
        while (_waveClock >= WaveEvery && guard++ < 6)
        {
            _waveClock -= WaveEvery;
            WaveTick();
            changed = true;
            if (_over) return true;
        }

        if (changed) Redraw();
        return changed;
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;

        if (index < MenuBase)
        {
            if (index == CastleY * Size + CastleX)
            {
                _msg = "The keep cannot move";
                Redraw();
                return true;
            }
            if (_buildings.TryGetValue(index, out var b))
            {
                // Selling refunds half the build cost.
                _gold += BuildingInfo[(int)b.Kind].Cost / 2;
                _buildings.Remove(index);
                _sel = -1;
                _msg = "Sold for half";
                Redraw();
                return true;
            }
            _sel = _sel == index ? -1 : index;
            _msg = _sel >= 0 ? "now tap a build" : "";
            Redraw();
            return true;
        }

        if (index >= MenuBase && index < MenuBase + 4)
        {
            Build((Kind)(index - MenuBase));
            return true;
        }

        return false;
    }

    private void Build(Kind kind)
    {
        var info = BuildingInfo[(int)kind];
        if (_sel < 0)
        {
            _msg = "tap a floor tile first";
        }
        else if (_buildings.ContainsKey(_sel))
        {
            _msg = "that tile is taken";
            _sel = -1;
        }
        else if (RaiderAt(_sel % Size, _sel / Size) != null)
        {
            _msg = "a raider is in the way";
        }
        else if (_gold < info.Cost)
        {
            _msg = "not enough gold";
        }
        else
        {
            _gold -= info.Cost;
            _buildings[_sel] = new Building { Kind = kind, Hp = info.Hp };
            _msg = $"{kind} built";
            _sel = -1;
        }
        Redraw();
    }

    private void EconomyTick()
    {
        int mines = 0, barracks = 0;
        foreach (var b in _buildings.Values)
        {
            if (b.Kind == Kind.Mine) mines++;
            else if (b.Kind == Kind.Barracks) barracks++;
        }
        _gold += 3 + 2 * mines;
        // Each barracks trains one soldier a cycle, gold permitting.
        for (int i = 0; i < barracks && _soldiers < SoldierCap; i++)
        {
            if (_gold < SoldierCost) break;
            _gold -= SoldierCost;
            _soldiers++;
        }
    }

    private void WaveTick()
    {
        if (_wavesSpawned >= TotalWaves) return;
        _wavesSpawned++;
        _msg = $"wave {_wavesSpawned} incoming!";

        // Spawn along the right edge, falling back a column when it is full.
        var spots = new List<int>();
        for (int col = Size - 1; col >= Size - 3 && spots.Count < _wavesSpawned; col--)
            for (int y = 0; y < Size; y++)
            {
                int idx = y * Size + col;
                if (idx == CastleY * Size + CastleX) continue;
                if (!_buildings.ContainsKey(idx) && RaiderAt(col, y) == null) spots.Add(idx);
            }
        Shuffle(spots);
        for (int n = 0; n < _wavesSpawned && n < spots.Count; n++)
            _raiders.Add(new Raider { X = spots[n] % Size, Y = spots[n] / Size, Hp = RaiderHp });
    }

    private void RaiderTick()
    {
        // Towers shoot first: 1 damage to the nearest raider in range 2.
        foreach (var pair in _buildings)
        {
            if (pair.Value.Kind != Kind.Tower) continue;
            int tx = pair.Key % Size, ty = pair.Key / Size;
            Raider? best = null;
            int bestD = 3;
            foreach (var r in _raiders)
            {
                int d = Math.Max(Math.Abs(r.X - tx), Math.Abs(r.Y - ty));
                if (d <= 2 && d < bestD)
                {
                    bestD = d;
                    best = r;
                }
            }
            if (best != null) best.Hp -= 1;
        }
        _raiders.RemoveAll(r => r.Hp <= 0);

        // Then raiders act: adjacent to the keep they strike, otherwise they march.
        for (int i = _raiders.Count - 1; i >= 0; i--)
        {
            var r = _raiders[i];
            if (Math.Abs(r.X - CastleX) + Math.Abs(r.Y - CastleY) == 1)
            {
                if (_soldiers > 0)
                {
                    // A soldier meets the raider at the wall and both are spent.
                    _soldiers--;
                    _raiders.RemoveAt(i);
                }
                else
                {
                    _castleHp -= RaiderAtk;
                    if (_castleHp <= 0)
                    {
                        Fall();
                        return;
                    }
                }
                continue;
            }
            March(r);
        }
        CheckWaveCleared();
    }

    private void March(Raider r)
    {
        int dx = Math.Sign(CastleX - r.X), dy = Math.Sign(CastleY - r.Y);
        bool preferX = Math.Abs(CastleX - r.X) >= Math.Abs(CastleY - r.Y);
        var steps = preferX ? new[] { (dx, 0), (0, dy) } : new[] { (0, dy), (dx, 0) };
        foreach (var (sx, sy) in steps)
        {
            if (sx == 0 && sy == 0) continue;
            int nx = r.X + sx, ny = r.Y + sy;
            if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) continue;
            int idx = ny * Size + nx;
            if (idx == CastleY * Size + CastleX) continue;   // the keep is fought, not entered
            if (_buildings.TryGetValue(idx, out var b))
            {
                b.Hp -= RaiderAtk;                           // raiders smash what blocks them
                if (b.Hp <= 0) _buildings.Remove(idx);
                return;
            }
            if (RaiderAt(nx, ny) == null)
            {
                r.X = nx;
                r.Y = ny;
                return;
            }
        }
    }

    private void CheckWaveCleared()
    {
        if (_raiders.Count > 0 || _wavesSpawned <= _wavesDefeated) return;
        _wavesDefeated = _wavesSpawned;
        if (_wavesSpawned >= TotalWaves)
        {
            _over = true;
            _won = true;
            ScoreCounts = true;
        }
    }

    private void Fall()
    {
        _over = true;
        _fell = true;
        // A run that beat nothing and banked nothing has no score to post.
        ScoreCounts = Score >= 1;
    }

    private Raider? RaiderAt(int x, int y)
    {
        foreach (var r in _raiders)
            if (r.X == x && r.Y == y) return r;
        return null;
    }

    private static void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = i < MenuBase ? Cell : Board;
            c.Foreground = Ink;
            c.IsEnabled = !_over && i < MenuBase;
            c.FontSize = 18;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
        }

        foreach (var pair in _buildings)
        {
            var info = BuildingInfo[(int)pair.Value.Kind];
            var c = _cells[pair.Key];
            c.Text = info.Glyph;
            c.Background = info.Brush;
            c.Foreground = Cell;
            c.Bold = true;
        }

        var castle = _cells[CastleY * Size + CastleX];
        castle.Text = "♥";
        castle.Background = Bad;
        castle.Foreground = Cell;
        castle.Bold = true;

        foreach (var r in _raiders)
        {
            var c = _cells[r.Y * Size + r.X];
            c.Text = "✗";
            c.Background = Bad;
            c.Foreground = Cell;
            c.Bold = true;
        }

        if (_sel >= 0 && !_over)
        {
            var c = _cells[_sel];
            c.BorderBrush = Accent;
            c.BorderThickness = new Avalonia.Thickness(2);
        }

        for (int k = 0; k < 4; k++)
        {
            var info = BuildingInfo[k];
            var c = _cells[MenuBase + k];
            c.Text = $"{info.Glyph} {info.Cost}";
            c.Background = Cell;
            c.Foreground = Ink;
            c.IsEnabled = !_over;
            c.FontSize = 14;
            c.Bold = true;
            c.BorderBrush = Accent;
            c.BorderThickness = new Avalonia.Thickness(1);
        }

        Readout(MenuBase + 4, $"Gold\n{_gold}");
        Readout(MenuBase + 5, $"Sold\n{_soldiers}");
        Readout(MenuBase + 6, $"Wave\n{_wavesSpawned}/{TotalWaves}");
        Readout(MenuBase + 7, $"Keep\n{Math.Max(0, _castleHp)}");
    }

    private void Readout(int index, string text)
    {
        var c = _cells[index];
        c.Text = text;
        c.Background = Board;
        c.Foreground = Muted;
        c.IsEnabled = false;
        c.FontSize = 12;
        c.Bold = true;
    }
}
