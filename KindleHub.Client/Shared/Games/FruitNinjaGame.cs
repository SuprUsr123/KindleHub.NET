using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Fruit Ninja — swipe the flying fruit, ported from the official client. Fruit
/// arcs up from the bottom of the board; tap it to slice, catch several in one
/// tap for a combo, and never touch a bomb. Three missed fruit ends the run.
/// </summary>
public sealed class FruitNinjaGame : GameBase
{
    private const int Width = 11;
    private const int Height = 13;

    private const int FrameMs = 50;        // fixed simulation frame
    private const int MaxFrames = 6;       // catch-up guard: at most 6 frames per Tick
    private const double Dt = 0.05;        // frame length in seconds
    private const double Gravity = 14.0;   // cells/s²
    private const int SpawnMs = 900;       // one item per spawn at the start
    private const int MinSpawnMs = 400;    // spawn cadence floor as it speeds up
    private const int SpawnSpeedup = 15;   // ms faster per item already thrown
    private const int BombChance = 20;     // percent
    private const int SlicePoints = 10;
    private const int ComboBonus = 5;
    private const int MaxMisses = 3;

    private sealed class Item
    {
        public double X, Y, Vx, Vy;
        public bool Bomb;
        public int Tint;
    }

    private readonly List<GameCell> _cells = new();
    private readonly List<Item> _items = new();
    private int _misses;
    private int _spawnIn;
    private int _spawned;
    private int _score;
    private string _cause = "";
    private TimeSpan _acc;
    private bool _over;

    public FruitNinjaGame()
    {
        for (int i = 0; i < Width * Height; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "fruitninja";
    public override string Name => "Fruit Ninja";
    public override int Columns => Width;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override bool IsRealTime => true;

    public override int Score => Math.Clamp(_score, 0, 1_000_000);

    public override string? ResultText => _over ? $"{_cause} — {Score} points." : null;

    public override string StatusText => _over
        ? $"Final score {Score}."
        : $"Score {_score} · miss {_misses}/{MaxMisses} · tap fruit";

    public override void Reset()
    {
        _items.Clear();
        _misses = 0;
        _spawnIn = SpawnMs;
        _spawned = 0;
        _score = 0;
        _cause = "";
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
        _spawnIn -= FrameMs;
        if (_spawnIn <= 0)
        {
            _spawnIn += Math.Max(MinSpawnMs, SpawnMs - _spawned * SpawnSpeedup);
            Spawn();
        }

        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var it = _items[i];
            it.Vy += Gravity * Dt;
            it.X += it.Vx * Dt;
            it.Y += it.Vy * Dt;

            if (it.X < -1.5 || it.X > Width + 0.5 || it.Y > Height + 0.5)
            {
                _items.RemoveAt(i);
                if (!it.Bomb && it.Y > Height + 0.5)
                {
                    _misses++;
                    if (_misses >= MaxMisses) { End("Three misses"); return; }
                }
            }
        }
    }

    private void Spawn()
    {
        _spawned++;
        bool bomb = Random.Shared.Next(100) < BombChance;
        _items.Add(new Item
        {
            X = 2 + Random.Shared.NextDouble() * 7,
            Y = Height + 0.4,
            Vx = (Random.Shared.NextDouble() - 0.5) * 3,
            Vy = -(13 + Random.Shared.NextDouble() * 4), // arcs reach most of the board
            Bomb = bomb,
            Tint = Random.Shared.Next(Tiles4.Length),
        });
    }

    public override bool OnKey(GameKey key)
    {
        // Keyboard play: confirm slices the cell in front of the lowest fruit.
        if (_over) return false;
        if (key != GameKey.Confirm) return false;
        var target = _items
            .Where(i => !i.Bomb)
            .OrderBy(i => i.Y)
            .FirstOrDefault();
        if (target == null) return false;
        int x = Math.Clamp((int)Math.Round(target.X), 0, Width - 1);
        int y = Math.Clamp((int)Math.Round(target.Y), 0, Height - 1);
        return Slice(x, y);
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;
        return Slice(index % Width, index / Width);
    }

    private bool Slice(int x, int y)
    {
        // A bomb under the blade ends the run instantly.
        if (_items.Any(i => i.Bomb && CellX(i) == x && CellY(i) == y))
        {
            End("Boom");
            return true;
        }

        bool sliced = false;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var it = _items[i];
            if (it.Bomb) continue;
            if (CellX(it) == x && CellY(it) == y)
            {
                _score += SlicePoints;
                _items.RemoveAt(i);
                sliced = true;
            }
        }

        // Fruit caught next to the tap cell goes in the same swipe, +5 each.
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var it = _items[i];
            if (it.Bomb) continue;
            int dx = CellX(it) - x, dy = CellY(it) - y;
            if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 && (dx != 0 || dy != 0))
            {
                _score += ComboBonus;
                _items.RemoveAt(i);
                sliced = true;
            }
        }

        if (sliced) Redraw();
        return sliced;
    }

    private static int CellX(Item it) => Math.Clamp((int)Math.Round(it.X), 0, Width - 1);

    private static int CellY(Item it) => Math.Clamp((int)Math.Round(it.Y), 0, Height - 1);

    private void End(string cause)
    {
        if (_over) return;
        _over = true;
        _cause = cause;
        ScoreCounts = _score > 0;
        Redraw();
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
            c.FontSize = 16;
            c.Bold = false;
        }

        foreach (var it in _items)
        {
            var c = _cells[CellY(it) * Width + CellX(it)];
            if (it.Bomb)
            {
                c.Text = "✱";
                c.Background = Ink;
                c.Foreground = Cell;
            }
            else
            {
                c.Text = "●";
                c.Background = Tiles4[it.Tint];
                c.Foreground = Ink;
            }
            c.Bold = true;
        }
    }
}
