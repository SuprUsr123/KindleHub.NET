using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Tower Defence — place and upgrade towers along the winding path to stop ten
/// waves of creeps reaching the castle, ported from the official client. Waves
/// start on a three-second countdown; leaked creeps cost the castle a point each.
/// </summary>
public sealed class TowerDefGame : GameBase
{
    private const int MapW = 13;
    private const int MapH = 9;
    private const int UiRows = 2;      // build-menu row + status row under the map
    private const int Rows = MapH + UiRows;

    private const int FrameMs = 50;    // fixed simulation frame
    private const int MaxFrames = 6;   // catch-up guard: at most 6 frames per Tick
    private const int CreepMs = 600;   // creep cadence along the path
    private const int TankMs = 900;    // tanks are slower
    private const int SpawnGapMs = 800;
    private const int WaveGapMs = 3000;
    private const int WaveCount = 10;
    private const int CastleHpMax = 20;
    private const int StartGold = 40;
    private const int KillGold = 5;
    private const int WaveGold = 30;
    private const int CreepHp = 3;
    private const int TankHp = 6;
    private const int SlowFor = 1500;  // frost slow duration
    private const int FlashFor = 100;  // muzzle-flash tint

    // '#' path, '.' buildable, 'C' castle, 'S' spawn.
    private static readonly string[] Map =
    {
        ".............",
        "..........##C",
        "..........#..",
        "..#########..",
        "..#..........",
        "..#########..",
        "..........#..",
        "S##########..",
        ".............",
    };

    private static readonly int[] TowerCost = { 15, 30, 20 };
    private static readonly int[] TowerRange = { 3, 2, 2 };
    private static readonly double[] TowerDmg = { 1, 2, 1 };
    private static readonly int[] TowerFireMs = { 1000, 1000, 1400 };
    private static readonly string[] TowerGlyph = { "A", "C", "F" };

    private sealed class Creep
    {
        public int PathIdx;
        public double Hp, MaxHp;
        public bool Tank;
        public int MoveIn, SlowIn;
    }

    private sealed class Tower
    {
        public int X, Y, Kind, Level = 1, FireIn, FlashIn, Invested;
    }

    private readonly List<GameCell> _cells = new();
    private readonly List<Creep> _creeps = new();
    private readonly List<Tower> _towers = new();
    private readonly List<(int X, int Y)> _path = new();
    private int _gold, _castle;
    private int _wave;              // current (or last) wave number
    private int _wavesSurvived;
    private int _waveSize, _spawned, _spawnIn, _waveIn;
    private bool _inCountdown = true;
    private int _menuX = -1, _menuY = -1;
    private bool _menuTower;
    private int _score;
    private TimeSpan _acc;
    private bool _over;
    private bool _won;

    public TowerDefGame()
    {
        for (int i = 0; i < MapW * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "towerdef";
    public override string Name => "Tower Defence";
    public override int Columns => MapW;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => true;

    public override int Score => Math.Clamp(_score, 0, 1_000_000);

    public override string? ResultText => !_over
        ? null
        : _won ? $"All waves cleared — {Score} points." : $"Castle fell — {Score} points.";

    public override string StatusText
    {
        get
        {
            if (_over) return $"Final score {Score}.";
            if (_inCountdown) return $"Next wave in {(_waveIn + 999) / 1000}s · gold {_gold}";
            return $"Wave {_wave}/{WaveCount} · gold {_gold} · castle {_castle}";
        }
    }

    public override void Reset()
    {
        _creeps.Clear();
        _towers.Clear();
        _path.Clear();
        BuildPath();
        _gold = StartGold;
        _castle = CastleHpMax;
        _wave = 0;
        _wavesSurvived = 0;
        _waveSize = 0;
        _spawned = 0;
        _spawnIn = 0;
        _waveIn = WaveGapMs;
        _inCountdown = true;
        _menuX = -1;
        _menuY = -1;
        _menuTower = false;
        _score = 0;
        _acc = TimeSpan.Zero;
        _over = false;
        _won = false;
        ScoreCounts = false;
        Redraw();
    }

    private void BuildPath()
    {
        // Walk the winding path from spawn to castle; the map has no branches.
        var seen = new HashSet<(int X, int Y)>();
        (int X, int Y) cur = (0, 0);
        for (int y = 0; y < MapH; y++)
        for (int x = 0; x < MapW; x++)
            if (Map[y][x] == 'S') cur = (x, y);

        while (true)
        {
            _path.Add(cur);
            seen.Add(cur);
            if (Map[cur.Y][cur.X] == 'C') break;
            var next = (X: -1, Y: -1);
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = cur.X + dx, ny = cur.Y + dy;
                if (nx < 0 || ny < 0 || nx >= MapW || ny >= MapH) continue;
                char ch = Map[ny][nx];
                if ((ch == '#' || ch == 'C') && !seen.Contains((nx, ny))) { next = (nx, ny); break; }
            }
            if (next.X < 0) break;
            cur = next;
        }
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
        if (_inCountdown)
        {
            _waveIn -= FrameMs;
            if (_waveIn <= 0) StartWave();
            return;
        }

        if (_spawned < _waveSize)
        {
            _spawnIn -= FrameMs;
            if (_spawnIn <= 0)
            {
                _spawnIn += SpawnGapMs;
                SpawnCreep();
            }
        }

        for (int i = _creeps.Count - 1; i >= 0; i--)
        {
            var creep = _creeps[i];
            creep.SlowIn = Math.Max(0, creep.SlowIn - FrameMs);
            creep.MoveIn -= FrameMs;
            if (creep.MoveIn > 0) continue;
            creep.MoveIn += (creep.Tank ? TankMs : CreepMs) * (creep.SlowIn > 0 ? 2 : 1); // frost: 50% slower
            creep.PathIdx++;
            if (creep.PathIdx >= _path.Count - 1)
            {
                _creeps.RemoveAt(i); // reached the castle
                _castle--;
                if (_castle <= 0)
                {
                    GameOver(won: false);
                    return;
                }
            }
        }

        foreach (var tower in _towers)
        {
            tower.FlashIn = Math.Max(0, tower.FlashIn - FrameMs);
            tower.FireIn -= FrameMs;
            if (tower.FireIn > 0) continue;
            var target = FirstInRange(tower);
            if (target == null)
            {
                tower.FireIn = 0; // stays hot until something walks in
                continue;
            }
            Fire(tower, target);
            tower.FireIn += TowerFireMs[tower.Kind];
        }

        if (_spawned >= _waveSize && _creeps.Count == 0)
        {
            _wavesSurvived++;
            _gold += WaveGold;
            if (_wavesSurvived >= WaveCount)
            {
                GameOver(won: true);
                return;
            }
            _inCountdown = true;
            _waveIn = WaveGapMs;
        }
    }

    private void StartWave()
    {
        _wave++;
        _waveSize = 4 + _wave;
        _spawned = 0;
        _spawnIn = 0;
        _inCountdown = false;
    }

    private void SpawnCreep()
    {
        // Every fourth wave brings tanks among the ranks.
        bool tank = (_wave == 4 || _wave == 8) && _spawned % 3 == 2;
        _creeps.Add(new Creep
        {
            PathIdx = 0,
            Hp = tank ? TankHp : CreepHp,
            MaxHp = tank ? TankHp : CreepHp,
            Tank = tank,
            MoveIn = tank ? TankMs : CreepMs,
        });
        _spawned++;
    }

    private Creep? FirstInRange(Tower tower)
    {
        Creep? best = null;
        int range = TowerRange[tower.Kind];
        foreach (var creep in _creeps)
        {
            var (cx, cy) = _path[creep.PathIdx];
            if (Math.Max(Math.Abs(cx - tower.X), Math.Abs(cy - tower.Y)) > range) continue;
            if (best == null || creep.PathIdx > best.PathIdx) best = creep; // first = furthest along
        }
        return best;
    }

    private void Fire(Tower tower, Creep target)
    {
        double dmg = TowerDmg[tower.Kind] * (tower.Level == 2 ? 1.5 : 1);
        target.Hp -= dmg;
        if (tower.Kind == 2) target.SlowIn = SlowFor; // frost chills its target
        if (tower.Kind == 1) // cannon splash covers one cell around the target
        {
            var (tx, ty) = _path[target.PathIdx];
            foreach (var creep in _creeps)
            {
                if (ReferenceEquals(creep, target)) continue;
                var (cx, cy) = _path[creep.PathIdx];
                if (Math.Max(Math.Abs(cx - tx), Math.Abs(cy - ty)) <= 1) creep.Hp -= dmg;
            }
        }
        tower.FlashIn = FlashFor;

        int before = _creeps.Count;
        _creeps.RemoveAll(c => c.Hp <= 0);
        _gold += KillGold * (before - _creeps.Count);
    }

    private void GameOver(bool won)
    {
        if (_over) return;
        _over = true;
        _won = won;
        _score = Math.Max(1, 100 * _wavesSurvived + _gold);
        ScoreCounts = true;
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (_over) return false;
        if (key != GameKey.Confirm) return false;
        if (!_inCountdown) return false;
        StartWave(); // impatient players can call the wave early
        Redraw();
        return true;
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;
        int x = index % MapW, y = index / MapW;

        if (y < MapH)
        {
            char cell = Map[y][x];
            if (cell != '.') return false; // path, spawn and castle aren't build sites
            var tower = _towers.FirstOrDefault(t => t.X == x && t.Y == y);
            if (tower != null)
            {
                _menuX = x;
                _menuY = y;
                _menuTower = true;
            }
            else
            {
                _menuX = x;
                _menuY = y;
                _menuTower = false;
            }
            Redraw();
            return true;
        }

        if (y != MapH || _menuX < 0) return false; // menu row only while a menu is open

        if (_menuTower)
        {
            var tower = _towers.FirstOrDefault(t => t.X == _menuX && t.Y == _menuY);
            if (tower == null) { CloseMenu(); Redraw(); return false; }
            if (x == 2) Upgrade(tower);
            else if (x == 5) { Sell(tower); CloseMenu(); }
            else if (x == 8) CloseMenu();
            else return false;
        }
        else
        {
            if (x == 2) Build(0);
            else if (x == 5) Build(1);
            else if (x == 8) Build(2);
            else return false;
        }
        Redraw();
        return true;
    }

    /// <summary>Right-click on a tower sells it for half of what was invested.</summary>
    public override bool OnSecondaryTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;
        int x = index % MapW, y = index / MapW;
        if (y >= MapH) return false;
        var tower = _towers.FirstOrDefault(t => t.X == x && t.Y == y);
        if (tower == null) return false;
        Sell(tower);
        CloseMenu();
        Redraw();
        return true;
    }

    private void CloseMenu()
    {
        _menuX = -1;
        _menuY = -1;
        _menuTower = false;
    }

    private void Build(int kind)
    {
        if (_gold < TowerCost[kind]) return;
        if (_towers.Any(t => t.X == _menuX && t.Y == _menuY)) return;
        _gold -= TowerCost[kind];
        _towers.Add(new Tower { X = _menuX, Y = _menuY, Kind = kind, Invested = TowerCost[kind] });
        CloseMenu();
    }

    private void Upgrade(Tower tower)
    {
        if (tower.Level >= 2) return;
        int cost = (int)(TowerCost[tower.Kind] * 0.8); // upgrades cost 80% of the base
        if (_gold < cost) return;
        _gold -= cost;
        tower.Level = 2;
        tower.Invested += cost;
        CloseMenu();
    }

    private void Sell(Tower tower)
    {
        _gold += tower.Invested / 2; // half of everything spent on it
        _towers.Remove(tower);
    }

    public override void Redraw()
    {
        for (int y = 0; y < Rows; y++)
        for (int x = 0; x < MapW; x++)
        {
            var c = _cells[y * MapW + x];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = !_over;
            c.FontSize = 13;
            c.Bold = false;

            if (y >= MapH) continue;

            char cell = Map[y][x];
            switch (cell)
            {
                case '#':
                    c.Background = Muted;
                    break;
                case '.':
                    c.Background = Cell;
                    break;
                case 'S':
                    c.Background = Warn;
                    c.Text = "✱";
                    c.Foreground = Cell;
                    c.Bold = true;
                    break;
                case 'C':
                    c.Background = Bad;
                    c.Text = "★";
                    c.Foreground = Cell;
                    c.Bold = true;
                    break;
            }
        }

        foreach (var tower in _towers)
        {
            var c = _cells[tower.Y * MapW + tower.X];
            c.Text = TowerGlyph[tower.Kind];
            c.Background = tower.FlashIn > 0 ? Warn : tower.Level == 2 ? Ink : Accent;
            c.Foreground = Cell;
            c.Bold = true;
        }

        foreach (var creep in _creeps)
        {
            var (cx, cy) = _path[creep.PathIdx];
            var c = _cells[cy * MapW + cx];
            c.Text = creep.Tank ? "■" : "●";
            c.Background = creep.Hp * 2 <= creep.MaxHp ? Warn : Bad;
            c.Foreground = Cell;
            c.Bold = true;
        }

        DrawMenuRow();
        DrawInfoRow();
    }

    private void DrawMenuRow()
    {
        int y = MapH;
        if (_menuX < 0)
        {
            for (int x = 0; x < MapW; x++) _cells[y * MapW + x].Background = Board;
            return;
        }

        if (_menuTower)
        {
            var tower = _towers.FirstOrDefault(t => t.X == _menuX && t.Y == _menuY);
            if (tower == null) return;
            int cost = (int)(TowerCost[tower.Kind] * 0.8);
            Option(2, y, tower.Level >= 2 ? "MAX" : $"UP {cost}", tower.Level < 2 && _gold >= cost);
            Option(5, y, $"SELL {tower.Invested / 2}", true);
            Option(8, y, "✗", true);
        }
        else
        {
            Option(2, y, $"A {TowerCost[0]}", _gold >= TowerCost[0]);
            Option(5, y, $"C {TowerCost[1]}", _gold >= TowerCost[1]);
            Option(8, y, $"F {TowerCost[2]}", _gold >= TowerCost[2]);
        }
    }

    private void Option(int x, int y, string text, bool affordable)
    {
        var c = _cells[y * MapW + x];
        c.Text = text;
        c.Background = affordable ? Good : Board;
        c.Foreground = Cell;
        c.FontSize = 10;
        c.Bold = true;
    }

    private void DrawInfoRow()
    {
        int y = MapH + 1;
        Info(0, y, $"Gold {_gold}");
        Info(4, y, $"Castle {_castle}");
        int shown = _inCountdown ? _wave + 1 : _wave;
        Info(9, y, $"Wave {Math.Min(shown, WaveCount)}/{WaveCount}");
    }

    private void Info(int x, int y, string text)
    {
        var c = _cells[y * MapW + x];
        c.Text = text;
        c.Foreground = Muted;
        c.FontSize = 8;
    }
}
