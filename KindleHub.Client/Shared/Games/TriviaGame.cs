using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Trivia, ported from the official client — a multiple-choice general-knowledge quiz.
/// Read the question, tap the answer you think is right, and a one-line fact explains
/// it either way. Twelve questions a run.
/// </summary>
public sealed class TriviaGame : GameBase
{
    private const int Cols = 8;
    private const int QuestionsPerRun = 12;

    // Option cells sit as a 2x2 block: (row, col) pairs below.
    private static readonly int[] OptionCells = { 10, 13, 18, 21 };

    // Question, four options, the correct index, and a one-line fact.
    private static readonly (string Prompt, string A, string B, string C, string D, int Correct, string Fact)[] Bank =
    {
        ("Which planet is closest to the Sun?", "Mercury", "Venus", "Mars", "Earth", 0, "Mercury zips round the Sun in 88 days."),
        ("What is the largest ocean?", "Pacific", "Arctic", "Indian", "Baltic", 0, "The Pacific covers a third of the Earth."),
        ("How many continents are there?", "5", "6", "7", "8", 2, "Most lists count seven continents."),
        ("What colour is a polar bear's skin?", "Black", "White", "Pink", "Grey", 0, "Under the white fur the skin is black."),
        ("Plants take in which gas?", "CO2", "Oxygen", "Helium", "Argon", 0, "Carbon dioxide feeds photosynthesis."),
        ("Where is the Mona Lisa displayed?", "Paris", "Rome", "Madrid", "Vienna", 0, "It hangs in the Louvre in Paris."),
        ("How many legs has a spider?", "6", "8", "10", "12", 1, "Spiders are arachnids, not insects."),
        ("What is H2O?", "Water", "Salt", "Sugar", "Steam", 0, "Two hydrogens and one oxygen."),
        ("Which country built the Great Wall?", "China", "Japan", "Korea", "India", 0, "It stretched for thousands of miles."),
        ("What is the hardest substance?", "Diamond", "Quartz", "Steel", "Granite", 0, "Diamond scores 10 on the Mohs scale."),
        ("How many sides has a hexagon?", "5", "6", "7", "8", 1, "Hex- is Greek for six."),
        ("Which animal is the tallest?", "Giraffe", "Ostrich", "Zebra", "Moose", 0, "Giraffes can reach five and a half metres."),
        ("What do bees make?", "Honey", "Silk", "Milk", "Butter", 0, "Bees turn nectar into honey."),
        ("Which metal is liquid at room temp?", "Mercury", "Lead", "Tin", "Zinc", 0, "Mercury melts at minus 39 degrees."),
        ("How many minutes are in an hour?", "30", "45", "60", "90", 2, "Sixty, a habit from ancient Babylon."),
        ("What is the capital of Japan?", "Tokyo", "Kyoto", "Osaka", "Seoul", 0, "Tokyo was called Edo until 1868."),
        ("Which sense do ears provide?", "Hearing", "Sight", "Smell", "Taste", 0, "The ear also controls balance."),
        ("How many colours in a rainbow?", "5", "6", "7", "9", 2, "Newton chose seven on purpose."),
        ("What is the largest planet?", "Jupiter", "Saturn", "Neptune", "Earth", 0, "Jupiter outweighs all the other planets."),
        ("What is a young cat called?", "Kitten", "Puppy", "Cub", "Foal", 0, "Kitten came from Old French."),
        ("How many players on a football team?", "9", "10", "11", "12", 2, "Eleven a side on the pitch."),
        ("Which sport is played at Wimbledon?", "Tennis", "Golf", "Cricket", "Rugby", 0, "Wimbledon started in 1877."),
        ("How many days in a leap year?", "364", "365", "366", "367", 2, "February gains a 29th day."),
        ("What is the boiling point of water?", "50C", "90C", "100C", "120C", 2, "At sea level, 100 degrees Celsius."),
        ("Which bird cannot fly?", "Penguin", "Eagle", "Sparrow", "Swallow", 0, "Penguins swim instead of flying."),
        ("How many bones in an adult human?", "106", "206", "306", "406", 1, "Babies start with about 300 bones."),
        ("What is the capital of France?", "Paris", "Lyon", "Rome", "Berlin", 0, "Paris sits on the river Seine."),
        ("Which language has most speakers?", "Chinese", "English", "Arabic", "Hindi", 0, "Chinese tops a billion speakers."),
        ("What does a compass point to?", "North", "South", "East", "West", 0, "The needle aligns itself with north."),
        ("How many strings has a violin?", "4", "5", "6", "7", 0, "The strings are G, D, A and E."),
        ("Which is the smallest planet?", "Mercury", "Mars", "Pluto", "Venus", 0, "Pluto was reclassified in 2006."),
        ("What is cheese mostly made from?", "Milk", "Eggs", "Wheat", "Soy", 0, "Curdled milk makes the curds."),
        ("Where did the Olympics begin?", "Greece", "Italy", "Egypt", "France", 0, "Olympia, from 776 BC."),
        ("What is the fastest land animal?", "Cheetah", "Lion", "Horse", "Hare", 0, "Cheetahs hit 100 km/h in bursts."),
        ("How many hours are in a day?", "12", "24", "36", "48", 1, "One full spin of the Earth."),
        ("Which sea creature has 8 arms?", "Octopus", "Squid", "Crab", "Eel", 0, "Octo is Greek for eight."),
        ("Mixing red and white gives?", "Pink", "Purple", "Brown", "Green", 0, "Pink is a tint of red."),
        ("What is the tallest mountain?", "Everest", "K2", "Alps", "Etna", 0, "Everest stands 8,849 metres tall."),
        ("Which animal says moo?", "Cow", "Sheep", "Goat", "Duck", 0, "Cows moo to call their calves."),
        ("How many wheels has a bicycle?", "2", "3", "4", "5", 0, "Bi- means two, like bicentennial."),
        ("Which gas do people need to breathe?", "Oxygen", "Helium", "CO2", "Argon", 0, "Oxygen keeps our cells alive."),
        ("How many strings has a guitar?", "4", "6", "8", "12", 1, "Most guitars have six strings."),
        ("How many sides has a triangle?", "3", "4", "5", "6", 0, "Tri- means three, like tricycle."),
        ("What is the largest hot desert?", "Sahara", "Gobi", "Mojave", "Atacama", 0, "The Sahara is the largest hot desert."),
        ("Which planet is the Red Planet?", "Mars", "Venus", "Jupiter", "Saturn", 0, "Iron oxide dust colours Mars red."),
        ("What do we call frozen water?", "Ice", "Steam", "Salt", "Rock", 0, "Ice is less dense than water."),
    };

    private readonly List<GameCell> _cells = new();
    private readonly List<int> _run = new();
    private int _place;
    private int _correct;
    private int _streak;
    private int _bestStreak;
    private bool _answered;
    private int _chosen;
    private bool _done;

    public TriviaGame()
    {
        for (int i = 0; i < Cols * 4; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "trivia";
    public override string Name => "Trivia";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _done ? 10 * _correct + 2 * _bestStreak : 0;
    public override string? ResultText => _done ? $"{_correct}/{QuestionsPerRun} correct." : null;

    public override string StatusText
    {
        get
        {
            if (_done) return $"Finished — {_correct}/{QuestionsPerRun} correct.";
            if (_answered) return "Tap any answer to continue.";
            return $"Q{_place + 1}/{QuestionsPerRun} · {_correct} right · streak {_streak}";
        }
    }

    public override void Reset()
    {
        _run.Clear();
        foreach (var i in Enumerable.Range(0, Bank.Length)
                     .OrderBy(_ => Random.Shared.Next()).Take(QuestionsPerRun))
            _run.Add(i);
        _place = 0;
        _correct = 0;
        _streak = 0;
        _bestStreak = 0;
        _answered = false;
        _done = false;
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
        int option = Array.IndexOf(OptionCells, index);
        if (option < 0) return false;

        if (_answered)
        {
            Advance();
            return true;
        }

        var q = Bank[_run[_place]];
        _answered = true;
        _chosen = option;
        if (option == q.Correct)
        {
            _correct++;
            _streak++;
            _bestStreak = Math.Max(_bestStreak, _streak);
        }
        else
        {
            _streak = 0;
        }
        Redraw();
        return true;
    }

    private void Advance()
    {
        _place++;
        _answered = false;
        if (_place >= QuestionsPerRun)
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
            SpreadRow(0, $"Done — {_correct}/{QuestionsPerRun} correct", Cols, 14, 4);
            return;
        }

        var q = Bank[_run[_place]];
        SpreadRow(0, q.Prompt, Cols, 16, 5);
        for (int i = 0; i < Cols; i++) _cells[i].Bold = true;

        var options = new[] { q.A, q.B, q.C, q.D };
        for (int i = 0; i < 4; i++)
        {
            var cell = _cells[OptionCells[i]];
            cell.Text = options[i];
            cell.FontSize = 12;
            if (_answered)
            {
                if (i == q.Correct)
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

        if (_answered) SpreadRow(Cols * 3, q.Fact, Cols, 11, 6);
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
