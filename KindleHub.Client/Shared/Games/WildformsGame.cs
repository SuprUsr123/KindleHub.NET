using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Wildforms - collect, train and evolve creatures. Search a region to meet a
/// wild Wildform, weaken it with your own, then throw a charm: the more hurt it
/// is, the better the chance. Train caught creatures with berries and they
/// evolve past 120 CP. Catch three different species to win.
/// </summary>
public sealed class WildformsGame : GameBase
{
    private const int Cols = 3;
    private const int FightCell = 7;
    private const int CharmCell = 8;
    private const int FleeCell = 9;
    private const int ActiveCell = 10;
    private const int BerriesCell = 11;
    private const int SearchesCell = 12;
    private const int EndCell = 13;
    private const int PartyBase = 15;
    private const int PartySlots = 12;
    private const int SearchQuota = 25;
    private const int EncounterChance = 60;
    private const int EvolveAt = 120;

    private enum State { Searching, Battle, Over }

    private sealed record SpeciesInfo(string Name, string Glyph, string Evolved);

    // Two wild species per region, plus the starter your run begins with.
    private static readonly SpeciesInfo[] AllSpecies =
    {
        new("Spriggle", "◆", "Foxglove"),
        new("Barkhide", "■", "Treant"),
        new("Glowfin", "●", "Glowray"),
        new("Stonewyrm", "▓", "Basalt"),
        new("Puddlefin", "○", "Tidalfin"),
        new("Marshling", "✱", "Bogward"),
        new("Hopbit", "▲", "Jackhop"),
        new("Sunmane", "★", "Solarmane"),
        new("Craghorn", "⚑", "Avalanch"),
        new("Emberhoof", "♥", "Blazehoof"),
        new("Zephyrix", "↑", "Cyclonix"),
        new("Cloudkit", "○", "Stormcat"),
        new("Flarekit", "✱", "Pyrefox"),
    };

    private static readonly int Starter = 12;
    private static readonly int[][] RegionPools =
    {
        new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4, 5 },
        new[] { 6, 7 }, new[] { 8, 9 }, new[] { 10, 11 },
    };
    private static readonly string[] RegionNames =
    {
        "Forest", "Cave", "Lake", "Plains", "Mountain", "Sky",
    };

    private sealed class Creature
    {
        public int Species;
        public int Cp;
        public int Hp;
        public int Stage;
        public string Name => Stage == 1 ? AllSpecies[Species].Name : AllSpecies[Species].Evolved;
        public string Glyph => AllSpecies[Species].Glyph;
    }

    private readonly List<GameCell> _cells = new();
    private readonly List<Creature> _party = new();
    private readonly HashSet<int> _speciesCaught = new();
    private State _state = State.Searching;
    private int _active;
    private int _berries;
    private int _searches;
    private int _caught;
    private int _evolved;
    private int _enemySpecies;
    private int _enemyCp;
    private int _enemyHp;
    private bool _over;
    private bool _won;
    private string _msg = "";

    public WildformsGame()
    {
        for (int i = 0; i < Cols * 9; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "wildforms";
    public override string Name => "Wildforms";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score => 100 * _caught + 50 * _evolved;

    public override string? ResultText => !_over ? null
        : _won ? "Three species collected - you win!"
        : _caught > 0 ? $"{_caught} caught, {_evolved} evolved."
        : "Out of searches.";

    public override string StatusText
    {
        get
        {
            if (_over) return _won ? $"Champion collector · {Score} points." : $"Run over · {Score} points.";
            if (_state == State.Battle)
                return $"{AllSpecies[_enemySpecies].Name} {_enemyHp}/{_enemyCp} · yours {_party[_active].Name} {_party[_active].Hp}/{_party[_active].Cp}";
            string extra = _msg.Length > 0 ? _msg : "tap a region to search";
            string s = $"Caught {_caught} · species {_speciesCaught.Count}/3 · {extra}";
            if (_party.Count > PartySlots) s += $" · …and {_party.Count - PartySlots} more";
            return s;
        }
    }

    public override void Reset()
    {
        _party.Clear();
        _party.Add(new Creature { Species = Starter, Cp = 50, Hp = 50, Stage = 1 });
        _speciesCaught.Clear();
        _state = State.Searching;
        _active = 0;
        _berries = 0;
        _searches = SearchQuota;
        _caught = 0;
        _evolved = 0;
        _over = false;
        _won = false;
        _msg = "";
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (key != GameKey.Confirm) return false;
        return Fight();
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;

        if (index < 6) return Search(index);
        if (index == FightCell) return Fight();
        if (index == CharmCell) return Charm();
        if (index == FleeCell) return Flee();
        if (index == ActiveCell)
        {
            _active = (_active + 1) % _party.Count;
            _msg = "";
            Redraw();
            return true;
        }
        if (index == EndCell) return EndRun();
        if (index >= PartyBase && index < PartyBase + PartySlots) return Train(index - PartyBase);
        return false;
    }

    private bool Search(int region)
    {
        if (_state != State.Searching || _searches <= 0) return false;
        _searches--;
        if (Random.Shared.Next(100) < EncounterChance)
        {
            var pool = RegionPools[region];
            _enemySpecies = pool[Random.Shared.Next(pool.Length)];
            _enemyCp = 30 + Random.Shared.Next(51);
            _enemyHp = _enemyCp;
            _state = State.Battle;
            _msg = $"a wild {AllSpecies[_enemySpecies].Name}!";
        }
        else
        {
            _msg = "nothing stirring";
        }
        CheckExhausted();
        Redraw();
        return true;
    }

    private bool Fight()
    {
        if (_state != State.Battle) return false;
        // Every round fought earns a training berry, win or lose.
        _berries++;
        var mine = _party[_active];
        int myHit = Damage(mine.Cp);
        int foeHit = Damage(_enemyCp);
        _enemyHp -= myHit;
        if (_enemyHp <= 0)
        {
            _msg = "knocked out - no catch";
            EndBattle();
            Redraw();
            return true;
        }
        mine.Hp -= foeHit;
        if (mine.Hp <= 0)
        {
            _msg = $"{mine.Name} fainted!";
            EndBattle();
            Redraw();
            return true;
        }
        _msg = $"hit {myHit}, took {foeHit}";
        Redraw();
        return true;
    }

    private bool Charm()
    {
        if (_state != State.Battle) return false;
        // The charm sticks more often the lower the wild one's health.
        double chance = (1.0 - (double)_enemyHp / _enemyCp) * 0.65;
        if (Random.Shared.NextDouble() < chance)
        {
            Catch();
            return true;
        }
        var mine = _party[_active];
        mine.Hp -= Damage(_enemyCp);
        if (mine.Hp <= 0)
        {
            _msg = $"{mine.Name} fainted!";
            EndBattle();
        }
        else
        {
            _msg = "the charm failed";
        }
        Redraw();
        return true;
    }

    private bool Flee()
    {
        if (_state != State.Battle) return false;
        _msg = "got away safely";
        EndBattle();
        return true;
    }

    private void Catch()
    {
        _party.Add(new Creature { Species = _enemySpecies, Cp = _enemyCp, Hp = _enemyCp, Stage = 1 });
        _caught++;
        _speciesCaught.Add(_enemySpecies);
        _msg = $"caught {AllSpecies[_enemySpecies].Name}!";
        if (_speciesCaught.Count >= 3)
        {
            _over = true;
            _won = true;
            _state = State.Over;
            ScoreCounts = true;
            Redraw();
            return;
        }
        EndBattle();
    }

    private void EndBattle()
    {
        var mine = _party[_active];
        mine.Hp = mine.Cp;         // creatures patch up between fights
        _state = State.Searching;
        if (_searches <= 0)
        {
            _over = true;
            _state = State.Over;
            ScoreCounts = _caught >= 1;
        }
        Redraw();
    }

    private void CheckExhausted()
    {
        if (!_over && _searches <= 0 && _state != State.Battle)
        {
            _over = true;
            _state = State.Over;
            ScoreCounts = _caught >= 1;
        }
    }

    private bool EndRun()
    {
        if (_caught >= 1)
        {
            _over = true;
            _state = State.Over;
            ScoreCounts = true;
            Redraw();
            return true;
        }
        _msg = "catch one creature first";
        Redraw();
        return true;
    }

    private bool Train(int j)
    {
        if (j >= _party.Count) return false;
        if (_berries < 1)
        {
            _msg = "need a berry to train";
            Redraw();
            return true;
        }
        _berries--;
        var cr = _party[j];
        cr.Cp += 10;
        cr.Hp = cr.Cp;
        if (cr.Stage == 1 && cr.Cp >= EvolveAt)
        {
            cr.Stage = 2;
            cr.Cp += 50;
            cr.Hp = cr.Cp;
            _evolved++;
            _msg = $"{cr.Name} evolved!";
        }
        else
        {
            _msg = $"{cr.Name} +10 CP";
        }
        Redraw();
        return true;
    }

    /// <summary>Each blow deals 10-20% of the attacker's CP.</summary>
    private static int Damage(int cp) =>
        Math.Max(1, (int)Math.Round(cp * (0.10 + 0.10 * Random.Shared.NextDouble())));

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.Text = "";
            c.Background = Board;
            c.Foreground = Ink;
            c.IsEnabled = false;
            c.FontSize = 13;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
        }

        // Regions, each hinting the two species that live there.
        for (int r = 0; r < 6; r++)
        {
            var c = _cells[r];
            var pool = RegionPools[r];
            c.Text = $"{RegionNames[r]}\n{AllSpecies[pool[0]].Glyph} {AllSpecies[pool[1]].Glyph}";
            c.Background = _state == State.Searching && _searches > 0 ? Cell : Board;
            c.IsEnabled = _state == State.Searching && _searches > 0;
            c.FontSize = 12;
            c.Bold = true;
            c.BorderBrush = Accent;
            c.BorderThickness = new Avalonia.Thickness(1);
        }

        // Foe window or hint text.
        var foe = _cells[6];
        if (_state == State.Battle)
        {
            double pct = (double)_enemyHp / _enemyCp;
            foe.Text = $"{AllSpecies[_enemySpecies].Name} {AllSpecies[_enemySpecies].Glyph}\nHP {_enemyHp}/{_enemyCp}";
            foe.Background = pct > 0.5 ? Good : pct > 0.25 ? Warn : Bad;
            foe.Foreground = Cell;
            foe.FontSize = 11;
            foe.Bold = true;
        }
        else
        {
            foe.Text = _over ? "run over" : _msg.Length > 0 ? Ellipsis(_msg, 20) : "search a region";
            foe.Foreground = Muted;
            foe.FontSize = 11;
        }

        ActionButton(FightCell, "Fight", Bad, _state == State.Battle);
        ActionButton(CharmCell, "Charm ★", Accent, _state == State.Battle);
        ActionButton(FleeCell, "Flee", Cell, _state == State.Battle);

        var active = _cells[ActiveCell];
        active.Text = $"Active\n{Ellipsis(_party[_active].Name, 9)} {_party[_active].Cp}";
        active.Background = Cell;
        active.IsEnabled = !_over;
        active.FontSize = 11;
        active.Bold = true;

        var berries = _cells[BerriesCell];
        berries.Text = $"Berries\n{_berries}";
        berries.Background = Cell;
        berries.FontSize = 11;
        berries.Bold = true;

        var searches = _cells[SearchesCell];
        searches.Text = $"Left\n{_searches}";
        searches.Background = Cell;
        searches.FontSize = 11;
        searches.Bold = true;

        ActionButton(EndCell, "End", Cell, !_over);

        // The collection: tap any creature to train it with a berry.
        for (int j = 0; j < PartySlots; j++)
        {
            var c = _cells[PartyBase + j];
            if (j >= _party.Count) continue;
            var cr = _party[j];
            c.Text = $"{cr.Glyph} {Ellipsis(cr.Name, 7)}\nCP {cr.Cp}{(cr.Stage == 2 ? " ★" : "")}";
            c.Background = Cell;
            c.IsEnabled = !_over;
            c.FontSize = 10;
            c.Bold = j == _active;
            if (j == _active)
            {
                c.BorderBrush = Accent;
                c.BorderThickness = new Avalonia.Thickness(2);
            }
        }
    }

    private void ActionButton(int index, string text, IBrush bg, bool enabled)
    {
        var c = _cells[index];
        c.Text = text;
        c.Background = bg;
        c.Foreground = ReferenceEquals(bg, Cell) ? Ink : Cell;
        c.IsEnabled = enabled;
        c.FontSize = 13;
        c.Bold = true;
    }
}
