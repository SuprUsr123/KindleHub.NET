using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Lucky Roller - pull random loot across seven rarities, from Common junk up to
/// the one-in-a-hundred COSMIC. Twenty rolls a run; every item lands in the
/// collection and the points add up into the score.
/// </summary>
public sealed class RollerGame : GameBase
{
    private const int Cols = 4;
    private const int RollQuota = 20;

    private sealed record Rarity(string Name, int Weight, int Points, IBrush Brush);

    // Weights add up to 100, so one percentile roll picks the tier.
    private static readonly Rarity[] Rarities =
    {
        new("Common", 32, 1, new SolidColorBrush(Color.Parse("#6b6b76"))),
        new("Uncommon", 26, 3, new SolidColorBrush(Color.Parse("#1f7a3d"))),
        new("Rare", 18, 8, new SolidColorBrush(Color.Parse("#2f6fed"))),
        new("Epic", 12, 20, new SolidColorBrush(Color.Parse("#6b3fa0"))),
        new("Legendary", 7, 50, new SolidColorBrush(Color.Parse("#9a6700"))),
        new("Mythic", 4, 150, new SolidColorBrush(Color.Parse("#b3261e"))),
        new("Cosmic", 1, 600, new SolidColorBrush(Color.Parse("#1b1b1f"))),
    };

    private static readonly string[][] Loot =
    {
        new[] { "Rusty Spoon", "Bent Nail", "Old Boot", "Chipped Cup", "Dusty Book", "Frayed Rope", "Cracked Pot", "Dull Coin" },
        new[] { "Silver Ring", "Oak Shield", "Travel Cloak", "Fine Compass", "Steel Dagger", "Leather Pack", "Copper Lamp", "Bone Flute" },
        new[] { "Enchanted Quill", "Moonstone", "Rune Blade", "Silver Horn", "Griffin Feather", "Flask of Dew", "Harbor Chart", "Miner's Pick" },
        new[] { "Dragon Scale", "Phoenix Feather", "Crystal Crown", "Storm Hammer", "Shadow Cloak", "Ancient Tome", "Sun Spear", "Wraith Chain" },
        new[] { "Crown of Kings", "Blade of Dawn", "Heart of Oak", "Titan Belt", "Seer's Orb", "Frost Brand", "Dragonheart", "Starforged Bow" },
        new[] { "Midas Glove", "Kraken Eye", "World Tree Leaf", "Time Shard", "Genie Lamp", "Godforged Anvil", "Phoenix Egg", "Leviathan Pearl" },
        new[] { "Heart of a Star", "Void Compass", "Galaxy in a Bottle", "Dawn of Time", "Singularity Shard", "Song of Creation", "Cosmic Crown", "First Light" },
    };

    private readonly List<GameCell> _cells = new();
    private readonly int[] _counts = new int[Rarities.Length];
    private int _rolls;
    private int _points;
    private string _lastItem = "";
    private int _lastRarity = -1;
    private bool _done;

    public RollerGame()
    {
        for (int i = 0; i < Cols * 9; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "roller";
    public override string Name => "Lucky Roller";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score => _points;

    public override string? ResultText => _done ? $"Twenty rolls, {_points} points." : null;

    public override string StatusText => _done
        ? $"Collection complete - {_points} points."
        : $"Rolls left {RollQuota - _rolls} · {_points} pts · tap ROLL";

    public override void Reset()
    {
        Array.Clear(_counts, 0, _counts.Length);
        _rolls = 0;
        _points = 0;
        _lastItem = "";
        _lastRarity = -1;
        _done = false;
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (key != GameKey.Confirm) return false;
        return Roll();
    }

    public override bool OnTap(int index)
    {
        if (index >= 0 && index < 4) return Roll();
        return false;
    }

    private bool Roll()
    {
        if (_done) return false;
        int roll = Random.Shared.Next(100);
        int r = Rarities.Length - 1, acc = 0;
        for (int i = 0; i < Rarities.Length; i++)
        {
            acc += Rarities[i].Weight;
            if (roll < acc)
            {
                r = i;
                break;
            }
        }

        var items = Loot[r];
        _lastItem = items[Random.Shared.Next(items.Length)];
        _lastRarity = r;
        _counts[r]++;
        _points += Rarities[r].Points;
        _rolls++;

        if (_rolls >= RollQuota)
        {
            _done = true;
            // Twenty rolls always earn at least 20 points, so the run always posts.
            ScoreCounts = true;
        }
        Redraw();
        return true;
    }

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

        // One fat ROLL button across the top row; any of the four cells works.
        for (int i = 0; i < 4; i++)
        {
            var c = _cells[i];
            c.Text = "ROLL ⚂";
            c.Background = Accent;
            c.Foreground = Cell;
            c.IsEnabled = !_done;
            c.FontSize = 20;
            c.Bold = true;
        }

        var label = _cells[4];
        label.Text = "Last";
        label.Foreground = Muted;
        label.FontSize = 12;

        var item = _cells[5];
        var rarity = _cells[6];
        var pts = _cells[7];
        if (_lastRarity >= 0)
        {
            var r = Rarities[_lastRarity];
            item.Text = Ellipsis(_lastItem, 14);
            item.Background = r.Brush;
            item.Foreground = Cell;
            item.FontSize = 12;
            item.Bold = true;
            rarity.Text = r.Name;
            rarity.Background = r.Brush;
            rarity.Foreground = Cell;
            rarity.FontSize = 12;
            rarity.Bold = true;
            pts.Text = $"+{r.Points}";
            pts.Background = Cell;
            pts.FontSize = 13;
            pts.Bold = true;
        }
        else
        {
            item.Background = Cell;
            rarity.Background = Cell;
            pts.Background = Cell;
        }

        // One summary row per rarity: name, count, points, drop rate.
        for (int r = 0; r < Rarities.Length; r++)
        {
            int row = 8 + r * 4;
            var name = _cells[row];
            name.Text = Rarities[r].Name;
            name.Background = Rarities[r].Brush;
            name.Foreground = Cell;
            name.FontSize = 12;
            name.Bold = true;

            var count = _cells[row + 1];
            count.Text = $"x{_counts[r]}";
            count.Background = Cell;
            count.FontSize = 13;
            count.Bold = true;

            var sum = _cells[row + 2];
            sum.Text = _counts[r] > 0 ? $"{_counts[r] * Rarities[r].Points} pts" : "-";
            sum.Background = Cell;
            sum.FontSize = 13;

            var odds = _cells[row + 3];
            odds.Text = $"{Rarities[r].Weight}%";
            odds.Background = Cell;
            odds.Foreground = Muted;
            odds.FontSize = 12;
        }
    }
}
