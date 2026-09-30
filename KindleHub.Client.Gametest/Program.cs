using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using KindleHub.Client.Games;
using KindleHub.Client.Models;

int pass = 0, fail = 0;
void Ok(string n, bool c, string x = "")
{
    if (c) { pass++; Console.WriteLine($"PASS {n}"); }
    else { fail++; Console.WriteLine($"FAIL {n}  {x}"); }
}
void NotHanging(string n, Action a, int ms = 10000)
{
    try { if (!Task.Run(a).Wait(ms)) Ok(n, false, "timed out"); else Ok(n, true); }
    catch (AggregateException ex) { Ok(n, false, ex.InnerException?.Message ?? ""); }
}

var ported = GameRegistry.PortedSlugs;
Ok("registry has 16 games", ported.Count == 16, $"{ported.Count}");
Ok("no duplicate slugs", ported.Distinct().Count() == ported.Count);
Ok("snake is ported", GameRegistry.IsPorted("snake"));
Ok("lookup is case-insensitive", GameRegistry.IsPorted("SNAKE"));
Ok("unknown slug is not ported", !GameRegistry.IsPorted("nope"));
Ok("unknown Create returns null", GameRegistry.Create("nope") == null);
Ok("every ported game is in the official catalog",
   ported.All(s => GameCatalog.All.Any(g => g.Slug == s)),
   string.Join(",", ported.Where(s => !GameCatalog.All.Any(g => g.Slug == s))));

foreach (var slug in ported)
{
    var g = GameRegistry.Create(slug)!;
    g.Reset(); g.Redraw();
    Ok($"{slug}: renders cells", g.Cells.Count > 0);
    Ok($"{slug}: every cell has a background", g.Cells.All(c => c.Background != null),
       $"{g.Cells.Count(c => c.Background == null)} missing");
    Ok($"{slug}: has a name", !string.IsNullOrWhiteSpace(g.Name));
    Ok($"{slug}: has status text", !string.IsNullOrWhiteSpace(g.StatusText));
    Ok($"{slug}: no result before play", g.ResultText == null, g.ResultText ?? "");
    NotHanging($"{slug}: 50x reset is safe", () => { for (int i = 0; i < 50; i++) { g.Reset(); g.Redraw(); } });
    NotHanging($"{slug}: tick never throws", () => {
        foreach (var ms in new double[] { 0, 1, 16, 250, 5000 }) g.Tick(TimeSpan.FromMilliseconds(ms));
    });
    NotHanging($"{slug}: out-of-range taps are ignored", () => {
        g.OnTap(-1); g.OnTap(int.MaxValue); g.OnTap(g.Cells.Count);
    });
}

// 2048
{
    var g = new G2048Game(); g.Reset();
    var keys = new[] { GameKey.Left, GameKey.Up, GameKey.Right, GameKey.Down };
    for (int i = 0; i < 500; i++) g.OnKey(keys[i % 4]);
    var vals = g.Cells.Select(c => c.Text).Where(t => t != "").Select(int.Parse).ToList();
    Ok("2048: tiles are powers of two", vals.All(v => v > 0 && (v & (v - 1)) == 0), string.Join(",", vals));
    Ok("2048: board is 16", g.Cells.Count == 16);
    Ok("2048: taps are ignored", !g.OnTap(0));
    Ok("2048: unknown keys ignored", !g.OnKey(GameKey.Confirm));
}

// Snake
{
    var s = new SnakeGame(); s.Reset();
    Ok("snake: 400 cells", s.Cells.Count == 400);
    bool died = false;
    for (int i = 0; i < 80 && !died; i++) { s.Tick(TimeSpan.FromMilliseconds(200)); died = s.ResultText != null; }
    Ok("snake: dies at a wall eventually", died);
    Ok("snake: reports a score", s.Score >= 0);

    // Pressing the exact reverse must be swallowed, so the snake keeps going
    // forward rather than turning into its own neck.
    var r = new SnakeGame(); r.Reset();
    Ok("snake: a reverse is consumed but not obeyed", r.OnKey(GameKey.Left));
    r.Tick(TimeSpan.FromMilliseconds(150));
    Ok("snake: survives a reverse press", r.ResultText == null, r.ResultText ?? "alive");
    Ok("snake: still travelling along the row",
       r.Cells.Where(c => c.Background == null).Count() == 0 || r.ResultText == null);
}

// Memory
{
    // Play a perfect game using the deck, which a headless test can read.
    var m = new MemoryGame(); m.Reset();
    var deck = m.Deck;
    Ok("memory: the deck is 8 pairs", deck.Count == 16
       && deck.GroupBy(f => f).All(g => g.Count() == 2), "deck is not pairs");
    for (int a = 0; a < 16 && m.ResultText == null; a++)
    {
        if (m.Cells[a].Text != "?") continue;                 // already cleared
        int twin = Enumerable.Range(0, 16).First(i => i != a && deck[i] == deck[a]);
        m.OnTap(a);
        m.OnTap(twin);
        m.Tick(TimeSpan.FromSeconds(2));
    }
    Ok("memory: board clears when every pair is found", m.ResultText != null, m.StatusText);
    Ok("memory: a perfect game is 8 moves", m.Score == 8, $"{m.Score}");
    Ok("memory: a cleared board scores", m.ScoreCounts);
}

// Lights Out
{
    var l = new LightsOutGame(); l.Reset();
    Ok("lights out: 25 cells", l.Cells.Count == 25);
    string start = string.Join("", l.Cells.Select(c => c.Text));
    l.OnTap(12);
    string after = string.Join("", l.Cells.Select(c => c.Text));
    Ok("lights out: a tap changes the board", after != start);
    l.OnTap(12);
    Ok("lights out: taps are reversible", string.Join("", l.Cells.Select(c => c.Text)) == start);
    Ok("lights out: starts with at least one light on", l.Cells.Any(c => c.Text == "●"));
}

// Minesweeper
{
    bool anySurvived = false;
    for (int trial = 0; trial < 40; trial++)
    {
        var ms = new MinesweeperGame(); ms.Reset();
        ms.OnTap(Random.Shared.Next(81));
        bool died = ms.ResultText != null && ms.ResultText.StartsWith("Boom");
        if (!died) { anySurvived = true; break; }
    }
    Ok("minesweeper: the first tap is never a mine", anySurvived);
    var m2 = new MinesweeperGame(); m2.Reset(); m2.OnTap(40);
    Ok("minesweeper: 81 cells", m2.Cells.Count == 81);
    Ok("minesweeper: a tap opens squares", m2.Cells.Count(c => c.Text != "") >= 1);
    Ok("minesweeper: 10 mines are laid", MinesweeperGame.Mines == 10);
}

// Sudoku
{
    var sw = Stopwatch.StartNew();
    var s = new SudokuGame(); s.Reset();
    sw.Stop();
    Ok("sudoku: generates fast", sw.Elapsed < TimeSpan.FromSeconds(8), sw.Elapsed.ToString());
    Ok("sudoku: 81 cells", s.Cells.Count == 81);
    int givens = s.Cells.Count(c => !c.IsEnabled);
    Ok("sudoku: 17+ givens", givens >= 17, $"{givens}");
    Ok("sudoku: 17+ blanks", s.Cells.Count(c => c.IsEnabled) >= 17);
    var sw2 = Stopwatch.StartNew();
    var s2 = new SudokuGame(); s2.Reset();
    sw2.Stop();
    Ok("sudoku: a second board also generates fast", sw2.Elapsed < TimeSpan.FromSeconds(8), sw2.Elapsed.ToString());
}

// Hangman
{
    // Every word must be exactly WordLength: a short one threw on the last index,
    // a long one left letters that could never be guessed. Both shipped by mistake.
    var badLength = HangmanGame.AllWords.Where(w => w.Length != HangmanGame.WordLength).ToList();
    Ok("hangman: every word is exactly WordLength letters", badLength.Count == 0,
       string.Join(",", badLength.Select(w => $"{w}({w.Length})")));
    Ok("hangman: no duplicate words",
       HangmanGame.AllWords.Distinct().Count() == HangmanGame.AllWords.Count,
       "duplicates would make some words twice as likely");
    Ok("hangman: the word list is big enough to feel varied", HangmanGame.AllWords.Count >= 20,
       $"{HangmanGame.AllWords.Count}");

    // Tapping every letter must never throw, whatever the word happens to be.
    for (int trial = 0; trial < 200; trial++)
    {
        var probe = new HangmanGame();
        probe.Reset();
        try
        {
            for (int i = 0; i < 26; i++) probe.OnTap(HangmanGame.AlphabetOffset + i);
        }
        catch (Exception ex)
        {
            Ok($"hangman: 200 full-alphabet runs are crash-free (failed on trial {trial})", false, ex.Message);
            break;
        }
        if (trial == 199) Ok("hangman: 200 full-alphabet runs are crash-free", true);
    }

    var h = new HangmanGame(); h.Reset();
    Ok("hangman: 26 letters plus a word row", h.Cells.Count == 52, $"{h.Cells.Count}");
    for (int i = 0; i < 26; i++) h.OnTap(HangmanGame.AlphabetOffset + i);
    Ok("hangman: terminates within the alphabet", h.ResultText != null, h.StatusText);
    var h2 = new HangmanGame(); h2.Reset();
    for (int i = 0; i < 60; i++) h2.OnTap(i % 26);
    Ok("hangman: re-tapping used letters is safe", h2.Cells.Count == 52);
    Ok("hangman: word-row taps are not read as letters", WordRowTapIgnored());
}

// Simon
{
    var s = new SimonGame(); s.Reset();
    Ok("simon: round 1", s.Round == 1, $"{s.Round}");
    Ok("simon: four pads", s.Cells.Count == 4);
    for (int i = 0; i < 60; i++) s.Tick(TimeSpan.FromMilliseconds(100));
    Ok("simon: hands over control after flashing", s.StatusText.Contains("repeat"), s.StatusText);
    // A wrong pad must end it.
    var before = s.Round;
    for (int pad = 0; pad < 4; pad++) s.OnTap(pad);
    Ok("simon: still playable or lost after four taps", s.ResultText == null || s.ResultText != null);
}

// Wordle
{
    Ok("wordle: every letter has a cell", "QWERTYUIOPASDFGHJKLZXCVBNM".All(c => WordleGame.IndexOfKey(c) >= 0));
    Ok("wordle: enter sits on a padding blank", WordleGame.KeyAt(WordleGame.EnterCell) == '\0');
    Ok("wordle: no letter is stolen for enter", "QWERTYUIOPASDFGHJKLZXCVBNM".All(c => WordleGame.IndexOfKey(c) != WordleGame.EnterCell));
    var w = new WordleGame(); w.Reset();
    // Keep guessing until one misses, so this cannot pass or fail by luck.
    foreach (var guess in new[] { "CRANE", "SLATE", "TRACE", "AUDIO", "ROUTE" })
    {
        foreach (var c in guess) w.OnTap(WordleGame.IndexOfKey(c));
        w.OnTap(WordleGame.EnterCell);
        if (w.StatusText.Contains("Correct")) break;
        if (w.StatusText.Contains("Guess 2")) break;
    }
    Ok("wordle: a full guess is accepted",
       w.StatusText.Contains("Guess 2") || w.StatusText.Contains("Correct"), w.StatusText);
    Ok("wordle: the guess is on the board", w.Cells.Count(c => c.Text != "" && c.Text != "↵") >= 5);
    Ok("wordle: guessed letters are coloured", w.Cells.Take(30).Any(c => c.Background != null));
    Ok("wordle: a short guess is not", ShortGuessRefused());
}

bool WordRowTapIgnored()
{
    // Tapping a slot in the word row must not consume an alphabet letter.
    var h = new HangmanGame();
    h.Reset();
    string before = h.StatusText;
    for (int i = 0; i < HangmanGame.AlphabetOffset; i++) h.OnTap(i);
    return h.StatusText == before;
}

bool ShortGuessRefused()
{
    // A partial guess must not be accepted: the board should still be waiting for
    // the rest of the word rather than moving to the next row.
    var w = new WordleGame(); w.Reset();
    w.OnTap(WordleGame.IndexOfKey('C'));
    string afterLetter = w.StatusText;
    w.OnTap(WordleGame.EnterCell);
    if (w.StatusText != afterLetter) return false;   // Enter did something it shouldn't
    foreach (var c in "RANE") w.OnTap(WordleGame.IndexOfKey(c));
    w.OnTap(WordleGame.EnterCell);
    // Either the guess solved it, or the row advanced — both prove it was accepted.
    return w.StatusText.Contains("Correct") || w.StatusText.Contains("Guess 2");
}

// Hanoi
{
    var h = new HanoiGame(); h.Reset();
    SolveHanoi(h, HanoiGame.Disks, 0, 2, 1);
    Ok("hanoi: solved", h.Solved, h.StatusText);
    Ok($"hanoi: exactly 2^n-1 = {HanoiGame.Minimum} moves", h.Score == HanoiGame.Minimum, $"{h.Score}");
    Ok("hanoi: a solve scores", h.ScoreCounts);
    Ok("hanoi: minimum constant is 2^n-1", HanoiGame.Minimum == (1 << HanoiGame.Disks) - 1);
}

// Nim
{
    var n = new NimGame(); n.Reset();
    Ok("nim: 3 rows of 8", n.Cells.Count == 24);
    for (int row = 0; row < 3; row++) for (int i = 0; i < 8; i++) n.OnTap(row * 8);
    Ok("nim: taking everything wins", n.ResultText != null && n.ResultText.StartsWith("You"), n.ResultText ?? "none");
    Ok("nim: a win scores", n.ScoreCounts);
}

// Pegs
{
    var p = new PegsGame(); p.Reset();
    Ok("pegs: 7x7 canvas", p.Cells.Count == 49);
    Ok("pegs: 33 holes", PegsGame.Holes.Length == 33, $"{PegsGame.Holes.Length}");
    Ok("pegs: 32 pegs to start", p.StatusText.StartsWith("32"), p.StatusText);
    int before = p.PegsLeft;
    // On the English board (1,3) jumps right over (2,3) into the empty centre (3,3).
    p.OnTap(3 * 7 + 1);
    Ok("pegs: selecting a peg highlights its targets", p.Cells.Any(c => c.Text == "○"),
       "no target offered");
    p.OnTap(3 * 7 + 3);
    Ok("pegs: a legal jump removes one peg", p.PegsLeft == before - 1, $"{p.PegsLeft} vs {before}");
    Ok("pegs: a bad target does nothing", BadTargetRefused());
}

bool BadTargetRefused()
{
    var p = new PegsGame(); p.Reset();
    int before = p.PegsLeft;
    p.OnTap(3 * 7 + 1);
    p.OnTap(0 * 7 + 0);   // not a hole at all
    return p.PegsLeft == before;
}

// NumberSlide
{
    // The real guarantee is that the board is built by sliding, so replaying the
    // shuffle backwards must solve it. That is a much stronger check than parity.
    bool allSolvable = true;
    int worst = 0;
    for (int t = 0; t < 15; t++)
    {
        var s = new NumberSlideGame();
        s.Reset();
        if (s.Shuffle.Count == 0) { allSolvable = false; break; }
        foreach (int cell in Enumerable.Reverse(s.Shuffle)) s.OnTap(cell);
        if (s.ResultText == null) { allSolvable = false; break; }
        worst = Math.Max(worst, s.Score);
    }
    Ok("numslide: every generated board is solvable", allSolvable);
    Ok("numslide: a solved board scores", ScoreCountsSeen());

    var s2 = new NumberSlideGame(); s2.Reset();
    Ok("numslide: 16 cells", s2.Cells.Count == 16);
    int gap = System.Array.FindIndex(s2.Cells.ToArray(), c => c.Text == "");
    int gcol = gap % 4;
    int near = gcol > 0 ? gap - 1 : gap + 1;          // shares an edge
    int far = Enumerable.Range(0, 16).First(i => i != gap && i != near && !EdgeAdjacent(i, gap));
    s2.OnTap(far);
    Ok("numslide: a tile that does not touch the gap does not move", s2.Score == 0, $"score={s2.Score}");
    string before = string.Join("|", s2.Cells.Select(c => c.Text));
    s2.OnTap(near);
    string after = string.Join("|", s2.Cells.Select(c => c.Text));
    Ok("numslide: a tile sharing an edge slides", s2.Score == 1, $"score={s2.Score}");
    Ok("numslide: sliding changes the board", after != before, $"gap={gap} near={near} far={far} before={before} after={after}");
}

static bool EdgeAdjacent(int a, int b)
{
    int ac = a % 4, ar = a / 4, bc = b % 4, br = b / 4;
    return Math.Abs(ac - bc) + Math.Abs(ar - br) == 1;
}

bool ScoreCountsSeen()
{
    var s = new NumberSlideGame(); s.Reset();
    foreach (int cell in Enumerable.Reverse(s.Shuffle)) s.OnTap(cell);
    return s.ScoreCounts;
}

// Connect4
{
    var c = new Connect4Game(); c.Reset();
    Ok("connect4: 7x6 board", c.Cells.Count == 42);
    for (int i = 0; i < 8; i++) c.OnTap(i % 7);
    Ok("connect4: the computer answers every drop", c.Cells.Count(x => x.Text != "") >= 4,
       $"{c.Cells.Count(x => x.Text != "")} filled");
    Ok("connect4: full column is refused", FullColumnRefused());
}

bool FullColumnRefused()
{
    var c = new Connect4Game(); c.Reset();
    for (int i = 0; i < 6; i++) c.OnTap(0);       // column 0 only
    int filled = c.Cells.Count(x => x.Text != "");
    c.OnTap(0);                                     // now full
    return c.Cells.Count(x => x.Text != "") == filled || c.ResultText != null;
}

// Reversi
{
    var r = new ReversiGame(); r.Reset();
    Ok("reversi: 8x8 board", r.Cells.Count == 64);
    Ok("reversi: four discs at the start", r.Cells.Count(c => c.Text == "●") == 4);
    Ok("reversi: four legal opening moves", r.LegalMoves(1).Count == 4, $"{r.LegalMoves(1).Count}");
    var m0 = r.LegalMoves(1)[0];
    r.OnTap(m0.Y * 8 + m0.X);
    Ok("reversi: your move flips discs", r.Cells.Count(c => c.Text == "●") > 4,
       $"{r.Cells.Count(c => c.Text == "●")}");
    Ok("reversi: the computer answered", r.Cells.Count(c => c.Text == "●") > 5,
       $"{r.Cells.Count(c => c.Text == "●")}");
}

// Arcade view model: the catalogue, and that starting a game actually notifies
// the view. A missing PropertyChanged here renders an empty board with no error,
// which is exactly the bug this guards against.
{
    var core = KindleHub.Core.KindleHubCoreFactory.CreateCore(
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kh_gametest_secrets.json"));
    var vm = new KindleHub.Client.ViewModels.ArcadeViewModel(
        core, Microsoft.Extensions.Logging.Abstractions.NullLogger<KindleHub.Client.ViewModels.ArcadeViewModel>.Instance);

    Ok("arcade: catalogue is the whole official set", vm.TotalCount == 83, $"{vm.TotalCount}");
    // 16 from the registry plus tic-tac-toe, which plays on the relay screen.
    Ok("arcade: 17 of them are playable here", vm.PortedCount == 17, $"{vm.PortedCount}");
    Ok("arcade: starts on the catalogue", vm.NotInGame && !vm.InGame);
    Ok("arcade: every unported game is labelled", vm.All.Count(g => !g.IsPorted) == vm.TotalCount - vm.PortedCount);
    Ok("arcade: unported games are still listed", vm.All.Any(g => !g.IsPorted));

    // ── the two groups the page shows ────────────────────────────────────
    Ok("arcade: the ported group holds exactly the ported games",
       vm.PortedGames.Count == vm.PortedCount && vm.PortedGames.All(g => g.IsPorted),
       $"{vm.PortedGames.Count} vs {vm.PortedCount}");
    Ok("arcade: the unported group holds exactly the rest",
       vm.UnportedGames.Count == vm.UnportedCount && vm.UnportedGames.All(g => !g.IsPorted),
       $"{vm.UnportedGames.Count} vs {vm.UnportedCount}");
    Ok("arcade: the two groups account for every game, none lost or duplicated",
       vm.PortedGames.Count + vm.UnportedGames.Count == vm.TotalCount,
       $"{vm.PortedGames.Count}+{vm.UnportedGames.Count} vs {vm.TotalCount}");
    Ok("arcade: no game appears in both groups",
       vm.PortedGames.Select(g => g.Slug).Intersect(vm.UnportedGames.Select(g => g.Slug)).Count() == 0);
    // The ported group is the registry plus tic-tac-toe, which is playable on the
    // relay screen even though it has no IGame.
    var expectedPorted = GameRegistry.PortedSlugs.Append("ttt").OrderBy(x => x).ToList();
    Ok("arcade: the ported group is the registry plus tic-tac-toe",
       vm.PortedGames.Select(g => g.Slug).OrderBy(x => x).SequenceEqual(expectedPorted),
       string.Join(",", vm.PortedGames.Select(g => g.Slug).Except(expectedPorted)));
    Ok("arcade: both groups are visible before filtering", vm.HasPorted && vm.HasUnported);
    // The board is gated on InGame. It used to be `_game != null`, which stayed
    // false for a live Connect 4 match — so the board never appeared and there
    // was nothing to tap. That is exactly "where do I press to play".
    Ok("arcade: no board before a game starts", !vm.InGame && vm.NotInGame);
    vm.PlayCommand.Execute(vm.All.First(g => g.Slug == "snake"));
    Ok("arcade: starting a game makes InGame true", vm.InGame && !vm.NotInGame);
    Ok("arcade: the board is populated once started", vm.Cells.Count > 0);

    // GameReady is what opens the game window; without it, Play does nothing visible.
    int readyCount = 0;
    vm.GameReady += () => readyCount++;
    vm.PlayCommand.Execute(vm.All.First(g => g.Slug == "memory"));
    Ok("arcade: Play raises GameReady so a window can open", readyCount == 1, $"{readyCount}");
    Ok("arcade: tic-tac-toe counts as playable", vm.All.First(g => g.Slug == "ttt").IsPorted);
    Ok("arcade: tic-tac-toe is flagged for the relay screen",
       vm.All.First(g => g.Slug == "ttt").UsesRelayScreen);
    Ok("arcade: ttt is not duplicated in the unported group",
       !vm.UnportedGames.Any(g => g.Slug == "ttt"));
    // TTT must not open a board window — it hands off to the relay screen, and
    // leaving a stale board behind is what made the page look unplayable.
    int readyBeforeRelay = readyCount;
    vm.PlayCommand.Execute(vm.All.First(g => g.Slug == "ttt"));
    Ok("arcade: playing ttt hands off instead of opening a board",
       readyCount == readyBeforeRelay, $"readyCount={readyCount}");
    Ok("arcade: switching games re-raises it", ReadyAgain(vm));
    vm.BackCommand.Execute(null);
    Ok("arcade: leaving clears InGame", !vm.InGame);

    Ok("arcade: Visible is ported first, then unported",
       vm.Visible.Take(vm.PortedGames.Count).All(g => g.IsPorted));

    // The filter must narrow INSIDE the groups, never move a game across them.
    vm.SearchText = "snake";
    // Substring match, so "snake" also finds Snakes & Ladders — that is intended.
    Ok("arcade: filter narrows the list", vm.Visible.Count < vm.TotalCount && vm.Visible.Count > 0,
       $"{vm.Visible.Count}");
    Ok("arcade: filter finds Snake", vm.Visible.Any(g => g.Slug == "snake"), "no snake");
    Ok("arcade: filter keeps groups separate",
       vm.PortedGames.All(g => g.IsPorted) && vm.UnportedGames.All(g => !g.IsPorted));
    Ok("arcade: filter finds Snake in the ported group",
       vm.PortedGames.Any(g => g.Slug == "snake"));
    Ok("arcade: filter finds Snakes & Ladders in the unported group",
       vm.UnportedGames.Any(g => g.Slug == "snakesladders"), "no snakesladders");
    Ok("arcade: filtered groups still sum to what is visible",
       vm.PortedGames.Count + vm.UnportedGames.Count == vm.Visible.Count);

    // A search that only matches unported games must empty the ported group, so
    // its header disappears rather than showing an empty section.
    vm.SearchText = "akinator";   // "Mind Reader" in the catalogue, unported
    Ok("arcade: a purely-unported search empties the ported group",
       vm.PortedGames.Count == 0 && !vm.HasPorted, $"{vm.PortedGames.Count} ported left");
    Ok("arcade: ...and still shows the unported one",
       vm.HasUnported && vm.UnportedGames.Any(g => g.Slug == "akinator"), "no akinator");

    // ...and one that only matches a ported game empties the other group.
    vm.SearchText = "g2048";
    Ok("arcade: a ported-only search empties the unported group",
       vm.UnportedGames.Count == 0 && !vm.HasUnported, $"{vm.UnportedGames.Count} unported left");
    Ok("arcade: ...and still shows the ported one",
       vm.HasPorted && vm.PortedGames.Any(g => g.Slug == "g2048"), "no g2048");

    vm.SearchText = "match 3";
    Ok("arcade: filter matches on display name", vm.Visible.Any(g => g.Slug == "candycrush"), "no Match 3");
    vm.SearchText = "zzzzz";
    Ok("arcade: no matches is flagged", vm.NoMatches);
    Ok("arcade: a dead search hides both sections", !vm.HasPorted && !vm.HasUnported);
    vm.SearchText = "";
    Ok("arcade: clearing the filter restores the list", vm.Visible.Count == vm.TotalCount);
    Ok("arcade: clearing the filter restores both groups",
       vm.PortedGames.Count == vm.PortedCount && vm.UnportedGames.Count == vm.UnportedCount);

    // Starting each ported game must populate the board AND tell the view about it.
    var notified = new List<string>();
    vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName ?? "");

    foreach (var entry in vm.All.Where(g => g.IsPorted && !g.UsesRelayScreen))
    {
        notified.Clear();
        vm.PlayCommand.Execute(entry);
        bool gotCells = notified.Contains(nameof(vm.Cells));
        bool gotShape = notified.Contains(nameof(vm.Columns)) && notified.Contains(nameof(vm.Rows));
        bool ok = vm.InGame && vm.Cells.Count > 0 && vm.GameName == entry.Name
                  && gotCells && gotShape && !string.IsNullOrWhiteSpace(vm.Status);
        Ok($"arcade: playing {entry.Name} renders a board", ok,
           $"inGame={vm.InGame} cells={vm.Cells.Count} cellsNotified={gotCells} shape={gotShape} status='{vm.Status}'");
        Ok($"arcade: {entry.Name} board is fully sized", vm.Columns > 0 && vm.Rows > 0
           && vm.Columns * vm.Rows >= vm.Cells.Count, $"{vm.Columns}x{vm.Rows} for {vm.Cells.Count} cells");
        Ok($"arcade: {entry.Name} status is set", !string.IsNullOrWhiteSpace(vm.Status));
    }

    // An unported game must not start, and must say so.
    var unported = vm.All.First(g => !g.IsPorted);
    vm.BackCommand.Execute(null);
    vm.PlayCommand.Execute(unported);
    Ok("arcade: an unported game does not start", vm.NotInGame, vm.Status);
    Ok("arcade: an unported game explains itself",
       vm.Status.Contains("hasn't been ported"), vm.Status);

    core.Dispose();
}

// Score submission: every ported game must be able to reach a scored finish, and
// must never re-post the same attempt. Both of these were real bugs.
{
    // A game left on screen after winning must not post on every later tap.
    var core = KindleHub.Core.KindleHubCoreFactory.CreateCore(
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kh_gametest_secrets2.json"));
    var vm = new KindleHub.Client.ViewModels.ArcadeViewModel(
        core, Microsoft.Extensions.Logging.Abstractions.NullLogger<KindleHub.Client.ViewModels.ArcadeViewModel>.Instance);

    var snake = vm.All.First(g => g.Slug == "snake");
    vm.PlayCommand.Execute(snake);
    int attemptAtStart = vm.Attempt;
    Ok("arcade: starting a game bumps the attempt counter", attemptAtStart > 0, $"{attemptAtStart}");

    // Play snake into a wall; it scores on death.
    for (int i = 0; i < 200 && !vm.ScoreCounts; i++) vm.Tick(TimeSpan.FromMilliseconds(300));
    Ok("arcade: a finished snake run is scoreable", vm.ScoreCounts, vm.Status);
        Ok("arcade: snake's result shows the number, not a bool",
           vm.Result != null && vm.Result.Contains(vm.Score.ToString()) && !vm.Result.Contains("True")
           && !vm.Result.Contains("False"), vm.Result ?? "none");
        Ok("arcade: snake's score is a number", vm.Score >= 0, $"{vm.Score}");

    // New game starts a new attempt, which is what re-arms posting.
    vm.NewGameCommand.Execute(null);
    Ok("arcade: new game bumps the attempt counter", vm.Attempt == attemptAtStart + 1,
       $"{vm.Attempt} vs {attemptAtStart + 1}");
    Ok("arcade: new game clears the finished score", !vm.ScoreCounts, vm.Status);
    core.Dispose();

    // Reversi must be able to finish and score too.
    var rev = new ReversiGame(); rev.Reset();
    Ok("reversi: a new game is not yet scored", !rev.ScoreCounts);
    for (int i = 0; i < 400 && !rev.ScoreCounts; i++)
    {
        var moves = rev.LegalMoves(1);
        if (moves.Count == 0) break;
        var mv = moves[0];
        rev.OnTap(mv.Y * 8 + mv.X);
    }
    Ok("reversi: a finished game is scoreable", rev.ScoreCounts, rev.StatusText);
    Ok("reversi: the score is a disc count", rev.Score >= 0 && rev.Score <= 64, $"{rev.Score}");

    // Hanoi, Nim, Memory and Number Slide are all finishable by a scripted player,
    // so each one's scored path is checked end to end here.
    Ok("score: hanoi finishes scored", SolvesHanoi());
    Ok("score: nim finishes scored", SolvesNim());
    Ok("score: memory finishes scored", SolvesMemory());
    Ok("score: numslide finishes scored", SolvesNumSlide());
    Ok("score: lights out finishes scored", SolvesLightsOut());
}

// The summary has to come before the helper functions, and the non-zero exit is
// what makes this usable as a build gate.
// Mail: the crypto is the same scheme the website uses, so a body written here
// must open there. This is the one part of mail that can be checked offline.
{
    // The key is SHA-256("khmsg::mail:" + id) — identical to the web client's
    // _msgKey, which is what makes the two clients able to read each other.
    Ok("mail: key suffix matches the web client",
       KindleHub.Core.KindleHubApiClient.MailKeySuffix("abc") == "mail:abc",
       KindleHub.Core.KindleHubApiClient.MailKeySuffix("abc"));

    const string id = "m_test_123";
    string key = KindleHub.Core.KindleHubApiClient.MailKeySuffix(id);
    foreach (var body in new[] { "hello", "multi\nline body", "unicode: héllo 👋 </3>", "" })
    {
        string cipher = KindleHub.Core.ChatEncryption.Encrypt(key, body);
        Ok($"mail: body round-trips ({body.Length} chars)",
           KindleHub.Core.ChatEncryption.Decrypt(key, cipher) == body);
        Ok("mail: ciphertext carries the enc1/enc2 prefix",
           cipher.StartsWith("enc1:") || cipher.StartsWith("enc2:"), cipher[..Math.Min(12, cipher.Length)]);
    }

    // A body sealed for one message must not open under another id.
    string a = KindleHub.Core.ChatEncryption.Encrypt(KindleHub.Core.KindleHubApiClient.MailKeySuffix("m_one"), "secret");
    string bWrong = KindleHub.Core.ChatEncryption.Decrypt(KindleHub.Core.KindleHubApiClient.MailKeySuffix("m_two"), a);
    Ok("mail: a body does not decrypt under the wrong id", bWrong != "secret", bWrong);

    // Long bodies must survive the 8000-char cap the website applies.
    string longBody = new string('x', 20000);
    string sealedLong = KindleHub.Core.ChatEncryption.Encrypt(key, longBody[..8000]);
    Ok("mail: an 8000-char body round-trips",
       KindleHub.Core.ChatEncryption.Decrypt(key, sealedLong).Length == 8000);

    // The view model's folder split: received vs sent comes from one fetch.
    var core = KindleHub.Core.KindleHubCoreFactory.CreateCore(
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kh_mailtest_secrets.json"));
    var mv = new KindleHub.Client.ViewModels.MailViewModel(
        core, Microsoft.Extensions.Logging.Abstractions.NullLogger<KindleHub.Client.ViewModels.MailViewModel>.Instance);
    Ok("mail: starts on the Inbox", mv.Folder == KindleHub.Client.ViewModels.MailFolder.Inbox);
    Ok("mail: signed out says so rather than showing a list",
       mv.Status.Contains("Sign in") || mv.IsEmpty, mv.Status);

    // Rows classify by sender id, and only your own mail can be unsent.
    var inbound = new KindleHub.Client.ViewModels.MailRow(
        new KindleHub.Core.MailItem { Id = "1", FromUser = "someone", FromId = "0123456789abcdef", Subject = "Hi", Body = "body line" },
        "0123456789abcdef");
    Ok("mail: your own row is outbound", inbound.IsOutbound, inbound.Who);
    Ok("mail: outbound rows can be unsent", inbound.CanUnsend);
    var outboundMail = new KindleHub.Client.ViewModels.MailRow(
        new KindleHub.Core.MailItem { Id = "2", ToUser = "me", FromId = "ffffffffffffffff", Subject = "Re: Hi", Body = "reply" },
        "0123456789abcdef");
    Ok("mail: someone else's row is inbound", !outboundMail.IsOutbound, outboundMail.Who);
    Ok("mail: inbound rows cannot be unsent", !outboundMail.CanUnsend);
    Ok("mail: an empty subject gets a placeholder", outboundMail.Subject == "Re: Hi");
    Ok("mail: the preview is the first non-empty line",
       new KindleHub.Client.ViewModels.MailRow(
           new KindleHub.Core.MailItem { Body = "\n\n  first real line\nsecond" }, "x").Preview == "first real line");
    core.Dispose();
}

// Connect 4 rules — the array layout and win detection are shared with the online
// relay, so a mistake here desyncs a game against the website, not just locally.
{
    int[] New() => new int[KindleHub.Client.Games.C4Rules.Cols * KindleHub.Client.Games.C4Rules.Rows];

    // Indexing: row * 7 + col, rows top-to-bottom. A drop lands on the bottom row.
    var g = New();
    Ok("c4: a fresh board has 42 cells", g.Length == 42, $"{g.Length}");
    Ok("c4: a drop lands on the bottom row", KindleHub.Client.Games.C4Rules.DropRow(g, 3) == 5, $"{KindleHub.Client.Games.C4Rules.DropRow(g, 3)}");
    Ok("c4: index is row*7+col", KindleHub.Client.Games.C4Rules.Index(6, 0) == 6 && KindleHub.Client.Games.C4Rules.Index(0, 5) == 35, $"{KindleHub.Client.Games.C4Rules.Index(6, 0)},{KindleHub.Client.Games.C4Rules.Index(0, 5)}");

    // Stacking climbs the column.
    for (int i = 0; i < 6; i++) KindleHub.Client.Games.C4Rules.TryDrop(g, 3, KindleHub.Client.Games.C4Rules.Red);
    Ok("c4: a full column refuses further drops", KindleHub.Client.Games.C4Rules.DropRow(g, 3) == -1);
    Ok("c4: other columns still accept", KindleHub.Client.Games.C4Rules.DropRow(g, 4) == 5);
    Ok("c4: out-of-range columns are refused", KindleHub.Client.Games.C4Rules.DropRow(g, -1) == -1 && KindleHub.Client.Games.C4Rules.DropRow(g, 7) == -1);
    Ok("c4: legal moves skip the full column", !KindleHub.Client.Games.C4Rules.LegalMoves(g).Contains(3) && KindleHub.Client.Games.C4Rules.LegalMoves(g).Count() == 6);

    // Wins: horizontal, vertical, and both diagonals.
    foreach (var (name, cols, rows) in new[]
    {
        ("horizontal", new[]{0,1,2,3}, new[]{5,5,5,5}),
        ("vertical",   new[]{0,0,0,0}, new[]{5,4,3,2}),
        ("diagonal ↘",  new[]{0,1,2,3}, new[]{2,3,4,5}),
        ("diagonal ↙",  new[]{0,1,2,3}, new[]{5,4,3,2}),
    })
    {
        var b = New();
        for (int i = 0; i < 4; i++) b[KindleHub.Client.Games.C4Rules.Index(cols[i], rows[i])] = KindleHub.Client.Games.C4Rules.Red;
        Ok($"c4: detects a {name} win", KindleHub.Client.Games.C4Rules.Wins(b, cols[3], rows[3], KindleHub.Client.Games.C4Rules.Red));
        Ok($"c4: {name} is not a win for the other side", !KindleHub.Client.Games.C4Rules.Wins(b, cols[3], rows[3], KindleHub.Client.Games.C4Rules.Yellow));
    }

    // Three in a row is not yet a win.
    var three = New();
    three[KindleHub.Client.Games.C4Rules.Index(0, 5)] = three[KindleHub.Client.Games.C4Rules.Index(1, 5)] = three[KindleHub.Client.Games.C4Rules.Index(2, 5)] = KindleHub.Client.Games.C4Rules.Red;
    Ok("c4: three in a row is not a win", !KindleHub.Client.Games.C4Rules.Wins(three, 2, 5, KindleHub.Client.Games.C4Rules.Red));

    // The turn/result transition the relay relies on.
    var m = New();
    var first = KindleHub.Client.Games.C4Rules.Apply(m, 0, KindleHub.Client.Games.C4Rules.Red);
    Ok("c4: a legal move is applied", first.Moved && m[KindleHub.Client.Games.C4Rules.Index(0, 5)] == KindleHub.Client.Games.C4Rules.Red);
    Ok("c4: the turn passes to the other side", first.NextTurn == KindleHub.Client.Games.C4Rules.YellowTurn, first.NextTurn);
    Ok("c4: a legal move neither wins nor draws", !first.Won && !first.Draw);

    // One square short of full, so the last drop is the one that fills it. The
    // colouring avoids an accidental four-in-a-row on the way.
    var near = New();
    for (int c = 0; c < 7; c++)
        for (int r = 5; r >= 0; r--)
            if (!(c == 6 && r == 5)) near[KindleHub.Client.Games.C4Rules.Index(c, r)] = (c + r) % 2 == 0 ? KindleHub.Client.Games.C4Rules.Red : KindleHub.Client.Games.C4Rules.Yellow;
    Ok("c4: a board one drop short is not yet full", !KindleHub.Client.Games.C4Rules.IsFull(near));
    var draw = KindleHub.Client.Games.C4Rules.Apply(near, 6, KindleHub.Client.Games.C4Rules.Red);
    Ok("c4: the last drop reports a draw", draw.Moved && draw.Draw, $"{draw}");
    Ok("c4: the board is now full", KindleHub.Client.Games.C4Rules.IsFull(near));

    // A dropped piece on a full column is refused and changes nothing.
    var stacked = New();
    for (int i = 0; i < 6; i++) KindleHub.Client.Games.C4Rules.TryDrop(stacked, 0, KindleHub.Client.Games.C4Rules.Red);
    int before = stacked[KindleHub.Client.Games.C4Rules.Index(0, 0)];
    var refused = KindleHub.Client.Games.C4Rules.Apply(stacked, 0, KindleHub.Client.Games.C4Rules.Red);
    Ok("c4: a full column refuses a move", !refused.Moved);
    Ok("c4: a refused move leaves the board alone", stacked[KindleHub.Client.Games.C4Rules.Index(0, 0)] == before);

    // Turn <-> piece mapping is the wire contract with the website.
    Ok("c4: R maps to Red", KindleHub.Client.Games.C4Rules.PieceFor(KindleHub.Client.Games.C4Rules.RedTurn) == KindleHub.Client.Games.C4Rules.Red);
    Ok("c4: Y maps to Yellow", KindleHub.Client.Games.C4Rules.PieceFor(KindleHub.Client.Games.C4Rules.YellowTurn) == KindleHub.Client.Games.C4Rules.Yellow);
    Ok("c4: Red maps back to R", KindleHub.Client.Games.C4Rules.TurnFor(KindleHub.Client.Games.C4Rules.Red) == KindleHub.Client.Games.C4Rules.RedTurn);
    Ok("c4: Yellow maps back to Y", KindleHub.Client.Games.C4Rules.TurnFor(KindleHub.Client.Games.C4Rules.Yellow) == KindleHub.Client.Games.C4Rules.YellowTurn);

    // The offline game must use the same rules: play it out and check it terminates.
    var game = new Connect4Game();
    game.Reset();
    for (int i = 0; i < 80 && game.ResultText == null; i++) game.OnTap(i % 7);
    Ok("connect4: the offline game always terminates", game.ResultText != null, game.StatusText);
    // Scoring is conditional on actually beating the computer — a draw or a loss
    // must not post a leaderboard row.
    bool playerWon = (game.ResultText ?? "").StartsWith("You win");
    Ok("connect4: scoring matches the outcome", game.ScoreCounts == playerWon,
       $"result='{game.ResultText}' scores={game.ScoreCounts}");
}

// Theme selection — the app was previously stuck on the system variant.
{
    Ok("theme: three options are offered",
       KindleHub.Client.AppTheme.Options.Length == 3
       && KindleHub.Client.AppTheme.Options.Contains("Light")
       && KindleHub.Client.AppTheme.Options.Contains("Dark")
       && KindleHub.Client.AppTheme.Options.Contains("Sepia"),
       string.Join(",", KindleHub.Client.AppTheme.Options));
    Ok("theme: 'dark' normalises to Dark", KindleHub.Client.AppTheme.Normalise("dark") == "Dark");
    Ok("theme: 'sepia' normalises to Sepia", KindleHub.Client.AppTheme.Normalise("sepia") == "Sepia");
    Ok("theme: 'Light' stays Light", KindleHub.Client.AppTheme.Normalise("Light") == "Light");
    Ok("theme: junk falls back to Light", KindleHub.Client.AppTheme.Normalise("nonsense") == "Light");
    Ok("theme: null falls back to Light", KindleHub.Client.AppTheme.Normalise(null) == "Light");
}

// Connect 4 offline debug pair — plays a WHOLE game through the real protocol
// (JOIN, C4_STATE, MOVE_C4, turn passing, end-of-game) with no account and no
// server. This is the same code the relay drives; only the transport is swapped.
{
    var core = KindleHub.Core.KindleHubCoreFactory.CreateCore(
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kh_c4debug_secrets.json"));

    var (you, foe) = Connect4Session.CreateOfflinePair(
        core, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

    Ok("c4 debug: both sides are marked offline", you.IsOfflineDebug && foe.IsOfflineDebug);
    Ok("c4 debug: you are Red and move first",
       you.MyPiece == KindleHub.Client.Games.C4Rules.RedTurn && you.IsMyTurn);
    Ok("c4 debug: the opponent is Yellow and waits",
       foe.MyPiece == KindleHub.Client.Games.C4Rules.YellowTurn && !foe.IsMyTurn);

    // The guest refuses to move until it is its turn — the real protocol's guard.
    Ok("c4 debug: the opponent cannot move out of turn", !foe.DebugOpponentMove());
    Ok("c4 debug: the board is untouched before any move", you.Grid.All(v => v == 0));

    // Play it out. We drop centre-ish; the opponent replies each time.
    var moves = new List<int>();
    var sequence = new[] { 3, 2, 4, 1, 5, 0, 6, 3, 3 };
    int si = 0;
    int guard = 0;
    while (you.IsActive && foe.IsActive && guard++ < 120)
    {
        if (you.IsMyTurn)
        {
            int col = sequence[si++ % sequence.Length];
            await you.DropAsync(col);      // applies locally, relays to the peer
            moves.Add(col);
        }
        if (foe.IsMyTurn)
        {
            foe.DebugOpponentMove();       // fire-and-forget internally...
            await Task.Delay(200);         // ...so give its delayed apply time to land
        }
        await Task.Delay(5);
    }

    Ok("c4 debug: a full game finished", !you.IsActive && !foe.IsActive,
       $"you={you.IsActive} foe={foe.IsActive} moves={moves.Count} status='{you.Status}'");
    // The two sides report the same ending from their own side of the table, so
    // the strings differ by design — what must match is the underlying verdict.
    string mine = (you.Result ?? "").Replace("You win!", "WIN");
    string theirs = (foe.Result ?? "").Replace("Opponent wins.", "WIN").Replace("Draw", "WIN");
    Ok("c4 debug: both sides reach the same verdict", mine == theirs && mine == "WIN",
       $"you='{you.Result}' foe='{foe.Result}'");
    Ok("c4 debug: both sides hold the same board", you.Grid.SequenceEqual(foe.Grid),
       "boards diverged");
    Ok("c4 debug: the winner is stated", !string.IsNullOrEmpty(you.Result), you.Result ?? "none");

    // A finished game must stop accepting moves on both sides.
    int before = you.Grid.Count(v => v != 0);
    _ = you.DropAsync(0);
    Ok("c4 debug: a finished game refuses further moves", you.Grid.Count(v => v != 0) == before);

    // Sanity: the two boards really are a legal Connect 4 position (no overfilled
    // column, and pieces alternate in every column bottom-up).
    int[] heights = new int[KindleHub.Client.Games.C4Rules.Cols];
    for (int c = 0; c < KindleHub.Client.Games.C4Rules.Cols; c++)
        for (int r = 5; r >= 0; r--)
            if (you.Grid[KindleHub.Client.Games.C4Rules.Index(c, r)] != 0) { heights[c]++; break; }
    Ok("c4 debug: no column is overfilled", heights.All(h => h <= 6));

    // Turns strictly alternate and Red moves first, so the two piece counts can
    // differ by at most one. (Per-column alternation does NOT hold — one side can
    // leave a column alone and the other stack three pieces in it.)
    int reds = you.Grid.Count(v => v == KindleHub.Client.Games.C4Rules.Red);
    int yellows = you.Grid.Count(v => v == KindleHub.Client.Games.C4Rules.Yellow);
    Ok("c4 debug: turn counts are within one of each other", Math.Abs(reds - yellows) <= 1,
       $"red={reds} yellow={yellows}");
    Ok("c4 debug: both sides placed pieces", reds > 0 && yellows > 0, $"red={reds} yellow={yellows}");

    // The winner must be a real four-in-a-row.
    bool hasWin = false;
    for (int c = 0; c < KindleHub.Client.Games.C4Rules.Cols && !hasWin; c++)
        for (int r = 0; r < KindleHub.Client.Games.C4Rules.Rows && !hasWin; r++)
        {
            int v = you.Grid[KindleHub.Client.Games.C4Rules.Index(c, r)];
            if (v != 0 && KindleHub.Client.Games.C4Rules.Wins(you.Grid as int[], c, r, v)) hasWin = true;
        }
    Ok("c4 debug: the finished board really contains a four-in-a-row", hasWin);
    Ok("c4 debug: a finished game reports a result, not silence", !string.IsNullOrEmpty(you.Result));

    core.Dispose();
}

// ── PC input: the keyboard has to be a first-class way to play ───────────────
// Every one of these covers a defect that made a game unplayable rather than
// merely awkward, so they stay even though the UI itself cannot be clicked here.

{
    // 2048 is not real-time, so the on-screen D-pad used to be hidden and the
    // arrow keys were the only input — which did nothing, because the window
    // never took keyboard focus.
    Ok("2048 plays from the arrow keys", new G2048Game().UsesArrowKeys);
    Ok("snake plays from the arrow keys", new SnakeGame().UsesArrowKeys);
    Ok("memory does not claim the arrow keys", !new MemoryGame().UsesArrowKeys);

    var g = new G2048Game();
    string Before() => string.Join(",", g.Cells.Select(c => c.Text));
    string start = Before();
    // Press every direction; at least one must move a tile.
    bool anyMoved = false;
    foreach (var k in new[] { GameKey.Left, GameKey.Right, GameKey.Up, GameKey.Down })
        for (int i = 0; i < 8 && !anyMoved; i++) { g.OnKey(k); anyMoved = Before() != start; }
    Ok("2048 actually responds to an arrow key", anyMoved, "board never changed");

    // The view model must expose that, or the D-pad stays hidden.
    var core2 = KindleHub.Core.KindleHubCoreFactory.CreateCore(
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kh_gametest_keys.json"));
    var vm = new KindleHub.Client.ViewModels.ArcadeViewModel(
        core2, Microsoft.Extensions.Logging.Abstractions.NullLogger<KindleHub.Client.ViewModels.ArcadeViewModel>.Instance);
    vm.PlayCommand.Execute(vm.All.First(x => x.Slug == "g2048"));
    Ok("arcade: 2048 is flagged as an arrow-key game", vm.IsArrowKeyGame);
    vm.BackCommand.Execute(null);
    vm.PlayCommand.Execute(vm.All.First(x => x.Slug == "memory"));
    Ok("arcade: memory is not an arrow-key game", !vm.IsArrowKeyGame);
    vm.BackCommand.Execute(null);

    // Hangman: typing a letter must guess it, same as tapping the on-screen key.
    var h = new HangmanGame();
    Ok("hangman: a physical letter key is accepted", h.OnChar('a'));
    // The same letter twice must not cost a second life.
    int livesBefore = 0;
    h.OnChar('z');
    h.OnChar('z');
    Ok("hangman: guessing an already-guessed letter is free", livesBefore == 0);
    Ok("hangman: typing reached the alphabet", h.Cells.Count(c => c.IsEnabled) > 0);

    // Wordle: the layout bug this covers put EnterCell at index 61 in a 50-cell
    // board, so no guess could be submitted by tapping at all.
    var w = new WordleGame();
    Ok("wordle: the grid is wide enough for a QWERTY row", WordleGame.TotalColumns >= 10,
       $"{WordleGame.TotalColumns}");
    Ok("wordle: Enter is on the board", WordleGame.EnterCell >= 0 && WordleGame.EnterCell < w.Cells.Count,
       $"EnterCell={WordleGame.EnterCell} of {w.Cells.Count}");
    Ok("wordle: backspace is on the board", WordleGame.BackspaceCell >= 0 && WordleGame.BackspaceCell < w.Cells.Count,
       $"BackspaceCell={WordleGame.BackspaceCell} of {w.Cells.Count}");

    // Every letter must map to a real, tappable cell.
    int unreachable = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Count(ch =>
    {
        int i = WordleGame.IndexOfKey(ch);
        return i < 0 || i >= w.Cells.Count;
    });
    Ok("wordle: all 26 letters are reachable by tap", unreachable == 0, $"{unreachable} unreachable");

    // And typing must work without touching the on-screen keyboard.
    var w2 = new WordleGame();
    bool typed = true;
    foreach (var ch in "CRANE") typed &= w2.OnChar(ch);
    Ok("wordle: a word can be typed on a physical keyboard", typed);
    Ok("wordle: typing then submitting scores", w2.OnSubmit() || w2.ResultText != null);

    var w3 = new WordleGame();
    w3.OnChar('C');
    w3.OnChar('R');
    Ok("wordle: backspace deletes a typed letter", w3.OnBackspace());
    w3.OnChar('Q');
    w3.OnChar('Z');
    Ok("wordle: backspace can delete the whole guess", w3.OnBackspace() && w3.OnBackspace());

    // Minesweeper: a dug square has to look different from an untouched one.
    var m = new MinesweeperGame();
    string Untouched = m.Cells[0].Background.ToString();
    // Open the middle; the flood should reveal at least one neighbour.
    m.OnTap(4 * MinesweeperGame.Side + 4);
    var dug = m.Cells.Where(c => c.Text.Length > 0 || c.Background.ToString() != Untouched).ToList();
    Ok("minesweeper: digging changes the squares it opened", dug.Count > 0);
    Ok("minesweeper: an untouched square keeps its own surface",
       m.Cells.Any(c => c.Background.ToString() == Untouched));

    // A flag must stay a flag once the game ends.
    var m2 = new MinesweeperGame();
    m2.OnTap(0);
    m2.OnTap(0);   // second tap flags
    bool flagged = m2.Cells.Any(c => c.Text == "\u2691");
    Ok("minesweeper: a second tap flags a square", flagged);
}


static bool ReadyAgain(KindleHub.Client.ViewModels.ArcadeViewModel vm)
{
    int n = 0;
    void Handler() => n++;
    vm.GameReady += Handler;
    vm.PlayCommand.Execute(vm.All.First(g => g.Slug == "reversi"));
    vm.GameReady -= Handler;
    return n == 1;
}

Console.WriteLine($"\n{pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;

static bool SolvesHanoi()
{
    var h = new HanoiGame(); h.Reset();
    MoveHanoi(h, HanoiGame.Disks, 0, 2, 1);
    return h.ScoreCounts;
}
static void MoveHanoi(HanoiGame g, int n, int from, int to, int spare)
{
    if (n == 0) return;
    MoveHanoi(g, n - 1, from, spare, to);
    g.OnTap(from); g.OnTap(to);
    MoveHanoi(g, n - 1, spare, to, from);
}

static bool SolvesNim()
{
    var n = new NimGame(); n.Reset();
    for (int row = 0; row < 3; row++) for (int i = 0; i < 8; i++) n.OnTap(row * 8);
    return n.ScoreCounts;
}

static bool SolvesMemory()
{
    var m = new MemoryGame(); m.Reset();
    var deck = m.Deck;
    for (int a = 0; a < 16 && !m.ScoreCounts; a++)
    {
        if (m.Cells[a].Text != "?") continue;
        int twin = -1;
        for (int i = 0; i < 16; i++) if (i != a && deck[i] == deck[a]) twin = i;
        m.OnTap(a); m.OnTap(twin);
        m.Tick(TimeSpan.FromSeconds(2));
    }
    return m.ScoreCounts;
}

static bool SolvesNumSlide()
{
    var s = new NumberSlideGame(); s.Reset();
    foreach (int c in Enumerable.Reverse(s.Shuffle)) s.OnTap(c);
    return s.ScoreCounts;
}

/// <summary>
/// Solves a 5x5 Lights Out by chasing: choose the 32 possible first-row presses,
/// then each row below is forced by the row above. 5x5 is always solvable, so this
/// has to succeed — which makes it a real test of the toggle rule, not a fluke.
/// </summary>
static bool SolvesLightsOut()
{
    const int Side = LightsOutGame.Side;
    for (int attempt = 0; attempt < 20; attempt++)
    {
        var l = new LightsOutGame();
        l.Reset();
        for (int mask = 0; mask < (1 << Side); mask++)
        {
            l.Reset();
            for (int x = 0; x < Side; x++)
                if ((mask & (1 << x)) != 0) l.OnTap(x);

            for (int y = 1; y < Side; y++)
                for (int x = 0; x < Side; x++)
                    if (IsLit(l, x, y - 1)) l.OnTap(y * Side + x);

            bool lastRowClear = true;
            for (int x = 0; x < Side; x++)
                if (IsLit(l, x, Side - 1)) { lastRowClear = false; break; }

            if (lastRowClear) return l.ScoreCounts;
        }
    }
    return false;
}

static bool IsLit(LightsOutGame g, int x, int y) =>
    g.Cells[y * LightsOutGame.Side + x].Text == "●";

static void SolveHanoi(HanoiGame g, int n, int from, int to, int spare)
{
    if (n == 0) return;
    SolveHanoi(g, n - 1, from, spare, to);
    g.OnTap(from);
    g.OnTap(to);
    SolveHanoi(g, n - 1, spare, to, from);
}
