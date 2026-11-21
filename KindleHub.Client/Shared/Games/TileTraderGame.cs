using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Tile Trader - buy goods cheaply in one town and sell them dearly in another.
/// Six towns sit on a road grid; tapping a linked town travels there and costs a
/// day, and every day each town's prices drift. Turn 100 coins into 1000 within
/// thirty days, trading Spice, Gold and Wool out of a ten-unit cargo hold.
/// </summary>
public sealed class TileTraderGame : GameBase
{
    public const int MapW = 11;
    public const int MapH = 7;
    private const int MarketBase = MapW * MapH;        // 77: this town's market
    private const int InfoBase = MarketBase + MapW;    // 88: day, coins, hold, End
    private const int TotalDays = 30;
    private const int HoldCap = 10;
    private const int GoalCoins = 1000;
    private const int StartCoins = 100;
    private const int EndMinCoins = 100;

    private static readonly string[] TownNames = { "A", "B", "C", "D", "E", "F" };

    // A 3x2 grid of towns: A B C on top, D E F below.
    private static readonly (int X, int Y)[] Towns =
    {
        (1, 1), (5, 1), (9, 1), (1, 5), (5, 5), (9, 5),
    };

    // Roads: A-B, B-C, A-D, B-E, C-F, D-E, E-F (adjacent towns on the grid).
    private static readonly bool[,] Linked =
    {
        { false, true,  false, true,  false, false },
        { true,  false, true,  false, true,  false },
        { false, true,  false, false, false, true  },
        { true,  false, false, false, true,  false },
        { false, true,  false, true,  false, true  },
        { false, false, true,  false, true,  false },
    };

    private static readonly string[] Goods = { "Spice", "Gold", "Wool" };
    private static readonly int[] Base = { 30, 90, 20 };

    // Each town skews particular goods cheap or dear.
    private static readonly double[][] Skew =
    {
        new[] { 0.7, 1.2, 1.1 },
        new[] { 1.1, 0.8, 1.3 },
        new[] { 1.4, 1.0, 0.8 },
        new[] { 0.9, 1.3, 1.2 },
        new[] { 1.2, 0.9, 0.7 },
        new[] { 1.3, 1.1, 0.9 },
    };

    private static readonly IBrush[] TownTints =
    {
        new SolidColorBrush(Color.Parse("#dbe4f0")),
        new SolidColorBrush(Color.Parse("#e6ddf0")),
        new SolidColorBrush(Color.Parse("#f0e6d8")),
        new SolidColorBrush(Color.Parse("#d8eef0")),
        new SolidColorBrush(Color.Parse("#e4f0d8")),
        new SolidColorBrush(Color.Parse("#f0dbe6")),
    };

    // Roads borrow the pale 2048 tile tint, so the map mixes the shared palette
    // with the per-town pastels above.
    private static readonly IBrush RoadBrush = Tiles4[1];

    private readonly List<GameCell> _cells = new();
    private readonly bool[] _road = new bool[MapW * MapH];
    private readonly HashSet<int> _decor = new();
    private readonly int[] _cargo = new int[Goods.Length];
    private readonly double[] _drift = new double[Towns.Length * Goods.Length];
    private int _town;
    private int _day;
    private int _coins;
    private bool _over;
    private bool _won;
    private string _msg = "";

    public TileTraderGame()
    {
        for (int i = 0; i < InfoBase + MapW; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "tiletrader";
    public override string Name => "Tile Trader";
    public override int Columns => MapW;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score => _coins;

    public override string? ResultText => !_over ? null
        : _won ? "Trading empire - 1000 coins reached!"
        : _coins > 0 ? $"Out of time - {_coins} coins banked."
        : "Out of time with an empty purse.";

    public override string StatusText
    {
        get
        {
            if (_over) return _won ? $"Goal hit · {Score} points." : $"Run banked · {Score} points.";
            string extra = _msg.Length > 0 ? $" · {_msg}" : "";
            return $"Day {Math.Min(_day, TotalDays)}/{TotalDays} · in {TownNames[_town]} · {_coins} coins{extra}";
        }
    }

    public override void Reset()
    {
        _town = 0;
        _day = 1;
        _coins = StartCoins;
        Array.Clear(_cargo, 0, _cargo.Length);
        for (int i = 0; i < _drift.Length; i++) _drift[i] = 0.9 + Random.Shared.NextDouble() * 0.2;
        _over = false;
        _won = false;
        _msg = "buy low, sell high";
        ScoreCounts = false;
        LayRoads();
        _decor.Clear();
        while (_decor.Count < 8)
        {
            int i = Random.Shared.Next(MapW * MapH);
            if (_road[i] || TownAt(i % MapW, i / MapW) >= 0) continue;
            _decor.Add(i);
        }
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (_over) return false;
        var (dx, dy) = key switch
        {
            GameKey.Up => (0, -1),
            GameKey.Down => (0, 1),
            GameKey.Left => (-1, 0),
            GameKey.Right => (1, 0),
            _ => (0, 0),
        };
        if (dx == 0 && dy == 0) return false;
        // Arrow keys travel to the linked town in that direction.
        for (int t = 0; t < Towns.Length; t++)
            if (Linked[_town, t] &&
                Math.Sign(Towns[t].X - Towns[_town].X) == dx &&
                Math.Sign(Towns[t].Y - Towns[_town].Y) == dy)
                return Travel(t);
        return false;
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;

        if (index < MapW * MapH)
        {
            int t = TownAt(index % MapW, index / MapW);
            return t >= 0 ? Travel(t) : false;
        }

        switch (index)
        {
            case 78: Buy(0); return true;
            case 79: Sell(0); return true;
            case 81: Buy(1); return true;
            case 82: Sell(1); return true;
            case 84: Buy(2); return true;
            case 85: Sell(2); return true;
            case 92:
                if (_coins > EndMinCoins)
                {
                    _over = true;
                    ScoreCounts = true;
                    Redraw();
                }
                else
                {
                    _msg = $"finish above {EndMinCoins} coins first";
                    Redraw();
                }
                return true;
            default:
                return false;
        }
    }

    private bool Travel(int t)
    {
        if (_over) return false;
        if (t == _town) return true;
        if (!Linked[_town, t])
        {
            _msg = "no road there";
            Redraw();
            return true;
        }
        _town = t;
        _day++;
        DriftDay();
        _msg = $"arrived in {TownNames[t]}";
        if (_day > TotalDays)
        {
            // Thirty days are spent; whatever the purse holds is the score.
            _over = true;
            ScoreCounts = _coins >= 1;
        }
        Redraw();
        return true;
    }

    private void Buy(int g)
    {
        if (_over) return;
        int p = Price(_town, g);
        if (Hold() >= HoldCap) _msg = "cargo hold full";
        else if (_coins < p) _msg = "not enough coins";
        else
        {
            _coins -= p;
            _cargo[g]++;
            _msg = $"bought {Goods[g]} at {p}";
        }
        Redraw();
    }

    private void Sell(int g)
    {
        if (_over) return;
        int p = Price(_town, g);
        if (_cargo[g] <= 0) _msg = $"no {Goods[g]} to sell";
        else
        {
            _cargo[g]--;
            _coins += p;
            _msg = $"sold {Goods[g]} at {p}";
            if (_coins >= GoalCoins)
            {
                _over = true;
                _won = true;
                ScoreCounts = true;
            }
        }
        Redraw();
    }

    private int Hold() => _cargo[0] + _cargo[1] + _cargo[2];

    private int Price(int town, int g) =>
        Math.Max(1, (int)Math.Round(Base[g] * Skew[town][g] * _drift[town * Goods.Length + g]));

    /// <summary>Prices random-walk up to twenty percent a day, clamped to a band.</summary>
    private void DriftDay()
    {
        for (int i = 0; i < _drift.Length; i++)
            _drift[i] = Math.Clamp(_drift[i] * (0.8 + Random.Shared.NextDouble() * 0.4), 0.5, 2.0);
    }

    private int TownAt(int x, int y)
    {
        for (int t = 0; t < Towns.Length; t++)
            if (Towns[t].X == x && Towns[t].Y == y) return t;
        return -1;
    }

    /// <summary>Straight roads between every linked pair of towns.</summary>
    private void LayRoads()
    {
        Array.Clear(_road, 0, _road.Length);
        for (int a = 0; a < Towns.Length; a++)
            for (int b = a + 1; b < Towns.Length; b++)
            {
                if (!Linked[a, b]) continue;
                int x0 = Math.Min(Towns[a].X, Towns[b].X);
                int x1 = Math.Max(Towns[a].X, Towns[b].X);
                int y0 = Math.Min(Towns[a].Y, Towns[b].Y);
                int y1 = Math.Max(Towns[a].Y, Towns[b].Y);
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                        _road[y * MapW + x] = true;
            }
    }

    public override void Redraw()
    {
        // The map: tinted towns, pale roads, a few trees.
        for (int i = 0; i < MapW * MapH; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = false;
            c.FontSize = 18;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);

            int t = TownAt(i % MapW, i / MapW);
            if (t >= 0)
            {
                c.Text = TownNames[t];
                c.Background = TownTints[t];
                c.Bold = true;
                c.IsEnabled = !_over;
                if (t == _town)
                {
                    c.BorderBrush = Accent;
                    c.BorderThickness = new Avalonia.Thickness(2);
                }
                else if (Linked[_town, t])
                {
                    c.BorderBrush = Accent;
                    c.BorderThickness = new Avalonia.Thickness(1);
                }
                continue;
            }
            if (_road[i])
            {
                c.Background = RoadBrush;
                continue;
            }
            if (_decor.Contains(i))
            {
                c.Text = "■";
                c.Foreground = Muted;
            }
        }

        // This town's market: price, buy, sell for each of the three goods.
        for (int g = 0; g < Goods.Length; g++)
        {
            int baseIdx = MarketBase + g * 3;
            var price = _cells[baseIdx];
            price.Text = $"{Goods[g]}\n{Price(_town, g)}c";
            price.Background = Cell;
            price.FontSize = 12;
            price.Bold = true;

            var buy = _cells[baseIdx + 1];
            buy.Text = "Buy";
            buy.Background = Good;
            buy.Foreground = Cell;
            buy.IsEnabled = !_over;
            buy.FontSize = 13;
            buy.Bold = true;

            var sell = _cells[baseIdx + 2];
            sell.Text = _cargo[g] > 0 ? $"Sell {_cargo[g]}" : "Sell";
            sell.Background = _cargo[g] > 0 ? Warn : Cell;
            sell.Foreground = _cargo[g] > 0 ? Cell : Muted;
            sell.IsEnabled = !_over;
            sell.FontSize = 13;
            sell.Bold = true;
        }

        // Readouts.
        Readout(InfoBase, $"Day\n{Math.Min(_day, TotalDays)}/{TotalDays}");
        Readout(InfoBase + 1, $"Coins\n{_coins}");
        Readout(InfoBase + 2, $"Hold\n{Hold()}/{HoldCap}");
        Readout(InfoBase + 3, $"In\n{TownNames[_town]}");

        var end = _cells[InfoBase + 4];
        end.Text = "End";
        end.Background = Cell;
        end.Foreground = Ink;
        end.IsEnabled = !_over;
        end.FontSize = 13;
        end.Bold = true;
        end.BorderBrush = Accent;
        end.BorderThickness = new Avalonia.Thickness(1);
    }

    private void Readout(int index, string text)
    {
        var c = _cells[index];
        c.Text = text;
        c.Background = Board;
        c.Foreground = Ink;
        c.IsEnabled = false;
        c.FontSize = 12;
        c.Bold = true;
    }
}
