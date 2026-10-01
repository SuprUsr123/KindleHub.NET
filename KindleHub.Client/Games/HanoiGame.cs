using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Tower of Hanoi — move the whole stack from the left peg to the right.
/// Tapping a peg lifts its top disk, tapping another drops it. The move count is
/// scored because the theoretical minimum is 2^n-1.
/// </summary>
public sealed class HanoiGame : GameBase
{
    private const int PegCount = 3;
    public const int Disks = 5;
    public const int Minimum = (1 << Disks) - 1; // 2^n - 1 = 31

    private readonly List<GameCell> _cells = new();
    private readonly List<int>[] _pegs = { new(), new(), new() };
    private int _lifted = -1;
    private int _liftedFrom = -1;
    private int _moves;

    public HanoiGame()
    {
        for (int i = 0; i < PegCount; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "hanoi";
    public override string Name => "Tower of Hanoi";
    public override int Columns => PegCount;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _moves;
    public override bool ScoreCounts => Solved;

    public override string? ResultText => Solved
        ? $"Solved in {_moves} moves — the best possible is {Minimum}."
        : null;

    public override string StatusText => Solved
        ? $"Solved in {_moves} moves (minimum {Minimum})."
        : _lifted >= 0
            ? $"Holding disk {_lifted + 1} · {_moves} moves"
            : $"{_moves} moves · minimum {Minimum} · tap a peg to lift its top disk";

    public bool Solved => _pegs[2].Count == Disks;

    public override void Reset()
    {
        for (int i = 0; i < PegCount; i++) _pegs[i].Clear();
        // Largest at the bottom of peg 0.
        for (int d = Disks; d >= 1; d--) _pegs[0].Add(d);
        _lifted = -1;
        _liftedFrom = -1;
        _moves = 0;
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (Solved || index < 0 || index >= PegCount) return false;

        if (_lifted < 0)
        {
            if (_pegs[index].Count == 0) return true;
            _lifted = _pegs[index][^1];
            _liftedFrom = index;
            _pegs[index].RemoveAt(_pegs[index].Count - 1);
            Redraw();
            return true;
        }

        // Tapping the same peg simply puts the held disk back. It is not a move.
        if (index == _liftedFrom)
        {
            _pegs[index].Add(_lifted);
            _lifted = -1;
            _liftedFrom = -1;
            Redraw();
            return true;
        }

        // Can only drop onto an empty peg or a larger disk.
        if (_pegs[index].Count > 0 && _pegs[index][^1] < _lifted) return true;
        _pegs[index].Add(_lifted);
        _lifted = -1;
        _liftedFrom = -1;
        _moves++;
        if (Solved) ScoreCounts = true;
        Redraw();
        return true;
    }

    public override void Redraw()
    {
        for (int p = 0; p < PegCount; p++)
        {
            var c = _cells[p];
            bool active = _lifted >= 0 ? p == DropTarget() : _pegs[p].Count > 0;
            c.IsEnabled = _pegs[p].Count > 0 || _lifted >= 0;
            c.Bold = false;
            c.FontSize = 15;
            c.Foreground = active ? Ink : Muted;
            c.Background = Board;

            // Render a real little tower: a vertical peg, bottom-aligned disks,
            // and a base. The old version drew only a stack of block characters,
            // which made the three columns look like enormous text labels.
            var stack = _pegs[p];
            var lines = new string[Disks + 1];
            int firstDiskRow = Disks - stack.Count;
            for (int row = 0; row < Disks; row++)
            {
                if (row < firstDiskRow)
                {
                    lines[row] = "    │";
                    continue;
                }

                int stackIndex = stack.Count - 1 - (row - firstDiskRow);
                int disk = stack[stackIndex];
                int width = 3 + disk * 2;
                lines[row] = new string('━', width);
            }
            lines[Disks] = "━━━━━━━━━━━━━━━━";
            c.Text = string.Join("\n", lines);
        }
    }

    /// <summary>Where the held disk would legally land, or -1 when nowhere will take it.</summary>
    private int DropTarget()
    {
        for (int p = 0; p < PegCount; p++)
            if (_pegs[p].Count == 0 || _pegs[p][^1] > _lifted) return p;
        return -1;
    }
}
