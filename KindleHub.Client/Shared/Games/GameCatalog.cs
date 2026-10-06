using System;
using System.Collections.Generic;

namespace KindleHub.Client.Models;

/// <summary>
/// The KindleHub arcade catalog, ported from the official client's GAME_HELP
/// table (kh-app.js). Every entry is one launchable game: <see cref="Slug"/> is
/// the deep-link / relay identifier used by launchGame(id) on the site, and it is
/// what we append to the site root to open that game in a browser.
///
/// Kept as static data rather than fetched so the Games page is useful offline
/// and never renders an empty grid when the API is unreachable.
/// </summary>
public static class GameCatalog
{
    /// <summary>Where the official web client lives. Games open as &lt;root&gt;/&lt;slug&gt;.</summary>
    public const string SiteRoot = "https://kindlehub.pro";

    /// <summary>Every game the official client can launch, alphabetically by slug.</summary>
    public static IReadOnlyList<GameCatalogEntry> All { get; } = Build();

    /// <summary>Builds the deep link that opens <paramref name="slug"/> on the official client.</summary>
    public static string UrlFor(string slug) => SiteRoot.TrimEnd('/') + "/" + slug;

    private static IReadOnlyList<GameCatalogEntry> Build()
    {
        var list = new List<GameCatalogEntry>
        {
            new GameCatalogEntry("akinator", "Mind Reader",
                "Think of a well-known person, animal or fictional character — it reads your mind Akinator-style. Answer its yes/no questions honestly (Yes, No, Probably, Probably not, or Don't know) and it names who you're thinking of. If it guesses wrong, tell it — it keeps digging. It runs on FREE AI (your own key if you've set one, otherwise the free Cloudflare AI) — never the shared pool, so it can't use up anyone else's allowance. With no AI available it falls back to an on-device database of well-known answers. Your win streak is the score."),
            new GameCatalogEntry("anagrams", "Anagrams",
                "Unscramble the jumbled letters into the word that fits the clue. Tap a letter tile to add it to your answer, tap a tile in the answer row to send it back. Press Check when you think you have it. Shuffle re-jumbles the spare letters; Skip reveals the word and moves on. Solve as many as you can — beat your best streak."),
            new GameCatalogEntry("arena", "Arena Clash",
                "A top-down brawl against three bots. TAP anywhere in the arena to send your fighter there — it AUTO-FIRES at the nearest enemy, so you only steer (one-handed, no aiming). Use the grey blocks as cover and grab the green health packs. Knock out an enemy for a KO; first to 5 KOs wins, or the most KOs when the 2-minute timer runs out. Knocked-out fighters respawn after a moment."),
            new GameCatalogEntry("battleship", "Battleship",
                "Place five ships, then take turns firing at one square of the enemy grid. Sink their whole fleet before they sink yours. Three ways to play: against the computer, two players on this device (it hands over between turns so nobody sees the other board), or online against another Kindle — where your fleet never leaves your device, so there is nothing to peek at."),
            new GameCatalogEntry("beambalance", "Balance",
                "Put every weight on the left or the right of the beam so both sides come to the same total. Tap a weight to move it between left, off the beam, and right. Every weight has to be on the beam for it to count."),
            new GameCatalogEntry("blackjack", "Blackjack",
                "Get closer to 21 than the dealer without going over. Hit for another card, Stand to hold. Face cards are 10; an Ace counts as 1 or 11. The dealer draws to 17. A two-card 21 (Blackjack) pays 3:2. Adjust your bet, then Deal — build the biggest chip stack you can."),
            new GameCatalogEntry("blockblast", "Block Blast",
                "Drag the block shapes onto the grid. Fill a full row or column to clear it. It ends when no piece can fit."),
            new GameCatalogEntry("bridges", "Bridges",
                "Every island shows how many bridges must touch it. Tap two islands in the same row or column to build a bridge between them, tap the same pair again for a double, and once more to remove it. Bridges cannot cross each other or pass through an island. When every number is satisfied AND all the islands are joined into one group, the puzzle is done."),
            new GameCatalogEntry("candycrush", "Match 3",
                "Swap adjacent pieces to line up three or more of a kind. Matches clear and score — set up chains for big combos."),
            new GameCatalogEntry("cargorun", "Cargo Run",
                "Collect every crate and finish at the depot without running out of fuel. Each step costs one fuel, so the route matters more than the reflexes. Walls block you. Your lowest fuel used is kept for each map."),
            new GameCatalogEntry("checkers", "Checkers",
                "Move diagonally and jump enemy pieces to capture them. Reach the far side to crown a King. Capture everything to win."),
            new GameCatalogEntry("chess", "Chess",
                "Play chess against the computer. Tap a piece, then its destination. Deliver checkmate to win."),
            new GameCatalogEntry("connect4", "Connect 4",
                "Drop discs into the columns. First to line up four in a row — across, down or diagonally — wins."),
            new GameCatalogEntry("connections", "Connections",
                ""),
            new GameCatalogEntry("craft", "Infinite Craft",
                "Start with four elements — Water, Fire, Wind, Earth — and combine any two to discover something new (Water + Fire = Steam, Earth + Water = Mud, and on and on). Tap two things in your collection to mix them. Hundreds of combos work instantly offline; anything brand new is dreamed up by KindleHub AI and saved forever, so the world never stops growing. See how many things you can discover."),
            new GameCatalogEntry("crazy8", "Uno",
                "The classic colour-and-number card game — play the computer or a friend online. Empty your hand first. Play a card that matches the discard by COLOUR (shown by the letter R/Y/G/B and the coloured border) or by NUMBER/symbol. Action cards: Skip and Reverse skip the other player, Draw Two makes them draw 2 and miss a turn. Wild lets you pick the colour; Wild Draw Four also makes them draw 4. Can’t play? Tap the deck to Draw, then play it or Pass. From the start menu choose “Play vs Computer” or “Play Online — 2 players”; online, the host runs the game so each player only ever sees their own hand, and there’s a chat panel to talk."),
            new GameCatalogEntry("crossyroad", "Crossy Road",
                "Hop your chicken UP across endless lanes of grass, traffic, rivers and railways. TAP the middle of the board (or ↑) to hop forward, tap the left/right thirds (or ←/→) to dodge sideways. Time the gaps in the traffic; on a RIVER you must land on and ride the drifting logs — falling in the water is instant. Watch the red rail lights: a train sweeps the tracks periodically, so cross between trains. Grab the gold coins for bonus points. Trees block a cell, so route around them. There's a safe grass strip after every river, so it's always survivable. Your score is distance (metres) plus coins — beat your best."),
            new GameCatalogEntry("cryptogram", "Cryptogram",
                "A famous quote with every letter swapped for another. Tap a letter in the quote, then tap what you think it really stands for. Letter frequency and short words are the way in. Hint reveals one letter, but a hinted solve does not count towards your total."),
            new GameCatalogEntry("deephalls", "Deep Halls",
                "A turn-based dungeon crawler. Move with the arrow pad (or arrow keys), or tap a square next to you. Walk into a monster to attack it. Nothing moves until you do, so take your time. Pick up potions (!), weapons (/), armour (]) and gold ($) by walking over them, and find the stairs (>) to go deeper. Killing monsters levels you up, which raises your health, attack and defence. You heal a little on each new floor. Traps show as a small ring when you are next to one, so look before you step. Shrines (&) offer one bargain each. Traders (*) from depth 2 sell potions, gear and spells — cast a spell from the Spells button and it lands on the next monster you hit. Death ends the run — your deepest floor is kept."),
            new GameCatalogEntry("digquest", "Deep Dig",
                "A dig-and-smash story platformer over SIX chapters. ◄ ► move, JUMP leaps (grab a Spring-cap for a double-jump), DIG smashes the earth you face, DIG▼ digs straight down (keyboard: arrows/WASD, Space, Z). Tunnel for crystals, coins and gate-stones; a drilling-charm breaks solid stone and a ward-shield keeps you safe for a while; STOMP the crawling grubs from above (touch one and it bites). Touch a checkpoint flag and you respawn there. Bank crystals to buy permanent upgrades between runs. Clear each exit, then stomp the Gravecrawler boss three times to free the Heartspring. Toggle E-ink mode for fewer, chunkier frames."),
            new GameCatalogEntry("dominoes", "Dominoes",
                "Draw dominoes and be first to play your whole hand. A tile can only be added if one of its ends matches an end of the line. Draw if you cannot go. If both players are stuck and the pile is empty, fewest pips wins."),
            new GameCatalogEntry("dotsboxes", "Dots & Boxes",
                "Take turns drawing one line between two dots. Complete the 4th side of a box to claim it and take another turn. Most boxes wins."),
            new GameCatalogEntry("dquest", "Dungeon Quest",
                "The Kindle edition of Dungeon Quest 3D. First person, but turn by turn: you move one tile and turn ninety degrees at a time, and the screen redraws once per action — which is the one thing an e-ink panel is superb at. Pick a Knight, Ranger or Mage. Take a job from the Notice Board in the town square, buy a sword at the Smithy and potions from the Arcanist, rest at the Inn, then walk down into the Hollow Caves. Clear every monster on a floor before the stairs down will open. The Lich waits at the bottom. Arrow keys move and turn, Space attacks, E enters a door or a stair, M is the map."),
            new GameCatalogEntry("eightball", "8-Ball",
                "A top-down pool table with three modes — 1 Player (practice: clear the table in as few shots as you can), 2 Players (pass-and-play on one device, taking turns) and Play Online (match a friend on another device). Aim by dragging BACK from the white cue ball (like a slingshot) — the dotted line shows where it will go — then release to shoot; the further you drag, the harder the hit. In 2-player/online, sink a ball to shoot again; miss or scratch and it passes to your opponent. Pot your balls, then the black 8-ball LAST to win — sinking the 8 early (or with a scratch) loses."),
            new GameCatalogEntry("emberdeck", "Ember Deck",
                "A deck-building card battler. Each turn you get 3 energy and draw 5 cards; tap a card to play it, then End turn. Attacks damage the enemy, block soaks their next hit. Block is lost at the end of your turn unless the card says otherwise. The enemy always shows what it is about to do, so you can decide whether to block or push for damage. Win a fight and take one of three cards into your deck — or skip the card and rest instead. Ten floors, and the deck you build is the whole run."),
            new GameCatalogEntry("farm", "Farm",
                ""),
            new GameCatalogEntry("flightsim", "Flight Simulator",
                "Choose from three aircraft — a Cessna 172 (easy, forgiving), a Piper Arrow (faster) or a King Air 90 (twin turboprop). Then take Free Flight to explore, or Challenges: engine failure, storms and crosswind landings."),
            new GameCatalogEntry("freecell", "FreeCell",
                "Solitaire with everything face up, so it is thinking rather than luck. Build columns down in alternating colours, use the four free cells as temporary parking, and send each suit home from the ace. How many cards you can move at once depends on how many cells and empty columns are free. Auto sends home whatever is safe."),
            new GameCatalogEntry("fruitninja", "Fruit Ninja",
                "Swipe across the flying fruit to slice them. Slice several in one swipe for combo bonuses. Miss three fruit or slice a bomb (!) and it's game over."),
            new GameCatalogEntry("futoshiki", "Futoshiki",
                "A Latin square with arrows. Every row and column uses 1 to 5 exactly once, and an arrow between two squares points at the SMALLER number. Tap a square to cycle it. Judged against the rules, so any valid answer is accepted."),
            new GameCatalogEntry("g2048", "2048",
                "Slide all tiles with arrow keys or swipes. Matching numbers merge and double. Reach 2048 — then keep going for a high score."),
            new GameCatalogEntry("galaga", "Galaga",
                "Move left/right and fire. Blast the alien formation — and watch for divers that swoop down at you (worth 3x, but they can crash into you). Max two shots on screen. Clear each wave."),
            new GameCatalogEntry("geometrydash", "Geometry Dash",
                ""),
            new GameCatalogEntry("gomoku", "Gomoku",
                "Get five of your marks in a row, in any direction, on a 13x13 board. You are X. Three levels: the higher ones defend as well as attack. Simple to learn and very hard to master."),
            new GameCatalogEntry("hangman", "Hangman",
                "Guess the hidden word one letter at a time. Each wrong letter adds to the drawing — finish the word before the drawing completes."),
            new GameCatalogEntry("hanoi", "Tower of Hanoi",
                "Move the whole stack of disks from the left peg to the right peg. Tap a peg to lift its top disk, then tap another peg to drop it. You can never place a bigger disk on a smaller one. Fewer moves is better — the minimum is 2ⁿ−1 for n disks. Choose 3 to 7 disks."),
            new GameCatalogEntry("kakuro", "Kakuro",
                "Cross-sums. A run of white squares must add up to the clue in the black square before it — the top-right number is the run to its right, the bottom-left number the run below — and no digit may repeat inside a run. Tap a white square, then tap a digit. A clue fades once its run is satisfied. Easy 6, Medium 7 and Hard 8 grids; every puzzle is generated from a completed grid, so an answer always exists."),
            new GameCatalogEntry("lightsout", "Lights Out",
                "Tapping a tile toggles it and its neighbours. Switch every light off to win the puzzle."),
            new GameCatalogEntry("loopwire", "Loop Wire",
                "Tap a piece to turn it a quarter-turn. Every open end has to meet another open end, with nothing left dangling and nothing pointing off the edge of the board. Boards are built from a solved layout and then scrambled, so there is always a way through."),
            new GameCatalogEntry("mahjong", "Mahjong Match",
                "Match pairs of identical tiles. A tile can only be taken when one of its left or right sides is clear. Boards are built by laying PAIRS down in reverse order, so every board can be cleared - if you get stuck, undo."),
            new GameCatalogEntry("mastermind", "Mastermind",
                "Crack the hidden colour code. After each guess you're told how many pegs are the right colour, and how many are also in the right place."),
            new GameCatalogEntry("maze", "Maze",
                "Find your way to the ★ exit. Pick a mode from the hub: a 30-level Campaign across 5 worlds (later worlds add FOG, where the walls only show near you, and LOCKED mazes where you must grab the key first), a deterministic Daily Maze (same for everyone) with a streak, or Endless for an infinite size ramp. Take the shortest path to earn up to 3 stars; stars and solves earn coins to spend on player-token skins in the Shop. Move with arrows, keyboard, or swipe."),
            new GameCatalogEntry("memory", "Memory",
                "Flip two cards at a time to find matching pairs. Remember the positions and clear the board in as few moves as you can."),
            new GameCatalogEntry("minesweeper", "Minesweeper",
                "Clear every square that isn't a mine. A number shows how many mines touch that square. Flag the mines and reveal the rest."),
            new GameCatalogEntry("nerdle", "Nerdle",
                "Wordle with maths: guess the hidden 8-character equation (like 12+35=47) in 6 tries. Every guess must itself be a TRUE equation — the tiles then show green (right character, right spot), amber (in the equation, wrong spot) or grey (not in it). Uses digits, + - * and =. Fewer guesses = higher score."),
            new GameCatalogEntry("nextinline", "Next in Line",
                "Five numbers follow a rule. Work out the rule and pick the number that comes next. The rule is explained after each answer, so the ones you miss are the ones you learn. Three lives."),
            new GameCatalogEntry("nim", "Nim",
                "Counters in rows. On your turn take as many as you like from ONE row; whoever takes the last counter wins. The computer plays the mathematically perfect strategy, so it never blunders - the board tells you whether the position you are in is won or lost, so you can learn to spot it."),
            new GameCatalogEntry("nonogram", "Nonograms",
                ""),
            new GameCatalogEntry("numslide", "Number Slide",
                "Slide the numbered tiles until they read 1 upward with the gap at the end. Tap any tile next to the gap to slide it in. The shuffle always walks the gap around a solved board, so every puzzle can definitely be solved. Three sizes; your fewest moves is kept for each."),
            new GameCatalogEntry("oddone", "Odd One Out",
                "Four things, one of which does not belong with the others. Pick it. The reason is explained either way, so a wrong answer still teaches you something."),
            new GameCatalogEntry("pacman", "Pac-Man",
                "Steer round the maze and eat every dot while dodging the three ghosts. Swipe the maze, use the arrow buttons, or arrow keys. The big dots are POWER pellets — for a few seconds the ghosts turn grey and you can eat THEM for 200 points. Clear the board to reach the next, slightly faster level. 3 lives; score 10 a dot, 50 a power pellet."),
            new GameCatalogEntry("pairup", "Pair Up",
                "Turn over two cards at a time and match each word to what it means. Everything stays face up once matched. Fewer turns is better, and your best is kept for each set."),
            new GameCatalogEntry("pegs", "Peg Solitaire",
                "The English board. Jump a peg over a neighbour into an empty hole and the jumped peg is removed. Tap the peg, then tap the hole. Getting down to one peg is a perfect game; most people stop around four."),
            new GameCatalogEntry("perfectcircle", "Perfect Circle",
                "Draw a circle in one stroke — on release it is scored 0–100% on roundness (radius consistency), how well the ends meet, and whether you drew a FULL loop (no cheating with a short arc). Pick a mode: Free Draw, a Tiny or Giant circle (match the target size), a timed Speed Draw, or the seeded Daily Challenge (same for everyone) with a streak. Climb an 8-tier rank ladder from Wobbly Line to Giotto, collect medals, and track your average in Stats."),
            new GameCatalogEntry("picpuzzle", "Picture Puzzle",
                "A jigsaw made for e-ink: a photo is sliced into 3×3 (or 4×4) tiles and shuffled — tap TWO tiles to swap them until the picture is rebuilt. Fewer moves = better score. A fresh photo every game."),
            new GameCatalogEntry("platformer", "Pixel Hop",
                "A Mario-style platformer with THREE levels that get harder. Hold ◄ / ► to run and tap Jump to leap (keyboard: arrow keys or Space; tap the screen to jump one-handed). Run and jump across the gaps — falling in a pit costs a life. Land ON TOP of a walking enemy to stomp it; touching one from the side hurts you. Grab the coin rings and reach the flag to clear a level and move to the next. Your coins and lives carry over; you respawn at the last ledge you stood on. Beat all three to win. Score = coins + stomps + a finishing bonus."),
            new GameCatalogEntry("quickcount", "Quick Count",
                "How many marks are on screen? Pick the right number from four. The grids get denser as you go, so counting one at a time stops working and you have to start grouping. Three lives."),
            new GameCatalogEntry("reversi", "Reversi",
                "The classic disc-flipping strategy game (Othello) — now a full campaign. Climb a 12-opponent Ladder, each smarter than the last (four AI strength tiers powered by a look-ahead search), earning up to 3 stars per foe by the size of your win. Beat foes to unlock the next, collect stars to unlock board themes, and finish the Ladder. There's a deterministic Daily Challenge (same game for everyone) with a streak, a Free Play difficulty picker, achievements, plus Undo and a limited Hint. You play the SOLID discs; grab the corners — they never flip. There is also two-player: pass-and-play on one device, or online against another Kindle. Two-player games are kept out of the Ladder, the Daily and the leaderboard."),
            new GameCatalogEntry("roller", "Lucky Roller",
                "Tap ROLL to pull random loot across seven rarities — from Common junk up to the max-rare COSMIC. Every item goes in your backpack; sell it (by rarity or Sell All) for coins. Spend coins on upgrades: Luck (better odds), Multi-roll (roll several at once), Value (higher sell prices), and Auto-roll (it rolls for you). Fill the Collection Log by discovering every named item. How rich — and how lucky — can you get?"),
            new GameCatalogEntry("set", "Set",
                ""),
            new GameCatalogEntry("simon", "Simon",
                "A memory brain-trainer. Watch the pads flash, then tap them back in order — each round adds a step. Pick a mode from the menu: Classic, Reverse (tap it backwards), Grid-6/Grid-9 (more pads), Speed Rush (faster), or Zen (3 lives). Earn XP to level up and unlock new modes, chase 17 achievements, and play the seeded Daily Challenge — the same sequence for everyone each day — to build a daily streak. Pads play tones on supported devices (toggle Sound in the menu)."),
            new GameCatalogEntry("slither", "Slither",
                "Drag on the board (or move the mouse / arrow keys) to steer your snake toward where you want to go. Eat the glowing dots to grow longer. Hold Boost for a burst of speed at the cost of a little length. You die if your head hits another snake's body or the world edge — but if another snake rams YOU, it dies and drops food. Score = dots eaten."),
            new GameCatalogEntry("snake", "Snake",
                "Steer with the arrow keys (or swipe). Eat the food to grow longer and score — but don't hit the walls or your own tail."),
            new GameCatalogEntry("snakesladders", "Snakes & Ladders",
                "Roll the die and race to the last square. Ladders send you up; snakes slide you back down."),
            new GameCatalogEntry("sokoban", "Sokoban",
                "A warehouse puzzle campaign — 23 hand-crafted levels in 4 star-gated packs. Push every box onto a ring target (PUSH only, never pull, one box at a time). Beat each level's move par to earn up to 3 stars, and collect stars to unlock the next pack. Undo (Z), Redo (Y), and Restart help you find the perfect solution; your best move-count is saved per level. There's a Level Select hub with Continue, an achievements wall, and a deterministic Daily Puzzle (same for everyone) with a streak. Move by swipe, arrows, or keyboard."),
            new GameCatalogEntry("solitaire", "Solitaire",
                "Build the four foundations up by suit from Ace to King. Stack the tableau down in alternating colours. Tap the stock to deal."),
            new GameCatalogEntry("spaceinv", "Space Invaders",
                "Move left/right and fire at the descending aliens. Clear each wave before it reaches the bottom."),
            new GameCatalogEntry("spellingbee", "Spelling Bee",
                "Make words of 4+ letters from the seven letters shown — every word MUST use the centre letter, and letters can repeat. 4-letter words score 1, longer words score their length, and a pangram (uses all 7) earns +7. The accepted word list is curated per puzzle. Rotates daily."),
            new GameCatalogEntry("strands", "Strands",
                "Every letter in the grid belongs to exactly one theme word. Tap adjacent letters (any direction) to chain a word — forwards or backwards — then Submit. Find every theme word to finish the board. A new board rotates in daily."),
            new GameCatalogEntry("stronghold", "Stronghold",
                "Build a base, train an army, and raid other players. Nothing runs in real time on screen: your mines and wells produce against the clock, so close the game and come back to collect. One builder means one job at a time, and no building may outrank your Keep. Raids hit a SAVED COPY of somebody's base, so nobody needs to be online and their buildings are never really destroyed - you take loot, they keep their base. Troops you send do not come back. Half the base is one star, the Keep is another, flattening it is the third. A clan is a shared chat room."),
            new GameCatalogEntry("sudoku", "Sudoku",
                "Fill the grid so every row, column and 3×3 box has 1–9 with no repeats. Pick a difficulty and solve with logic — no guessing needed."),
            new GameCatalogEntry("sumlines", "Sum Lines",
                "Put the digits 1 to 9 in the empty squares. A clue like A11 means the row to its right must add up to 11; D7 means the column below it must add up to 7. No digit repeats inside a single run. Tap a square to cycle it through 1-9 and back to blank — no keyboard needed."),
            new GameCatalogEntry("tetris", "Tetris",
                "Move and rotate the falling pieces to complete full horizontal lines, which clear and score. It ends if the stack reaches the top."),
            new GameCatalogEntry("tiletrader", "Tile Trader",
                "Buy goods cheaply in one town and sell them dearly in another. Prices differ from town to town and drift day by day, and each journey costs a day. You have thirty days and 120 coins to start. Whatever everything is worth at the end is your score."),
            new GameCatalogEntry("towerdef", "Tower Defence",
                "Place and upgrade towers along the path to stop waves of enemies reaching your castle. 8 towers, 8 enemy types, 40 waves."),
            new GameCatalogEntry("traffic", "Traffic Jam",
                "Slide the cars until the R car can drive out on the right. Cars only move along their own length. Tap a car, then tap the direction you want it to go. Every level was proved solvable by a solver, and par is the true minimum number of moves."),
            new GameCatalogEntry("trivia", "Trivia",
                "A multiple-choice general-knowledge quiz. Read the question, tap the answer you think is right. A correct answer scores a point and the right choice lights up; a wrong answer costs one of your three ♥ lives. Keep going until you run out of lives — then beat your best score. All questions are built in, so it works with no internet."),
            new GameCatalogEntry("ttt", "Tic-Tac-Toe",
                "Get three of your marks in a row — horizontal, vertical or diagonal — before your opponent does."),
            new GameCatalogEntry("wildforms", "Wildforms",
                "Collect, train and evolve creatures. Search a region to meet a wild Wildform, weaken it with your own, then throw a charm to catch it — the more hurt it is, the better your chances, but knock it out and you lose the catch. There are five types in a cycle: Leaf beats Wave, Wave beats Ember, Ember beats Gale, Gale beats Stone, Stone beats Leaf. Each move tells you whether it is strong or weak against what you are facing. Win fights to level up; some Wildforms evolve. Beat a region champion to open the next region. Your party is saved, so you can come back to it."),
            new GameCatalogEntry("wordladder", "Word Ladder",
                "Change ONE letter at a time to get from the first word to the last, and every step has to be a real word. Type the next word and press Add step. Undo takes a step back. The shortest known route is shown when you finish, so there is something to beat."),
            new GameCatalogEntry("wordle", "Wordle",
                "Guess the 5-letter word in 6 tries. Green = correct spot, yellow = wrong spot, grey = not in the word."),
            new GameCatalogEntry("wordsearch", "Word Search",
                "Find every hidden word in the letter grid. Words run in any direction — drag across the letters to mark one."),
            new GameCatalogEntry("yahtzee", "Yahtzee",
                "Roll five dice up to three times a turn — tap dice to HOLD them between rolls. Then pick a category to score it (each can be used once). Upper-section total of 63+ earns a +35 bonus; five of a kind is a 50-point YAHTZEE. Thirteen turns, highest total wins."),
        };

        return list;
    }

    /// <summary>Display name for a slug, falling back to the slug itself when unknown.</summary>
    public static string NameFor(string slug)
    {
        foreach (var g in All)
            if (string.Equals(g.Slug, slug, StringComparison.OrdinalIgnoreCase))
                return g.DisplayName;
        return slug;
    }
}
