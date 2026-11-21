using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Odd One Out, ported from the official client — four things, one of which does not
/// belong. Pick it and the reason is explained either way. Ten rounds a run.
/// </summary>
public sealed class OddOneGame : GameBase
{
    private const int Cols = 8;
    private const int ItemBase = 2;    // item cells, row 0 cols 2..5
    private const int Rounds = 10;

    // Four items, the index of the odd one, and a one-line reason.
    private static readonly (string[] Items, int Odd, string Reason)[] Bank =
    {
        (new[] { "APPLE", "BANANA", "CARROT", "PEAR" }, 2, "Carrot is a vegetable; the rest are fruit."),
        (new[] { "MARS", "VENUS", "JUPITER", "MOON" }, 3, "The Moon is a satellite, not a planet."),
        (new[] { "DOG", "CAT", "COW", "SPARROW" }, 3, "Sparrow is a bird; the rest are mammals."),
        (new[] { "VIOLIN", "GUITAR", "HARP", "DRUM" }, 3, "The drum has no strings."),
        (new[] { "ROSE", "TULIP", "OAK", "DAISY" }, 2, "The oak is a tree; the rest are flowers."),
        (new[] { "SEDAN", "TRUCK", "BUS", "YACHT" }, 3, "The yacht is a boat; the rest have wheels."),
        (new[] { "GOLD", "SILVER", "WOOD", "BRONZE" }, 2, "Wood is not a metal."),
        (new[] { "SHARK", "TUNA", "SALMON", "DOLPHIN" }, 3, "The dolphin is a mammal; the rest are fish."),
        (new[] { "JANUARY", "MARCH", "JULY", "SUNDAY" }, 3, "Sunday is a day; the rest are months."),
        (new[] { "LION", "TIGER", "WOLF", "EAGLE" }, 3, "The eagle is a bird; the rest are mammals."),
        (new[] { "THREE", "SEVEN", "NINE", "TWELVE" }, 3, "Twelve is even; the rest are odd numbers."),
        (new[] { "CIRCLE", "SQUARE", "OVAL", "CUBE" }, 3, "The cube is 3-D; the rest are flat shapes."),
        (new[] { "PEN", "PENCIL", "CRAYON", "INK" }, 3, "Ink is a liquid; the rest are tools."),
        (new[] { "BICYCLE", "TRAIN", "PLANE", "CANOE" }, 3, "The canoe has no wheels."),
        (new[] { "EAGLE", "SPARROW", "PIGEON", "BAT" }, 3, "The bat is a mammal; the rest are birds."),
        (new[] { "WHEAT", "RICE", "OATS", "BEEF" }, 3, "Beef is meat; the rest are grains."),
        (new[] { "SUN", "MOON", "STAR", "LAMP" }, 3, "The lamp is made by people, not in the sky."),
        (new[] { "BLUE", "GREEN", "RED", "ROUND" }, 3, "Round is a shape; the rest are colours."),
        (new[] { "MILK", "JUICE", "WATER", "BREAD" }, 3, "Bread is solid; the rest are drinks."),
        (new[] { "COPPER", "IRON", "GLASS", "STEEL" }, 2, "Glass is not a metal."),
        (new[] { "TIGER", "ZEBRA", "PANDA", "PENGUIN" }, 3, "The penguin is a bird; the rest are mammals."),
        (new[] { "JUPITER", "SATURN", "MERCURY", "PLUTO" }, 3, "Pluto is a dwarf planet."),
        (new[] { "CHEESE", "YOGURT", "BUTTER", "APPLE" }, 3, "The apple is fruit; the rest are dairy."),
        (new[] { "NILE", "DANUBE", "EVEREST", "VOLGA" }, 2, "Everest is a mountain; the rest are rivers."),
        (new[] { "SPINACH", "LETTUCE", "KALE", "BANANA" }, 3, "The banana is fruit; the rest are greens."),
        (new[] { "CELLO", "DRUM", "HARP", "EASEL" }, 3, "The easel is for painting, not music."),
        (new[] { "SOFA", "TABLE", "CHAIR", "CARPET" }, 3, "The carpet is not furniture."),
        (new[] { "DIAMOND", "RUBY", "PEARL", "GRANITE" }, 3, "Granite is plain rock, not a gem."),
        (new[] { "HEART", "LUNGS", "BRAIN", "MOTOR" }, 3, "The motor is a machine, not an organ."),
        (new[] { "DOVE", "EAGLE", "WHALE", "SPARROW" }, 2, "The whale is a mammal; the rest are birds."),
        (new[] { "BUS", "TRAIN", "TRAM", "FERRY" }, 3, "The ferry is a boat; the rest run on land."),
        (new[] { "BEE", "ANT", "SPIDER", "WASP" }, 2, "The spider has eight legs, not six."),
        (new[] { "SIX", "EIGHT", "TEN", "SEVEN" }, 3, "Seven is odd; the rest are even."),
        (new[] { "OAK", "PINE", "ROSE", "MAPLE" }, 2, "The rose is a flower; the rest are trees."),
        (new[] { "KNIFE", "SPOON", "FORK", "PLATE" }, 3, "The plate is not for eating with."),
        (new[] { "LEMON", "GRAPE", "MANGO", "CELERY" }, 3, "Celery is a vegetable; the rest are fruit."),
        (new[] { "MARS", "EARTH", "VENUS", "COMET" }, 3, "The comet is ice and dust, not a planet."),
        (new[] { "SOCK", "GLOVE", "HAT", "WOOL" }, 3, "Wool is a material, not clothing."),
        (new[] { "TROUT", "SALMON", "PERCH", "HERON" }, 3, "The heron is a bird; the rest are fish."),
        (new[] { "VIENNA", "PARIS", "LONDON", "SPAIN" }, 3, "Spain is a country; the rest are cities."),
    };

    private readonly List<GameCell> _cells = new();
    private readonly List<int> _rounds = new();
    private int _place;
    private int _correct;
    private int _streak;
    private int _bestStreak;
    private bool _answered;
    private int _chosen;
    private bool _done;
    private string _message = "";

    public OddOneGame()
    {
        for (int i = 0; i < Cols * 2; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "oddone";
    public override string Name => "Odd One Out";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _done ? 10 * _correct + 5 * _bestStreak : 0;
    public override string? ResultText => _done ? $"{_correct}/{Rounds} correct." : null;

    public override string StatusText
    {
        get
        {
            if (_done) return $"Finished — {_correct}/{Rounds} correct.";
            if (_answered) return "Tap any item to continue.";
            return $"Round {_place + 1}/{Rounds} · {_correct} right · streak {_streak} · tap the odd one";
        }
    }

    public override void Reset()
    {
        _rounds.Clear();
        foreach (var i in Enumerable.Range(0, Bank.Length).OrderBy(_ => Random.Shared.Next()).Take(Rounds))
            _rounds.Add(i);
        _place = 0;
        _correct = 0;
        _streak = 0;
        _bestStreak = 0;
        _answered = false;
        _done = false;
        _message = "";
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (key == GameKey.Confirm && _answered && !_done)
        {
            Advance();
            return true;
        }
        return base.OnKey(key);
    }

    public override bool OnTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;
        int item = index - ItemBase;
        if (item < 0 || item > 3) return false;

        if (_answered)
        {
            Advance();
            return true;
        }

        var puzzle = Bank[_rounds[_place]];
        _answered = true;
        _chosen = item;
        if (item == puzzle.Odd)
        {
            _correct++;
            _streak++;
            _bestStreak = Math.Max(_bestStreak, _streak);
            _message = "Right! " + puzzle.Reason;
        }
        else
        {
            _streak = 0;
            _message = $"It was {puzzle.Items[puzzle.Odd]}. " + puzzle.Reason;
        }
        Redraw();
        return true;
    }

    private void Advance()
    {
        _place++;
        _answered = false;
        if (_place >= Rounds)
        {
            _done = true;
            if (_correct > 0) ScoreCounts = true;
        }
        Redraw();
    }

    public override void Redraw()
    {
        foreach (var c in _cells)
        {
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = false;
            c.Bold = false;
            c.FontSize = 14;
            c.Span = 1;
        }

        if (_done)
        {
            SpreadRow(Cols, $"Done — {_correct}/{Rounds} correct", Cols, 14, 3);
            return;
        }

        var bank = Bank[_rounds[_place]];
        for (int i = 0; i < 4; i++)
        {
            var cell = _cells[ItemBase + i];
            cell.Text = bank.Items[i];
            cell.FontSize = 11;
            if (_answered)
            {
                if (i == bank.Odd)
                {
                    cell.Background = Good;
                    cell.Foreground = Cell;
                    cell.Bold = true;
                }
                else if (i == _chosen)
                {
                    cell.Background = Bad;
                    cell.Foreground = Cell;
                }
                else
                {
                    cell.Foreground = Muted;
                }
            }
            else
            {
                cell.Background = Cell;
            }
        }

        if (_answered) SpreadRow(Cols, _message, Cols, 11, 6);
    }

    /// <summary>Splits text evenly across a row of cells so one row can carry a sentence.</summary>
    private void SpreadRow(int baseIndex, string text, int parts, int fontSize, int perCell)
    {
        text = text.Trim();
        if (text.Length > parts * perCell) text = Ellipsis(text, parts * perCell);
        int chunk = (text.Length + parts - 1) / parts;
        for (int i = 0; i < parts; i++)
        {
            int at = i * chunk;
            _cells[baseIndex + i].Text = at < text.Length
                ? text[at..Math.Min(at + chunk, text.Length)]
                : "";
            _cells[baseIndex + i].FontSize = fontSize;
        }
    }
}
