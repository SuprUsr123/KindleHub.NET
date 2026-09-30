using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>Lights Out — tap a tile to flip it and its neighbours; clear the board.</summary>
public sealed class LightsOutGame : GameBase
{
    public const int Side = 5;

    private readonly List<GameCell> _cells = new();
    private readonly bool[] _on = new bool[Side * Side];
    private int _moves;

    public LightsOutGame()
    {
        for (int i = 0; i < Side * Side; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "lightsout";
    public override string Name => "Lights Out";
    public override int Columns => Side;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override string? ResultText => Solved ? $"Solved in {_moves} moves." : null;

    public override string StatusText => Solved
        ? $"Solved in {_moves} moves — try for fewer."
        : $"Moves {_moves} · switch every light off";

    private bool Solved => _on.All(b => !b);

    public override void Reset()
    {
        // An all-off start state is already solved, so redraw until one is lit.
        do
        {
            for (int i = 0; i < _on.Length; i++) _on[i] = Random.Shared.Next(2) == 0;
        } while (!_on.Any(b => b));

        _moves = 0;
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _on.Length || Solved) return false;
        int x = index % Side, y = index / Side;
        foreach (var (dx, dy) in Neighbours(x, y))
        {
            if (dx < 0 || dy < 0 || dx >= Side || dy >= Side) continue;
            _on[dy * Side + dx] = !_on[dy * Side + dx];
        }
        _moves++;
        if (Solved) ScoreCounts = true;
        Redraw();
        return true;
    }

    /// <summary>The tapped tile plus its orthogonal neighbours — the classic rule.</summary>
    public static IEnumerable<(int dx, int dy)> Neighbours(int x, int y) => new[]
    {
        (x, y), (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1),
    };

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            bool on = _on[i];
            c.Text = on ? "●" : "";
            c.Background = on ? Tiles4[4] : Cell;
            c.Foreground = Cell;
            c.IsEnabled = !Solved;
            c.FontSize = 30;
            c.Bold = true;
        }
    }
}
