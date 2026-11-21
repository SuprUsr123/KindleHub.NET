using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Battleship, ported from the official client. Both fleets are placed at random
/// so play starts immediately: fire at the lower grid, the computer returns fire
/// on the upper one and keeps hunting around every hit until the ship goes down.
/// Sink all 17 enemy ship squares before it sinks yours.
/// </summary>
public sealed class BattleshipGame : GameBase
{
    private const int Side = 10;
    private const int DividerRow = 10;
    private const int EnemyTop = 11;
    private const int TotalRows = 21;
    private const int FleetCells = 17;
    private static readonly int[] ShipLengths = { 5, 4, 3, 3, 2 };
    private static readonly (int Dx, int Dy)[] Neighbours =
        { (1, 0), (-1, 0), (0, 1), (0, -1) };

    private readonly List<GameCell> _cells = new();
    private readonly List<int>[] _myShips = new List<int>[ShipLengths.Length];
    private readonly List<int>[] _foeShips = new List<int>[ShipLengths.Length];
    private readonly HashSet<int> _myShots = new();   // fired at the enemy grid (0..99)
    private readonly HashSet<int> _foeShots = new();  // fired at my grid
    private readonly List<int> _hunt = new();         // computer's pending targets
    private readonly List<int> _openHits = new();     // my hit squares not yet sunk
    private bool[] _mySunk = new bool[ShipLengths.Length];
    private bool[] _foeSunk = new bool[ShipLengths.Length];
    private int _myHits;
    private int _foeHits;
    private bool _over;
    private bool _win;
    private string _msg = "Fleets placed.";

    public BattleshipGame()
    {
        for (int i = 0; i < Side * TotalRows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "battleship";
    public override string Name => "Battleship";
    public override int Columns => Side;
    public override IReadOnlyList<GameCell> Cells => _cells;

    /// <summary>500 for the win plus a bonus that shrinks by 3 per wasted shot.</summary>
    public override int Score => _win ? 500 + Math.Max(0, 100 - (_myShots.Count - FleetCells) * 3) : 0;

    public override string? ResultText => !_over ? null
        : _win ? $"Enemy fleet destroyed in {_myShots.Count} shots."
        : "Your fleet is destroyed.";

    public override string StatusText => _over
        ? (ResultText ?? "")
        : $"{_msg} · you {_myHits}/{FleetCells} · CPU {_foeHits}/{FleetCells} · fire on the lower grid";

    public override void Reset()
    {
        _myShots.Clear();
        _foeShots.Clear();
        _hunt.Clear();
        _openHits.Clear();
        _mySunk = new bool[ShipLengths.Length];
        _foeSunk = new bool[ShipLengths.Length];
        _myHits = _foeHits = 0;
        _over = _win = false;
        ScoreCounts = false;
        _msg = "Fleets placed.";
        PlaceFleet(_myShips);
        PlaceFleet(_foeShips);
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _cells.Count) return false;

        int row = index / Side, col = index % Side;
        if (row == DividerRow && col <= 1)
        {
            Reset(); // "Re-place ⟲" deals two fresh fleets
            return true;
        }

        if (_over || row < EnemyTop) return false;

        int target = (row - EnemyTop) * Side + col;
        if (_myShots.Contains(target)) return false;

        _myShots.Add(target);
        int ship = ShipAt(_foeShips, target);
        if (ship >= 0)
        {
            _myHits++;
            if (_foeShips[ship].All(c => _myShots.Contains(c)))
            {
                _foeSunk[ship] = true;
                _msg = "You sank a ship!";
            }
            else _msg = "Hit!";

            if (_myHits >= FleetCells)
            {
                _over = _win = true;
                ScoreCounts = true;
                Redraw();
                return true;
            }
        }
        else _msg = "Miss.";

        ComputerFire();
        if (!_over && _foeHits >= FleetCells) _over = true;
        Redraw();
        return true;
    }

    /// <summary>Random shot, or a neighbour of an unresolved hit while hunting.</summary>
    private void ComputerFire()
    {
        int target = -1;
        while (_hunt.Count > 0)
        {
            int t = _hunt[0];
            _hunt.RemoveAt(0);
            if (!_foeShots.Contains(t)) { target = t; break; }
        }
        if (target < 0)
        {
            var open = Enumerable.Range(0, Side * Side).Where(i => !_foeShots.Contains(i)).ToList();
            if (open.Count == 0) return;
            target = open[Random.Shared.Next(open.Count)];
        }

        _foeShots.Add(target);
        int ship = ShipAt(_myShips, target);
        if (ship < 0) return;

        _foeHits++;
        _openHits.Add(target);
        if (_myShips[ship].All(c => _foeShots.Contains(c)))
        {
            _mySunk[ship] = true;
            _msg = "CPU sank your ship!";
            foreach (var c in _myShips[ship]) _openHits.Remove(c);
            RebuildHunt();
        }
        else
        {
            _msg = "CPU hits!";
            foreach (var (dx, dy) in Neighbours)
            {
                int nx = target % Side + dx, ny = target / Side + dy;
                if (nx < 0 || nx >= Side || ny < 0 || ny >= Side) continue;
                int n = ny * Side + nx;
                if (!_foeShots.Contains(n)) _hunt.Add(n);
            }
        }
    }

    private void RebuildHunt()
    {
        _hunt.Clear();
        foreach (var h in _openHits)
            foreach (var (dx, dy) in Neighbours)
            {
                int nx = h % Side + dx, ny = h / Side + dy;
                if (nx < 0 || nx >= Side || ny < 0 || ny >= Side) continue;
                int n = ny * Side + nx;
                if (!_foeShots.Contains(n)) _hunt.Add(n);
            }
    }

    private static void PlaceFleet(List<int>[] ships)
    {
        var used = new HashSet<int>();
        for (int s = 0; s < ships.Length; s++)
        {
            int len = ShipLengths[s];
            ships[s] = new List<int>();
            bool placed = false;
            for (int tries = 0; tries < 400 && !placed; tries++)
            {
                bool horiz = Random.Shared.Next(2) == 0;
                int x = Random.Shared.Next(horiz ? Side - len + 1 : Side);
                int y = Random.Shared.Next(horiz ? Side : Side - len + 1);
                var cells = new List<int>();
                for (int k = 0; k < len; k++)
                    cells.Add((y + (horiz ? 0 : k)) * Side + x + (horiz ? k : 0));
                if (cells.Any(used.Contains)) continue;
                ships[s] = cells;
                foreach (var c in cells) used.Add(c);
                placed = true;
            }
            // Deterministic fallback so a fleet can never end up short.
            for (int y = 0; y < Side && !placed; y++)
                for (int x = 0; x < Side && !placed; x++)
                    foreach (bool horiz in new[] { true, false })
                    {
                        if (x + (horiz ? len : 0) > Side || y + (horiz ? 0 : len) > Side) continue;
                        var cells = new List<int>();
                        for (int k = 0; k < len; k++)
                            cells.Add((y + (horiz ? 0 : k)) * Side + x + (horiz ? k : 0));
                        if (cells.Any(used.Contains)) continue;
                        ships[s] = cells;
                        foreach (var c in cells) used.Add(c);
                        placed = true;
                        break;
                    }
        }
    }

    private static int ShipAt(List<int>[] ships, int cell)
    {
        for (int s = 0; s < ships.Length; s++)
            if (ships[s].Contains(cell)) return s;
        return -1;
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = false;
            c.FontSize = 14;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
        }

        // Your fleet: rows 0..9. Ships are visible; CPU shots land here.
        for (int i = 0; i < Side * Side; i++)
        {
            var c = _cells[i];
            bool hit = _foeShots.Contains(i);
            if (ShipAt(_myShips, i) >= 0 && hit)
            {
                c.Text = "✱";
                c.Background = Bad;
                c.Foreground = Cell;
                c.Bold = true;
            }
            else if (ShipAt(_myShips, i) >= 0)
            {
                c.Background = Ink;
            }
            else if (hit)
            {
                c.Text = "·";
                c.Background = Muted;
                c.Foreground = Cell;
            }
        }

        // Divider row carries the re-place button.
        var label = _cells[DividerRow * Side];
        label.Text = "Re-place";
        label.FontSize = 9;
        label.Background = Cell;
        label.IsEnabled = true;
        var reroll = _cells[DividerRow * Side + 1];
        reroll.Text = "⟲";
        reroll.FontSize = 14;
        reroll.Foreground = Accent;
        reroll.Background = Cell;
        reroll.IsEnabled = true;

        // Enemy waters: rows 11..20, every unknown square is tappable.
        for (int i = 0; i < Side * Side; i++)
        {
            var c = _cells[(EnemyTop + i / Side) * Side + i % Side];
            bool fired = _myShots.Contains(i);
            bool isHit = fired && ShipAt(_foeShips, i) >= 0;
            if (fired)
            {
                c.Text = isHit ? "✱" : "·";
                c.Background = isHit ? Bad : Muted;
                c.Foreground = Cell;
                c.Bold = isHit;
            }
            else
            {
                c.Background = Cell;
                c.IsEnabled = !_over;
            }
        }
    }
}
