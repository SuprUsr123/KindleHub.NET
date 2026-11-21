using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Word Ladder, ported from the official client — change one letter at a time to get
/// from the first word to the last, and every step has to be a real word. Type the
/// next word (or use the on-screen keyboard) and submit; the ladder's known optimum
/// is the par.
/// </summary>
public sealed class WordLadderGame : GameBase
{
    private const int Cols = 10;
    private const int HistoryRows = 8;
    private const int InputRow = 9;
    private const int KeyBase = 100;
    private const int EnterCell = KeyBase + 20;
    private const int BackspaceCell = KeyBase + 29;

    private const string KeyboardRow1 = "QWERTYUIOP";
    private const string KeyboardRow2 = "ASDFGHJKL";
    private const string KeyboardRow3 = "ZXCVBNM";

    // Ladders with verified paths: each step changes exactly one letter and every
    // intermediate is a real four-letter word. Par counts every move, which is one
    // more than the rungs because the target word is the final move.
    private static readonly (string Start, string Target, string[] Path)[] Ladders =
    {
        ("COLD", "WARM", new[] { "CORD", "WORD", "WARD" }),
        ("HEAD", "TAIL", new[] { "HEAL", "TEAL", "TELL", "TALL" }),
        ("LOVE", "HATE", new[] { "DOVE", "DOTE", "DATE" }),
        ("WINE", "BEER", new[] { "WANE", "BANE", "BAND", "BEND", "BENT", "BEET" }),
        ("SICK", "WELL", new[] { "SILK", "SILL", "WILL" }),
        ("RIDE", "WALK", new[] { "TIDE", "TILE", "TALE", "TALK" }),
        ("FOUR", "FIVE", new[] { "FOUL", "FOIL", "FILL", "FILE" }),
    };

    // Enough common four-letter words that alternate routes validate, on top of the
    // shared Wordle list (which the ladder words need for their own steps).
    private static readonly string[] FourLetterWords =
    {
        "COLD","CORD","WORD","WARD","WARM","HEAD","HEAL","TEAL","TELL","TALL","TAIL",
        "PLAY","PLAT","PEAT","PEAL","GALE","GAME","WINE","WANE","BANE","BAND","BEND",
        "BENT","BEET","BEER","SICK","SILK","SILL","WILL","WELL","RIDE","TIDE","TILE",
        "TALE","TALK","WALK","LOVE","DOVE","DOTE","DATE","HATE","FOUR","FOUL","FOIL",
        "FILL","FILE","FIVE",
        "ABLE","AREA","AWAY","BABY","BACK","BALL","BANK","BASE","BEAR","BEAT","BEEN",
        "BELL","BELT","BEST","BILL","BIRD","BITE","BLUE","BOAT","BODY","BOLD","BONE",
        "BOOK","BOOT","BORN","BOTH","BOWL","BULK","BURN","BUSH","BUSY","CAKE","CALL",
        "CALM","CAME","CAMP","CARD","CARE","CASE","CASH","CAST","CELL","CHAT","CHIN",
        "CITY","CLAP","CLAW","CLAY","CLIP","CLUB","COAL","COAT","CODE","COIN","COME",
        "COOK","COOL","COPY","CORE","CORN","COST","CREW","CROP","CURE","CURL","DARE",
        "DARK","DATA","DATE","DAWN","DEAD","DEAF","DEAL","DEAR","DEBT","DEEP","DESK",
        "DIAL","DINE","DIRT","DISH","DOES","DONE","DOOR","DOSE","DOWN","DRAG","DRAW",
        "DREW","DROP","DRUM","DUAL","DUKE","DULL","DUST","DUTY","EACH","EARL","EARN",
        "EASE","EAST","EASY","EDGE","ELSE","EVEN","EVER","EXIT","FACE","FACT","FAIL",
        "FAIR","FALL","FARM","FAST","FATE","FEAR","FEED","FEEL","FEET","FELL","FELT",
        "FILE","FILL","FILM","FIND","FINE","FIRE","FIRM","FISH","FIST","FIVE","FLAG",
        "FLAT","FLEW","FLIP","FLOW","FOAM","FOLD","FOLK","FOOD","FOOT","FORM","FORT",
        "FOUR","FREE","FROG","FROM","FUEL","FULL","FUND","GAIN","GATE","GAVE","GEAR",
        "GIFT","GIRL","GIVE","GLAD","GLOW","GOAL","GOAT","GOES","GOLD","GOLF","GONE",
        "GOOD","GRAB","GRAY","GREW","GRIM","GRIP","GROW","HAIR","HALF","HALL","HAND",
        "HANG","HARD","HARM","HATE","HAVE","HEAR","HEAT","HELD","HELP","HEMP","HERE",
        "HERO","HIDE","HIGH","HIKE","HILL","HINT","HOLD","HOLE","HOME","HOPE","HORN",
        "HOST","HOUR","HUGE","HURT","IDEA","INTO","IRON","ITEM","JOIN","JOKE","JUMP",
        "JUST","KEEN","KEEP","KIND","KING","KNEE","KNEW","KNOW","LACK","LAID","LAKE",
        "LAND","LANE","LAST","LATE","LAZY","LEAD","LEAF","LEAN","LEFT","LEND","LIFE",
        "LIFT","LIKE","LIME","LINE","LION","LIST","LIVE","LOAD","LOAN","LOCK","LONG",
        "LOOK","LORD","LOSE","LOSS","LOST","LOUD","LOVE","LUCK","MADE","MAIL","MAIN",
        "MAKE","MALL","MANY","MARK","MASK","MASS","MATH","MEAL","MEAN","MEAT","MELT",
        "MENU","MESS","MILD","MILE","MILK","MIND","MINE","MISS","MODE","MOOD","MOON",
        "MORE","MOST","MOVE","MUCH","MUST","NAME","NAVY","NEAR","NEAT","NECK","NEED",
        "NEST","NEWS","NEXT","NICE","NINE","NONE","NOON","NOSE","NOTE","ONCE","ONLY",
        "OPEN","OVAL","OVEN","OVER","PACE","PACK","PAGE","PAID","PAIN","PAIR","PALE",
        "PALM","PARK","PART","PASS","PAST","PATH","PEAK","PICK","PINE","PINK","PIPE",
        "PLAN","PLUS","POOL","POOR","PORT","POST","POUR","PRAY","PULL","PURE","PUSH",
        "QUIT","RACE","RACK","RAIN","RANK","RARE","RATE","READ","REAL","REAR","REST",
        "RICH","RISE","RISK","ROAD","ROAR","ROCK","ROLE","ROLL","ROOF","ROOT","ROPE",
        "ROSE","RULE","RUSH","SAFE","SAID","SAIL","SALE","SALT","SAME","SAND","SAVE",
        "SEAT","SEED","SEEK","SEEM","SEEN","SELF","SELL","SEND","SENT","SHIP","SHOP",
        "SHOT","SHOW","SHUT","SIDE","SIGN","SING","SITE","SIZE","SKIN","SLIP","SLOW",
        "SNOW","SOFT","SOIL","SOLD","SOLE","SONG","SOON","SORT","SOUL","SOUP","SPIN",
        "SPOT","STAR","STAY","STEP","STOP","SUCH","SUIT","SURE","SWIM","TAKE","TEAM",
        "TERM","THEM","THEN","THIN","THIS","TIED","TIME","TINY","TOLD","TONE","TOOK",
        "TOOL","TOUR","TOWN","TREE","TRIP","TRUE","TUNE","TURN","TWIN","TYPE","UNIT",
        "UPON","USED","VAST","VERY","VIEW","VOTE","WAIT","WAKE","WALL","WANT","WASH",
        "WAVE","WEAR","WEEK","WENT","WERE","WEST","WHAT","WHEN","WIDE","WIFE","WILD",
        "WIND","WING","WINK","WIRE","WISE","WISH","WITH","WOLF","WOOD","WOOL","WORE",
        "WORK","WORM","WRAP","YARD","YEAR","ZERO","ZONE",
    };

    private static readonly HashSet<string> Dictionary = BuildDictionary();

    private readonly List<GameCell> _cells = new();
    private readonly List<string> _history = new();
    private readonly char[] _input = new char[4];
    private string _start = "";
    private string _target = "";
    private int _par;
    private int _moves;
    private int _col;                 // cursor into _input, Wordle-style
    private bool _won;
    private string _message = "";

    public WordLadderGame()
    {
        for (int i = 0; i < Cols * 13; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "wordladder";
    public override string Name => "Word Ladder";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _won ? Math.Max(20, 60 * _par - 10 * (_moves - _par) + 40) : 0;
    public override string? ResultText => _won
        ? $"{_start} to {_target} in {_moves} moves (par {_par})."
        : null;

    public override string StatusText
    {
        get
        {
            if (_won) return $"Solved in {_moves} moves — par was {_par}.";
            if (!string.IsNullOrEmpty(_message)) return _message;
            return $"{_start} → {_target} · par {_par} · move {_moves} · change one letter";
        }
    }

    private static HashSet<string> BuildDictionary()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in FourLetterWords) set.Add(w);
        // The shared five-letter list costs nothing to include and widens the vocab.
        foreach (var w in WordleWordList.Words) set.Add(w);
        return set;
    }

    public override void Reset()
    {
        var ladder = Ladders[Random.Shared.Next(Ladders.Length)];
        _start = ladder.Start;
        _target = ladder.Target;
        _par = ladder.Path.Length + 1;   // the target word is the last move
        _history.Clear();
        Array.Clear(_input, 0, _input.Length);
        _moves = 0;
        _col = 0;
        _won = false;
        _message = "";
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (key == GameKey.Confirm) return Submit();
        if (key == GameKey.Backspace) return OnBackspace();
        return base.OnKey(key);
    }

    public override bool OnLetter(char letter)
    {
        if (_won || _col >= 4) return false;
        letter = char.ToUpperInvariant(letter);
        if (letter < 'A' || letter > 'Z') return false;
        _input[_col++] = letter;
        _message = "";
        Redraw();
        return true;
    }

    public override bool OnBackspace()
    {
        if (_won || _col <= 0) return false;
        _col--;
        _input[_col] = '\0';
        _message = "";
        Redraw();
        return true;
    }

    public override bool OnTap(int index)
    {
        if (_won || index < 0 || index >= _cells.Count) return false;

        if (index is >= 93 and <= 96)
        {
            // Tapping the input row moves the cursor, like Wordle's board.
            _col = Math.Clamp(index - 93 + 1, 0, 4);
            Redraw();
            return true;
        }
        if (index == EnterCell) return Submit();
        if (index == BackspaceCell) return OnBackspace();

        char key = KeyAt(index);
        return key != '\0' && OnLetter(key);
    }

    private static char KeyAt(int index)
    {
        int k = index - KeyBase;
        if (k >= 0 && k < Cols) return KeyboardRow1[k];
        if (k >= 10 && k < 10 + KeyboardRow2.Length) return KeyboardRow2[k - 10];
        if (k >= 21 && k < 21 + KeyboardRow3.Length) return KeyboardRow3[k - 21];
        return '\0';
    }

    private bool Submit()
    {
        if (_won) return false;
        if (_input[0] == '\0' || _input[1] == '\0' || _input[2] == '\0' || _input[3] == '\0')
        {
            _message = "Four letters, please.";
            Redraw();
            return true;
        }

        string guess = new(_input, 0, 4);
        string prev = _history.Count == 0 ? _start : _history[^1];
        int diff = Enumerable.Range(0, 4).Count(i => guess[i] != prev[i]);
        if (diff == 0) _message = "That is where you already are.";
        else if (diff != 1) _message = "Change exactly one letter.";
        else if (!Dictionary.Contains(guess)) _message = $"{guess} is not in the word list.";
        else
        {
            _history.Add(guess);
            _moves++;
            _col = 0;
            Array.Clear(_input, 0, _input.Length);
            _message = "";
            if (guess == _target)
            {
                _won = true;
                ScoreCounts = true;
            }
        }
        Redraw();
        return true;
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
            c.FontSize = 15;
            c.Span = 1;
        }

        // Header: the ladder's two ends as letter tiles with an arrow between.
        for (int i = 0; i < 4; i++)
        {
            var cell = _cells[1 + i];
            cell.Text = _start[i].ToString();
            cell.Background = Cell;
            cell.FontSize = 20;
            cell.Bold = true;
        }
        _cells[5].Text = "→";
        _cells[5].FontSize = 18;
        _cells[5].Foreground = Muted;
        for (int i = 0; i < 4; i++)
        {
            var cell = _cells[6 + i];
            cell.Text = _target[i].ToString();
            cell.Background = _won ? Good : Cell;
            cell.Foreground = _won ? Cell : Ink;
            cell.FontSize = 20;
            cell.Bold = true;
        }

        // History window: the last eight accepted words, newest at the bottom.
        int shown = Math.Max(0, _history.Count - HistoryRows);
        for (int h = 0; h < HistoryRows; h++)
        {
            int index = shown + h;
            if (index >= _history.Count) break;
            string word = _history[index];
            string prev = index > 0 ? _history[index - 1] : _start;
            for (int i = 0; i < 4; i++)
            {
                var cell = _cells[(1 + h) * Cols + 3 + i];
                cell.Text = word[i].ToString();
                cell.FontSize = 18;
                cell.Bold = true;
                if (_won && index == _history.Count - 1)
                {
                    cell.Background = Good;
                    cell.Foreground = Cell;
                }
                else if (word[i] != prev[i])
                {
                    // Highlight the one letter that changed — the ladder's whole idea.
                    cell.Background = Accent;
                    cell.Foreground = Cell;
                }
                else
                {
                    cell.Background = Cell;
                }
            }
        }

        if (_won) return;

        for (int i = 0; i < 4; i++)
        {
            var cell = _cells[InputRow * Cols + 3 + i];
            if (_input[i] != '\0')
            {
                cell.Text = _input[i].ToString();
                cell.Background = Cell;
                cell.FontSize = 20;
                cell.Bold = true;
            }
            else
            {
                cell.Background = Board;
                cell.IsEnabled = true;
            }
        }

        // QWERTY keyboard in the Wordle shape: 10 / 9 / 7 keys, enter and delete flanks.
        for (int col = 0; col < Cols; col++)
        {
            DrawKey(KeyBase + col, KeyboardRow1[col]);
            if (col < KeyboardRow2.Length) DrawKey(KeyBase + Cols + col, KeyboardRow2[col]);
            int bottom = KeyBase + Cols * 2 + col;
            if (col == 0 || col == Cols - 1)
            {
                var cell = _cells[bottom];
                cell.Text = col == 0 ? "↵" : "✕";
                cell.FontSize = 16;
                cell.Background = Cell;
                cell.IsEnabled = true;
            }
            else if (col >= 1 && col <= KeyboardRow3.Length)
            {
                DrawKey(bottom, KeyboardRow3[col - 1]);
            }
        }
    }

    private void DrawKey(int index, char key)
    {
        var cell = _cells[index];
        cell.Text = key.ToString();
        cell.FontSize = 14;
        cell.Background = Cell;
        cell.IsEnabled = true;
    }
}
