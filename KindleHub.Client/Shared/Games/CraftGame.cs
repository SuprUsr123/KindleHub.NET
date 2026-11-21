using System;
using System.Collections.Generic;

namespace KindleHub.Client.Games;

/// <summary>
/// Infinite Craft — start with Water, Fire, Wind and Earth and combine any two
/// things to discover something new (Water + Fire = Steam, and on and on). The
/// official client dreams up brand-new things with AI; this offline port carries
/// the same flavour of recipe table instead, with a hundred-plus real chains up
/// to Dragon, Rainbow, Volcano, Life, Human and Galaxy.
/// </summary>
public sealed class CraftGame : GameBase
{
    private const int Wide = 6;
    private const int Rows = 6;
    private const int SlotBase = 6;      // 24 element slots fill rows 1-4
    private const int SlotCount = 24;
    private const int EndCell = 30;
    private const int MinFindsToEnd = 5;

    private static readonly string[] Starters = { "Water", "Fire", "Wind", "Earth" };
    private static readonly Dictionary<string, string> Recipes = BuildRecipes();

    private readonly List<GameCell> _cells = new();
    private readonly List<string> _discovered = new();
    private string? _selected;
    private string? _lastA;
    private string? _lastB;
    private string? _lastResult;
    private bool _isNew;
    private bool _over;
    private string _msg = "";

    public CraftGame()
    {
        for (int i = 0; i < Wide * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "craft";
    public override string Name => "Infinite Craft";
    public override int Columns => Wide;
    public override IReadOnlyList<GameCell> Cells => _cells;

    private int Finds => _discovered.Count - Starters.Length;

    public override int Score => 15 * Math.Max(0, Finds);

    public override string? ResultText => _over ? $"Discovered {Finds} things." : null;

    public override string StatusText
    {
        get
        {
            if (_over) return $"Run banked · {Finds} discoveries · {Score} points.";
            int hidden = Math.Max(0, _discovered.Count - SlotCount);
            string s = _msg.Length > 0
                ? _msg
                : $"Found {_discovered.Count} things · tap two to mix";
            if (hidden > 0) s += $" · …and {hidden} more";
            return s;
        }
    }

    public override void Reset()
    {
        _discovered.Clear();
        _discovered.AddRange(Starters);
        _selected = null;
        _lastA = null;
        _lastB = null;
        _lastResult = null;
        _isNew = false;
        _over = false;
        _msg = "";
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;

        if (index >= SlotBase && index < SlotBase + SlotCount)
        {
            var display = DisplayList();
            int j = index - SlotBase;
            if (j >= display.Count) return false;
            string name = display[j];
            if (_selected == null)
            {
                _selected = name;
                _msg = "";
            }
            else
            {
                // Tapping the same thing twice mixes it with itself.
                Combine(_selected, name);
            }
            Redraw();
            return true;
        }

        if (index == EndCell)
        {
            if (Finds >= MinFindsToEnd)
            {
                _over = true;
                ScoreCounts = true;
                Redraw();
            }
            else
            {
                _msg = $"Find {MinFindsToEnd} things first ({Finds} so far)";
                Redraw();
            }
            return true;
        }

        return false;
    }

    private void Combine(string a, string b)
    {
        _selected = null;
        _lastA = a;
        _lastB = b;
        if (Recipes.TryGetValue(Key(a, b), out var result))
        {
            _lastResult = result;
            _isNew = !_discovered.Contains(result);
            if (_isNew) _discovered.Add(result);
        }
        else
        {
            _lastResult = null;
            _isNew = false;
        }
    }

    /// <summary>The starters stay pinned; beyond that the 20 newest finds show.</summary>
    private List<string> DisplayList()
    {
        var list = new List<string>(Starters);
        for (int i = Math.Max(Starters.Length, _discovered.Count - 20); i < _discovered.Count; i++)
            list.Add(_discovered[i]);
        return list;
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
            c.FontSize = 16;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
        }

        // Formula row: A + B = C, with a NEW badge when the mix made something.
        var a = _cells[0];
        a.Text = _selected ?? _lastA ?? "Tap two";
        a.FontSize = 11;
        a.Foreground = _selected != null ? Ink : Muted;
        _cells[1].Text = "+";
        _cells[1].FontSize = 14;
        _cells[1].Foreground = Muted;
        _cells[2].Text = _lastB ?? "";
        _cells[2].FontSize = 11;
        _cells[3].Text = "=";
        _cells[3].FontSize = 14;
        _cells[3].Foreground = Muted;
        var result = _cells[4];
        result.FontSize = 11;
        if (_lastB == null)
        {
            result.Text = "";
        }
        else if (_lastResult != null)
        {
            result.Text = Ellipsis(_lastResult, 10);
            result.Bold = _isNew;
        }
        else
        {
            result.Text = "nothing new";
            result.Foreground = Muted;
        }
        var badge = _cells[5];
        if (_isNew)
        {
            badge.Text = "✓ NEW";
            badge.Background = Warn;
            badge.Foreground = Cell;
            badge.FontSize = 11;
            badge.Bold = true;
        }

        var display = DisplayList();
        for (int j = 0; j < SlotCount; j++)
        {
            var c = _cells[SlotBase + j];
            if (j >= display.Count) continue;
            string name = display[j];
            c.Text = Ellipsis(name, 10);
            c.FontSize = 11;
            c.Background = Cell;
            c.IsEnabled = !_over;
            if (_selected == name)
            {
                c.Bold = true;
                c.BorderBrush = Accent;
                c.BorderThickness = new Avalonia.Thickness(2);
            }
        }

        var end = _cells[EndCell];
        end.Text = "End";
        end.Background = Cell;
        end.IsEnabled = !_over;
        end.FontSize = 13;
        end.Bold = true;
        end.BorderBrush = Accent;
        end.BorderThickness = new Avalonia.Thickness(1);
    }

    private static string Key(string a, string b) =>
        string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;

    /// <summary>Every chain is reachable from the four starters.</summary>
    private static Dictionary<string, string> BuildRecipes()
    {
        var r = new Dictionary<string, string>();
        void Add(string a, string b, string c) => r[Key(a, b)] = c;

        Add("Water", "Fire", "Steam");
        Add("Water", "Earth", "Plant");
        Add("Water", "Wind", "Cloud");
        Add("Water", "Water", "Sea");
        Add("Earth", "Fire", "Lava");
        Add("Fire", "Wind", "Energy");
        Add("Fire", "Fire", "Inferno");
        Add("Earth", "Earth", "Mountain");
        Add("Earth", "Wind", "Dust");
        Add("Wind", "Wind", "Storm");
        Add("Earth", "Steam", "Geyser");
        Add("Steam", "Wind", "Fog");
        Add("Metal", "Steam", "Engine");
        Add("Fire", "Plant", "Ash");
        Add("Plant", "Plant", "Forest");
        Add("Lava", "Water", "Stone");
        Add("Lava", "Stone", "Obsidian");
        Add("Fire", "Stone", "Metal");
        Add("Stone", "Stone", "Boulder");
        Add("Stone", "Wind", "Sand");
        Add("Mountain", "Stone", "Cave");
        Add("Energy", "Metal", "Electricity");
        Add("Energy", "Water", "Wave");
        Add("Energy", "Energy", "Light");
        Add("Cloud", "Water", "Rain");
        Add("Cloud", "Cloud", "Sky");
        Add("Cloud", "Energy", "Lightning");
        Add("Cloud", "Storm", "Thunder");
        Add("Earth", "Rain", "Flower");
        Add("Rain", "Sun", "Rainbow");
        Add("Rain", "Wind", "Snow");
        Add("Snow", "Snow", "Ice");
        Add("Ice", "Ice", "Glacier");
        Add("Mountain", "Snow", "Avalanche");
        Add("Fire", "Sky", "Sun");
        Add("Sky", "Stone", "Moon");
        Add("Sky", "Sky", "Space");
        Add("Sun", "Sun", "Star");
        Add("Star", "Star", "Galaxy");
        Add("Galaxy", "Galaxy", "Universe");
        Add("Space", "Universe", "Multiverse");
        Add("Sun", "Universe", "Black Hole");
        Add("Moon", "Sun", "Eclipse");
        Add("Moon", "Water", "Tide");
        Add("Sea", "Sea", "Ocean");
        Add("Lava", "Sea", "Soup");
        Add("Lightning", "Soup", "Life");
        Add("Earth", "Life", "Animal");
        Add("Clay", "Life", "Human");
        Add("Life", "Swamp", "Reptile");
        Add("Fire", "Life", "Phoenix");
        Add("Cave", "Life", "Bat");
        Add("Animal", "Water", "Fish");
        Add("Animal", "Sky", "Bird");
        Add("Animal", "Forest", "Wolf");
        Add("Animal", "House", "Cat");
        Add("Bird", "Metal", "Airplane");
        Add("Fish", "Ocean", "Whale");
        Add("Fire", "Fish", "Sushi");
        Add("Mud", "Plant", "Swamp");
        Add("Mud", "Stone", "Clay");
        Add("Fire", "Mud", "Brick");
        Add("Dust", "Water", "Mud");
        Add("Brick", "Brick", "Wall");
        Add("Wall", "Wall", "House");
        Add("House", "Stone", "Castle");
        Add("House", "House", "Village");
        Add("Village", "Village", "City");
        Add("Tree", "Tree", "Forest");
        Add("Flower", "Water", "Seed");
        Add("Earth", "Seed", "Tree");
        Add("Tool", "Tree", "Wood");
        Add("Fire", "Wood", "Campfire");
        Add("Water", "Wood", "Boat");
        Add("Boat", "Metal", "Ship");
        Add("Human", "Ship", "Pirate");
        Add("Island", "Pirate", "Treasure");
        Add("Wood", "Wood", "Bridge");
        Add("Tool", "Wood", "Wheel");
        Add("Engine", "Wheel", "Car");
        Add("Engine", "Wind", "Plane");
        Add("Car", "City", "Traffic");
        Add("Engine", "Machine", "Train");
        Add("Metal", "Sun", "Gold");
        Add("Metal", "Metal", "Blade");
        Add("Blade", "Wood", "Sword");
        Add("Blade", "Machine", "Chainsaw");
        Add("Human", "Sword", "Knight");
        Add("Dragon", "Knight", "Hero");
        Add("Human", "Metal", "Tool");
        Add("Tool", "Tool", "Machine");
        Add("Energy", "Machine", "Robot");
        Add("Human", "Robot", "Cyborg");
        Add("Energy", "Human", "Wizard");
        Add("Fire", "Wizard", "Fireball");
        Add("Gold", "Human", "King");
        Add("Castle", "King", "Kingdom");
        Add("Fire", "Reptile", "Dragon");
        Add("Dragon", "Sky", "Wyvern");
        Add("Moon", "Wolf", "Werewolf");
        Add("Human", "Wolf", "Dog");
        Add("Bat", "Human", "Vampire");
        Add("Ash", "Life", "Ghost");
        Add("Ghost", "House", "Haunted");
        Add("Ash", "Human", "Zombie");
        Add("Lava", "Mountain", "Volcano");
        Add("Volcano", "Water", "Island");
        Add("Forest", "Island", "Jungle");
        Add("Jungle", "Life", "Monkey");
        Add("Sun", "Sand", "Desert");
        Add("Desert", "Plant", "Cactus");
        Add("Desert", "Water", "Oasis");
        Add("Fire", "Sand", "Glass");
        Add("Glass", "Metal", "Mirror");
        Add("Electricity", "Glass", "Lamp");
        Add("Electricity", "Metal", "Wire");
        Add("Wire", "Wire", "Circuit");
        Add("Circuit", "Energy", "Computer");
        Add("Computer", "Computer", "Internet");
        Add("Cat", "Internet", "Meme");
        Add("Sand", "Wave", "Beach");
        Add("Storm", "Storm", "Hurricane");
        Add("Flower", "Flower", "Garden");
        Add("Fire", "Forest", "Wildfire");
        return r;
    }
}
