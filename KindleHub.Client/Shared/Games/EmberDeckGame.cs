using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace KindleHub.Client.Games;

/// <summary>
/// Ember Deck - a deck-building card battler in five fights. Each turn you get
/// 3 energy and draw to five cards; tap a card to play it, then End turn and the
/// foe swings back through your armor. Armor resets when your next turn starts,
/// exactly like block in the official client.
/// </summary>
public sealed class EmberDeckGame : GameBase
{
    private const int Cols = 6;
    private const int HandBase = 6;        // six card slots fill row 1
    private const int HandCap = 6;
    private const int DrawPerTurn = 5;
    private const int HeroHpMax = 50;

    private enum CardKind { Attack, Armor, Heal, Draw }

    private sealed record CardDef(string Name, int Cost, string Text, CardKind Kind, int Amount);

    private static readonly CardDef[] Cards =
    {
        new("Strike", 1, "6 dmg", CardKind.Attack, 6),
        new("Guard", 1, "5 armor", CardKind.Armor, 5),
        new("Ember", 2, "9 dmg", CardKind.Attack, 9),
        new("Mend", 1, "heal 6", CardKind.Heal, 6),
        new("Insight", 1, "draw 2", CardKind.Draw, 2),
    };

    // The starting deck: 4 Strike, 3 Guard, 2 Ember, 2 Mend, 1 Insight.
    private static readonly int[] DeckRecipe = { 0, 0, 0, 0, 1, 1, 1, 2, 2, 3, 3, 4 };

    private static readonly (string Name, int Hp, int Atk)[] Foes =
    {
        ("Goblin", 20, 4),
        ("Wolf", 26, 5),
        ("Brute", 34, 6),
        ("Wraith", 40, 7),
        ("Dragon", 55, 9),
    };

    private static readonly IBrush SpentBrush = new SolidColorBrush(Color.Parse("#d9d9e0"));

    private readonly List<GameCell> _cells = new();
    private readonly List<int> _deck = new();
    private readonly List<int> _hand = new();
    private readonly List<int> _discard = new();
    private int _foeIndex;
    private int _foeHp;
    private int _hp;
    private int _armor;
    private int _energy;
    private int _beaten;
    private bool _over;
    private bool _won;
    private string _msg = "";

    public EmberDeckGame()
    {
        for (int i = 0; i < Cols * 3; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "emberdeck";
    public override string Name => "Ember Deck";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score =>
        50 * _beaten + 2 * Math.Max(0, _hp) + (_won ? 200 : 0);

    public override string? ResultText => _won
        ? "All five foes beaten - the ember burns bright."
        : _over ? (_beaten > 0
            ? $"Fell to {Foes[Math.Min(_foeIndex, Foes.Length - 1)].Name} after {_beaten} foes."
            : "Fell at the very first foe.") : null;

    public override string StatusText
    {
        get
        {
            if (_over) return _won ? $"Victory · {Score} points." : $"Defeat · {Score} points.";
            string extra = _msg.Length > 0 ? $" · {_msg}" : "";
            return $"You {_hp} HP +{_armor} · {Foes[_foeIndex].Name} {_foeHp} HP · energy {_energy}/3{extra}";
        }
    }

    public override void Reset()
    {
        _deck.Clear();
        _hand.Clear();
        _discard.Clear();
        _deck.AddRange(DeckRecipe);
        Shuffle(_deck);
        _foeIndex = 0;
        _foeHp = Foes[0].Hp;
        _hp = HeroHpMax;
        _armor = 0;
        _beaten = 0;
        _over = false;
        _won = false;
        _msg = "";
        ScoreCounts = false;
        StartTurn();
        Redraw();
    }

    public override bool OnKey(GameKey key)
    {
        if (_over || key != GameKey.Confirm) return false;
        EndTurn();
        return true;
    }

    public override bool OnTap(int index)
    {
        if (_over || index < 0 || index >= _cells.Count) return false;
        if (index >= HandBase && index < HandBase + HandCap)
        {
            int i = index - HandBase;
            if (i >= _hand.Count) return false;
            PlayCard(i);
            return true;
        }
        if (index == 12)
        {
            EndTurn();
            return true;
        }
        return false;
    }

    private void StartTurn()
    {
        // Armor only survives until your next turn starts.
        _armor = 0;
        _energy = 3;
        DrawTo(DrawPerTurn);
    }

    private void DrawTo(int target)
    {
        while (_hand.Count < target)
        {
            if (_deck.Count == 0)
            {
                if (_discard.Count == 0) break;
                _deck.AddRange(_discard);
                _discard.Clear();
                Shuffle(_deck);
            }
            _hand.Add(_deck[^1]);
            _deck.RemoveAt(_deck.Count - 1);
        }
    }

    private void PlayCard(int handIndex)
    {
        int id = _hand[handIndex];
        var card = Cards[id];
        if (_energy < card.Cost)
        {
            _msg = "Not enough energy";
            Redraw();
            return;
        }

        _hand.RemoveAt(handIndex);
        _discard.Add(id);
        _energy -= card.Cost;

        switch (card.Kind)
        {
            case CardKind.Attack:
                _foeHp -= card.Amount;
                _msg = $"{Foes[_foeIndex].Name} takes {card.Amount}";
                if (_foeHp <= 0) FoeDown();
                break;
            case CardKind.Armor:
                _armor += card.Amount;
                _msg = $"+{card.Amount} armor";
                break;
            case CardKind.Heal:
                _hp = Math.Min(HeroHpMax, _hp + card.Amount);
                _msg = $"Healed {card.Amount}";
                break;
            case CardKind.Draw:
                DrawTo(Math.Min(HandCap, _hand.Count + card.Amount));
                _msg = "Drew cards";
                break;
        }
        Redraw();
    }

    private void FoeDown()
    {
        _beaten++;
        if (_beaten >= Foes.Length)
        {
            _over = true;
            _won = true;
            ScoreCounts = true;
            _msg = "All foes beaten!";
            return;
        }
        _hp = Math.Min(HeroHpMax, _hp + 10);
        _foeIndex++;
        _foeHp = Foes[_foeIndex].Hp;
        _msg = $"Foe down! Healed 10";
    }

    private void EndTurn()
    {
        var foe = Foes[_foeIndex];
        int dmg = Math.Max(0, foe.Atk - _armor);
        _hp -= dmg;
        _msg = dmg > 0 ? $"{foe.Name} hits for {dmg}" : "Armor soaked it all";
        if (_hp <= 0)
        {
            _over = true;
            ScoreCounts = _beaten >= 1;
            Redraw();
            return;
        }
        StartTurn();
        Redraw();
    }

    private static void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
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
            c.FontSize = 16;
            c.Bold = false;
            c.BorderBrush = null;
            c.BorderThickness = new Avalonia.Thickness(0);
        }

        // Foe banner.
        var foe = _cells[0];
        foe.Text = Foes[_foeIndex].Name;
        foe.Background = Bad;
        foe.Foreground = Cell;
        foe.FontSize = 15;
        foe.Bold = true;
        Info(1, $"HP\n{_foeHp}");
        Info(2, $"ATK\n{Foes[_foeIndex].Atk}");
        Info(3, $"Foe\n{_foeIndex + 1}/5");

        // Hand: affordable cards pop, the rest grey out.
        for (int i = 0; i < HandCap; i++)
        {
            var c = _cells[HandBase + i];
            if (i >= _hand.Count) continue;
            var card = Cards[_hand[i]];
            c.Text = $"{card.Name} {card.Cost}\n{card.Text}";
            c.FontSize = 12;
            bool affordable = !_over && _energy >= card.Cost;
            c.Background = affordable ? Cell : SpentBrush;
            c.Foreground = affordable ? Ink : Muted;
            c.IsEnabled = affordable;
            c.Bold = true;
            c.BorderBrush = affordable ? Accent : null;
            c.BorderThickness = affordable ? new Avalonia.Thickness(1) : new Avalonia.Thickness(0);
        }

        // Controls.
        var end = _cells[12];
        end.Text = "End turn";
        end.Background = Accent;
        end.Foreground = Cell;
        end.IsEnabled = !_over;
        end.FontSize = 14;
        end.Bold = true;
        Info(13, $"Energy\n{_energy}/3");
        Info(14, $"Deck\n{_deck.Count}");
        Info(15, $"Used\n{_discard.Count}");
        Info(16, $"You\n{_hp}{(_armor > 0 ? "+" + _armor : "")}");
    }

    private void Info(int index, string text)
    {
        var c = _cells[index];
        c.Text = text;
        c.Background = Cell;
        c.Foreground = Ink;
        c.FontSize = 12;
        c.Bold = true;
    }
}
