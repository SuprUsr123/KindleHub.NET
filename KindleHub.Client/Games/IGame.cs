using System;
using System.Collections.Generic;

namespace KindleHub.Client.Games;

/// <summary>
/// A game ported from the official KindleHub client and playable in-process.
///
/// The contract is deliberately small — a grid of cells plus three input methods —
/// because nearly every game in the arcade (Snake, 2048, Memory, Sudoku, Wordle,
/// Simon, …) is just those three things. Games hold their own state, mutate
/// <see cref="Cells"/> in place and let the view re-read it each frame, so there is
/// no per-game view, no per-game XAML and no command wiring to repeat.
/// </summary>
public interface IGame
{
    /// <summary>Catalog slug, so scores post under the same key the website uses.</summary>
    string Slug { get; }

    string Name { get; }

    /// <summary>Grid width. Cells are filled left-to-right, top-to-bottom.</summary>
    int Columns { get; }

    /// <summary>The board, in render order. Mutated in place; never reallocated per frame.</summary>
    IReadOnlyList<GameCell> Cells { get; }

    /// <summary>Status line under the board — whose turn, what to do, score.</summary>
    string StatusText { get; }

    /// <summary>Result line, shown in colour once the game ends.</summary>
    string? ResultText { get; }

    /// <summary>True when the game is finished and the player did well enough to score.</summary>
    bool ScoreCounts { get; }

    /// <summary>What the finished game is worth. Meaningless unless <see cref="ScoreCounts"/>.</summary>
    int Score { get; }

    /// <summary>True for games that advance on a timer (Snake, Tetris) rather than on input.</summary>
    bool IsRealTime { get; }

    /// <summary>Starts a fresh game. Must fully reset state — the view calls this for "New game".</summary>
    void Reset();

    /// <summary>
    /// Advances the game by one frame. The view drives this on a timer for every
    /// game, not just real-time ones, so animations that need to settle (Memory's
    /// flip-back) are timer-driven rather than spawning threads.
    /// </summary>
    void Tick(TimeSpan elapsed);

    /// <summary>Repaints <see cref="Cells"/> from the game's current state.</summary>
    void Redraw();

    /// <summary>Handles a key press. Return true if the game consumed it.</summary>
    bool OnKey(GameKey key);

    /// <summary>Handles a tick on the cell at <paramref name="index"/> in <see cref="Cells"/>.</summary>
    bool OnTap(int index);
}
