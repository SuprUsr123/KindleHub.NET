using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Every game ported from the official client, in the order the arcade shows them.
/// Adding a game means adding one line here plus its <see cref="IGame"/> class; the
/// Games page builds itself from this list.
/// </summary>
public static class GameRegistry
{
    private static readonly Func<IGame>[] _factories =
    {
        () => new SnakeGame(),
        () => new G2048Game(),
        () => new MemoryGame(),
        () => new MastermindGame(),
        () => new LightsOutGame(),
        () => new MinesweeperGame(),
        () => new SudokuGame(),
        () => new HangmanGame(),
        () => new SimonGame(),
        () => new WordleGame(),
        () => new HanoiGame(),
        () => new NimGame(),
        () => new PegsGame(),
        () => new NumberSlideGame(),
        () => new Connect4Game(),
        () => new ReversiGame(),
        () => new DotsBoxesGame(),
    };

    /// <summary>Slugs that have a real implementation, in display order.</summary>
    public static IReadOnlyList<string> PortedSlugs { get; } =
        _factories.Select(f => f().Slug).ToList();

    public static bool IsPorted(string slug) =>
        PortedSlugs.Any(s => string.Equals(s, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>Builds a fresh instance of a ported game, or null when it isn't ported yet.</summary>
    public static IGame? Create(string slug)
    {
        var factory = _factories.FirstOrDefault(f =>
            string.Equals(f().Slug, slug, StringComparison.OrdinalIgnoreCase));
        return factory?.Invoke();
    }
}
