using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Spelling Bee, ported from the official client — make words of four or more letters
/// from the seven letters shown. Every word must use the centre letter and letters may
/// repeat; finding a pangram (all seven letters) wins on the spot.
/// </summary>
public sealed class SpellingBeeGame : GameBase
{
    private const int Cols = 10;
    private const int EndCell = 38;        // row 3, col 8
    private const int WordBase = 43;       // row 4, cols 3..6
    private const int DelCell = 53;        // row 5, col 3
    private const int OkCell = 55;         // row 5, col 5
    private const int MaxWord = 12;

    // Ring positions then the centre; indexes match _letters below.
    private static readonly int[] LetterCells = { 13, 14, 15, 23, 25, 34, 24 };

    // Seven-distinct-letter seed words; each reset picks one and re-derives the puzzle.
    // Every seed must have exactly seven different letters or the honeycomb breaks.
    private static readonly string[] Seeds =
    {
        "PLANETS", "MARKING", "CHAPTER", "PROBLEM", "GUITARS", "WANDERS",
        "SOLDIER", "CHANTED", "ROAMING", "BLAZERS", "FARMING", "MINDSET",
    };

    // Curated common words (3-9 letters). The valid list for a puzzle is the subset
    // that uses only its seven letters, contains the centre and is at least 4 long.
    private static readonly string[] Bank =
    {
        // PLANETS-friendly
        "PLANETS","PLAN","PLANS","PLANT","PLANTS","PLATE","PLATES","PLAIN","PLAINS",
        "PLEA","PLEAS","PLEAT","PLEATS","LEAP","LEAPS","PALE","TAPE","TAPES","PANS",
        "SNAP","SNAPS","SPAN","PANT","PANTS","ANTS","SALT","LATE","STEAL","SLATE",
        "SLEPT","SPENT","STALE","TALE","TALES","SALE","SALES","LANE","LANES","LEAN",
        "SEAL","PETAL","STAPLE","LAPSE","SLANT","SPLIT","ASPEN","SPELL","PEELS","LEST",
        "TELLS","SELLS","PEAS","ALES","SEA","SET","NET","TEN","LET","PET","SAP","LAP",
        "LAPS","PAL","NAP","NAPS","PAT","PATS","ANT","TAN","PAN",
        // MARKING-friendly
        "MARKING","MARK","RANK","RING","GRIN","MAIN","RAIN","GAIN","MINI","KARMA",
        "GRAM","MANIA","MARINA","ARMING","RANKING","MARGIN","KING","KINGS","RANG",
        "GANG","RAGING","GRINNING","ARM","RIM","KIN","AIR","RAM","MAN","AIM",
        // CHAPTER-friendly
        "CHAPTER","HEAT","TEAR","RATE","PACT","PEACH","CHEAP","CHEAT","REACH","TEACH",
        "PARCH","CATCH","PEAT","CARP","ARCH","RACE","PACE","CAPER","PEER","HATE",
        "HARE","RARE","TEETH","TREAT","REACT","CREPT","EAT","TEA","CAT","HAT","RAT",
        "ERA","ACE",
        // PROBLEM-friendly
        "PROBLEM","PROBE","ROPE","POLE","ROLE","MORE","LORE","BORE","MOLE","POEM",
        "PORE","LOBE","ROBE","BLOB","BELL","POLL","ROLL","BROOM","ROOM","LOOM",
        "MOREL","PROPEL","BLOOM","MEMBER","BOOM","POOR","ORE","ELM","MOB","ROB",
        // GUITARS-friendly
        "GUITARS","GUITAR","STAR","STARS","RATS","ARTS","GRIT","GUST","RUST","SUIT",
        "STIR","RAGS","SAGA","GAIT","GAITS","AURA","TRAIT","SARI","STAIR","SUGAR",
        "ARTIST","TRAITS","TAUT","STRUT","GRIST","ART","RUG","SIT","GAS","TAG",
        // WANDERS-friendly
        "WANDERS","WANDER","SAND","WARD","DARE","DEAR","READ","DEAN","SANE","WANE",
        "WARE","WARES","SANDER","SNARE","EARN","NEAR","DARN","WARNS","WRENS","DRAWN",
        "DAWN","END","SEA","RED","EAR","AND","RAW","WAR","DEN","SAD",
        // SOLDIER-friendly
        "SOLDIER","SOLD","SOLE","DOSE","LOSE","LOSS","DOLE","LIDS","SLID","SIDE",
        "DIES","IDES","LIED","ODDS","DODO","SOIL","OILS","DOERS","SOLID","LORD",
        "RODE","RODS","DOOR","OLDIE","SOLO","DOLL","OIL",
        // CHANTED-friendly
        "CHANTED","CHANT","CANE","ACHE","CHAT","THAT","THAN","THEN","DEAD","DEED",
        "HEAD","EATEN","TENT","DENT","TEND","CANTEEN","ATTEND","DANCED","ANTEDATE",
        "ADD","DECADE",
        // ROAMING-friendly
        "ROAMING","MORNING","IRON","MOON","ROAM","GROOM","MINOR","NORM","MOOR",
        "ONION","MORN","MOAN","MANOR","MAIN","RAIN","GAIN","GRAM",
        // BLAZERS-friendly
        "BLAZERS","BLAZER","BLAZE","LAZE","ZEAL","BALE","BEAR","BEARS","EARL","REAL",
        "BLARE","ERASE","SABLE","ZEBRA","SEALER","LABS","BAZAAR","AREA","EASE",
        // FARMING-friendly
        "FARMING","FARM","FAIR","AFAR","GRAIN","AIRING","FAIRING","FIG","FIN","FIR",
        "FAN",
        // MINDSET-friendly
        "MINDSET","MIDST","MINT","DENIM","TIMID","STINT","MIND","MEND",
        "SEND","SENT","TEND","TENT","DENT",
        // General common words
        "TIME","MITE","ITEM","TIDE","EDIT","DIET","DINE","NINE","MINE","TINE","TINT",
        "NOTE","TONE","TONS","ONSET","NEST","SENT","NETS","REST","TEST","BEST","MEND",
        "SEND","SING","RISE","RISK","ROAD","ROAR","ROCK","ROOF","ROOT","ROSE","RUDE",
        "RULE","RUSH","SAFE","SAID","SAIL","SAVE","SEAT","SEED","SEEK","SEEM","SEEN",
        "SELF","SHIP","SHOP","SHOT","SHOW","SHUT","SIGN","SITE","SIZE","SKIN","SLIP",
        "SLOW","SNOW","SOFT","SONG","SOON","SORT","SOUL","SOUP","SPIN","SPOT","STAY",
        "STEP","STOP","SUCH","SURE","SWIM","TAKE","TEAM","TERM","THEM","THIN","THIS",
        "TIED","TINY","TOLD","TOOL","TOUR","TOWN","TREE","TRIP","TRUE","TUNE","TURN",
        "TWIN","TYPE","UNIT","UPON","USED","VAST","VERY","VIEW","VOTE","WAIT","WAKE",
        "WALL","WANT","WASH","WAVE","WEAR","WEEK","WELL","WENT","WERE","WEST","WHAT",
        "WHEN","WIDE","WIFE","WILD","WIND","WING","WINK","WIRE","WISE","WISH","WITH",
        "WOLF","WOOD","WOOL","WORD","WORE","WORK","WORM",
    };

    private readonly List<GameCell> _cells = new();
    private readonly char[] _letters = new char[7];   // 0..5 ring, 6 = centre
    private readonly HashSet<string> _valid = new(StringComparer.Ordinal);
    private readonly HashSet<string> _found = new(StringComparer.Ordinal);
    private readonly List<string> _recent = new();
    private string _current = "";
    private int _score;
    private bool _over;
    private string _result = "";
    private string _message = "";

    public SpellingBeeGame()
    {
        for (int i = 0; i < Cols * 6; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "spellingbee";
    public override string Name => "Spelling Bee";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _score;
    public override string? ResultText => _over ? _result : null;

    public override string StatusText
    {
        get
        {
            if (_over) return _result;
            if (!string.IsNullOrEmpty(_message)) return _message;
            return $"Words {_found.Count} · {_score} pts · every word needs {_letters[6]}";
        }
    }

    public override void Reset()
    {
        var seed = Seeds[Random.Shared.Next(Seeds.Length)].ToCharArray();
        var letters = seed.Distinct().ToArray();

        // Pick the centre that keeps the most playable words on the board.
        char centre = letters[0];
        int best = -1;
        foreach (var c in letters)
        {
            int n = Bank.Count(w => w.Length >= 4 && w.Contains(c) && w.All(ch => letters.Contains(ch)));
            if (n > best)
            {
                best = n;
                centre = c;
            }
        }

        _letters[6] = centre;
        int ring = 0;
        foreach (var c in letters)
            if (c != centre) _letters[ring++] = c;

        _valid.Clear();
        foreach (var w in Bank)
            if (w.Length >= 4 && w.Contains(centre) && w.All(ch => letters.Contains(ch)))
                _valid.Add(w);

        _found.Clear();
        _recent.Clear();
        _current = "";
        _score = 0;
        _over = false;
        _result = "";
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
        if (_over) return false;
        letter = char.ToUpperInvariant(letter);
        if (Array.IndexOf(_letters, letter) < 0) return false;
        if (_current.Length >= MaxWord) return true;
        _current += letter;
        _message = "";
        Redraw();
        return true;
    }

    public override bool OnBackspace()
    {
        if (_over || _current.Length == 0) return false;
        _current = _current[..^1];
        _message = "";
        Redraw();
        return true;
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;
        if (index == OkCell) return Submit();
        if (index == DelCell) return OnBackspace();
        if (index == EndCell)
        {
            if (_found.Count < 5)
            {
                _message = "Find at least five words first.";
                Redraw();
                return true;
            }
            _over = true;
            _result = $"{_found.Count} words — {_score} points.";
            ScoreCounts = true;
            Redraw();
            return true;
        }

        int at = Array.IndexOf(LetterCells, index);
        return at >= 0 && OnLetter(_letters[at]);
    }

    private bool Submit()
    {
        if (_over) return false;
        string word = _current;
        if (word.Length == 0) return true;
        char centre = _letters[6];

        if (word.Length < 4) _message = "Too short — four letters minimum.";
        else if (word.IndexOf(centre) < 0) _message = $"Every word needs the centre {centre}.";
        else if (_found.Contains(word)) _message = "Already found.";
        else if (!_valid.Contains(word)) _message = "Not in our word list.";
        else
        {
            _found.Add(word);
            _recent.Add(word);
            if (_recent.Count > 8) _recent.RemoveAt(0);
            _score += word.Length - 3;
            if (word.Distinct().Count() == 7)
            {
                // A pangram uses all seven letters: bonus points and instant victory.
                _score += 7;
                _over = true;
                _result = $"Pangram {word}! {_found.Count} words, {_score} points.";
                ScoreCounts = true;
                Redraw();
                return true;
            }
            _message = $"Nice — {word} is worth {word.Length - 3}.";
        }
        _current = "";
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
            c.FontSize = 14;
            c.Span = 1;
        }

        SpreadRow(0, string.Join(" ", _recent), Cols, 11, 5);
        _cells[0].Foreground = Muted;

        // Honeycomb: three ring letters, then ring-centre-ring, then the last ring
        // letter below — a hex-ish shape inside the square grid.
        for (int i = 0; i < 7; i++)
        {
            var cell = _cells[LetterCells[i]];
            cell.Text = _letters[i].ToString();
            cell.FontSize = i == 6 ? 24 : 20;
            if (i == 6)
            {
                cell.Background = Warn;
                cell.Foreground = Cell;
            }
            else
            {
                cell.Background = Cell;
                cell.Foreground = Ink;
            }
            cell.Bold = true;
            cell.IsEnabled = !_over;
        }

        SpreadRow(WordBase, _current, 4, 16, 3);
        _cells[WordBase].Foreground = Ink;
        for (int i = 0; i < 4; i++) _cells[WordBase + i].Bold = true;

        if (_over) return;
        var end = _cells[EndCell];
        end.Text = "END";
        end.Background = Cell;
        end.FontSize = 11;
        end.IsEnabled = true;
        var del = _cells[DelCell];
        del.Text = "✕";
        del.Background = Cell;
        del.FontSize = 16;
        del.IsEnabled = true;
        var ok = _cells[OkCell];
        ok.Text = "✔";
        ok.Background = Cell;
        ok.FontSize = 18;
        ok.Bold = true;
        ok.IsEnabled = true;
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
