using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Uno (crazy eights) against the computer, ported from the official client —
/// play a card that matches the discard's rank or suit, or any 8 to change the
/// suit. Nothing playable? Draw one card; if it plays you may play it or pass.
/// Empty your hand first.
/// </summary>
public sealed class Crazy8Game : GameBase
{
    private const int Cols = 10;
    private const int Rows = 6;
    private const int HandCap = 13;

    private const int DrawCell = 10;          // row 1, col 0
    private const int DiscardCell = 11;       // row 1, col 1
    private const int SuitCell = 12;          // row 1, col 2
    private const int PassCell = 46;          // row 4, col 6
    private static readonly int[] PickerCells = { 41, 42, 43, 44 }; // row 4, cols 1-4

    private readonly record struct Card(int Rank, int Suit); // rank 1..13, suit 0..3

    private static readonly string[] Suits = { "♠", "♥", "♦", "♣" };
    private static readonly string[] RankName = { "", "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };

    private readonly List<GameCell> _cells = new();
    private readonly List<Card> _deck = new();
    private readonly List<Card> _discard = new(); // top card is the last one
    private readonly List<Card> _hand = new();
    private readonly List<Card> _cpu = new();
    private int _suit;
    private bool _awaitSuit; // played an 8, suit not chosen yet
    private bool _canPass;   // drew a playable card: play it or pass
    private bool _over, _win;
    private string _msg = "Your turn.";

    public Crazy8Game()
    {
        for (int i = 0; i < Cols * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "crazy8";
    public override string Name => "Uno";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score => _win ? 300 + 5 * _cpu.Count : 0;

    public override string? ResultText => !_over ? null
        : _win ? $"You went out — the computer held {_cpu.Count} cards."
        : "The computer went out first.";

    public override string StatusText
    {
        get
        {
            if (_over) return ResultText ?? "Game over.";
            if (_awaitSuit) return "Pick a suit for your 8.";
            return $"You {_hand.Count} · CPU {_cpu.Count} · deck {_deck.Count} · {_msg}";
        }
    }

    public override void Reset()
    {
        _hand.Clear();
        _cpu.Clear();
        _discard.Clear();
        _deck.Clear();
        NewDeck();
        for (int i = 0; i < 7; i++)
        {
            _hand.Add(DrawOne()!.Value);
            _cpu.Add(DrawOne()!.Value);
        }

        Card start = _deck[^1];
        int guard = 0;
        while (start.Rank == 8 && guard++ < 100)
        {
            _deck.RemoveAt(_deck.Count - 1);
            _deck.Insert(Random.Shared.Next(_deck.Count), start);
            start = _deck[^1];
        }
        _deck.RemoveAt(_deck.Count - 1);
        _discard.Add(start);
        _suit = start.Suit;

        _awaitSuit = false;
        _canPass = false;
        _over = _win = false;
        ScoreCounts = false;
        _msg = "Match rank or suit · 8s are wild.";
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _cells.Count || _over) return false;

        if (_awaitSuit)
        {
            int picker = Array.IndexOf(PickerCells, index);
            if (picker < 0) return false;
            _suit = picker;
            _awaitSuit = false;
            _msg = $"Suit is {Suits[picker]}.";
            CpuTurn();
            Redraw();
            return true;
        }

        if (index >= 20 && index <= 32)
        {
            int k = index - 20;
            if (k >= _hand.Count) return false;
            var card = _hand[k];
            if (!Playable(card))
            {
                _msg = "That card doesn't match.";
                Redraw();
                return true;
            }
            _hand.RemoveAt(k);
            _discard.Add(card);
            if (card.Rank == 8)
            {
                _awaitSuit = true;
                _msg = "You played 8 — pick a suit.";
                Redraw();
                return true;
            }
            _suit = card.Suit;
            _canPass = false;
            if (_hand.Count == 0)
            {
                _over = _win = true;
                ScoreCounts = true;
                Redraw();
                return true;
            }
            _msg = $"You played {CardText(card)}.";
            CpuTurn();
            Redraw();
            return true;
        }

        if (index == DrawCell)
        {
            if (_canPass)
            {
                _msg = "Play the drawn card or pass.";
                Redraw();
                return true;
            }
            var drawn = DrawOne();
            if (drawn is null)
            {
                _canPass = true;
                _msg = "Nothing left to draw — pass.";
                Redraw();
                return true;
            }
            _hand.Add(drawn.Value);
            if (Playable(drawn.Value))
            {
                _canPass = true;
                _msg = $"Drew {CardText(drawn.Value)} — play it or pass.";
            }
            else
            {
                _canPass = false;
                _msg = $"Drew {CardText(drawn.Value)} — no play, turn passes.";
                CpuTurn();
            }
            Redraw();
            return true;
        }

        if (index == PassCell && _canPass)
        {
            _canPass = false;
            _msg = "You passed.";
            CpuTurn();
            Redraw();
            return true;
        }

        return false;
    }

    private bool Playable(Card c)
    {
        var top = _discard[^1];
        return c.Rank == top.Rank || c.Suit == _suit || c.Rank == 8;
    }

    /// <summary>Greedy: first plain match, an 8 only when stuck, else draw one and pass.</summary>
    private void CpuTurn()
    {
        while (true)
        {
            int pick = -1;
            for (int i = 0; i < _cpu.Count; i++)
                if (Playable(_cpu[i]) && _cpu[i].Rank != 8)
                {
                    pick = i;
                    break;
                }
            if (pick < 0)
                for (int i = 0; i < _cpu.Count; i++)
                    if (_cpu[i].Rank == 8)
                    {
                        pick = i;
                        break;
                    }

            if (pick >= 0)
            {
                var card = _cpu[pick];
                _cpu.RemoveAt(pick);
                _discard.Add(card);
                if (card.Rank == 8)
                {
                    _suit = BestCpuSuit();
                    _msg = $"CPU played 8 and chose {Suits[_suit]}.";
                }
                else
                {
                    _suit = card.Suit;
                    _msg = $"CPU played {CardText(card)}.";
                }
                if (_cpu.Count == 0)
                {
                    _over = true;
                    _win = false;
                }
                return;
            }

            var drawn = DrawOne();
            if (drawn is null)
            {
                _msg = "CPU passed.";
                return;
            }
            _cpu.Add(drawn.Value);
            if (!Playable(drawn.Value))
            {
                _msg = "CPU drew a card and passed.";
                return;
            }
        }
    }

    private int BestCpuSuit()
    {
        var counts = new int[4];
        foreach (var c in _cpu) counts[c.Suit]++;
        int best = 0;
        for (int s = 1; s < 4; s++)
            if (counts[s] > counts[best]) best = s;
        return counts[best] == 0 ? Random.Shared.Next(4) : best;
    }

    private Card? DrawOne()
    {
        if (_deck.Count == 0)
        {
            if (_discard.Count <= 1) return null;
            Card top = _discard[^1];
            _deck.AddRange(_discard.Take(_discard.Count - 1));
            _discard.Clear();
            _discard.Add(top);
            Shuffle(_deck);
        }
        var c = _deck[^1];
        _deck.RemoveAt(_deck.Count - 1);
        return c;
    }

    private void NewDeck()
    {
        _deck.Clear();
        for (int s = 0; s < 4; s++)
            for (int r = 1; r <= 13; r++)
                _deck.Add(new Card(r, s));
        Shuffle(_deck);
    }

    private static void Shuffle(List<Card> deck)
    {
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }

    private static string CardText(Card c) => RankName[c.Rank] + Suits[c.Suit];

    private static bool RedSuit(int suit) => suit is 1 or 2;

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

        // Computer hand: a count plus face-down backs.
        _cells[0].Text = "CPU";
        _cells[0].Foreground = Muted;
        _cells[0].FontSize = 11;
        var count = _cells[1];
        count.Text = _cpu.Count.ToString();
        count.FontSize = 16;
        count.Bold = true;
        for (int j = 0; j < 8 && j < _cpu.Count; j++)
        {
            var c = _cells[2 + j];
            c.Text = "▓";
            c.Background = Ink;
            c.Foreground = Cell;
            c.FontSize = 12;
        }

        // Draw pile, discard, current suit.
        var draw = _cells[DrawCell];
        draw.Text = _deck.Count > 0 || _discard.Count > 1 ? "▓" : "";
        draw.Background = Ink;
        draw.Foreground = Cell;
        draw.FontSize = 16;
        draw.IsEnabled = !_over && !_awaitSuit && !_canPass;
        if (_discard.Count > 0)
        {
            var top = _discard[^1];
            var d = _cells[DiscardCell];
            d.Text = CardText(top);
            d.Foreground = RedSuit(top.Suit) ? Bad : Ink;
            d.Background = Cell;
            d.FontSize = 18;
            d.Bold = true;
        }
        var suit = _cells[SuitCell];
        suit.Text = Suits[_suit];
        suit.Foreground = RedSuit(_suit) ? Bad : Ink;
        suit.FontSize = 20;
        suit.Bold = true;

        // Your hand (up to 13 cards across two rows); playable cards get a border.
        for (int k = 0; k < HandCap; k++)
        {
            var c = _cells[20 + k];
            if (k >= _hand.Count) continue;
            var card = _hand[k];
            c.Text = CardText(card);
            c.Foreground = RedSuit(card.Suit) ? Bad : Ink;
            c.Background = Cell;
            c.FontSize = 16;
            c.IsEnabled = !_over && !_awaitSuit;
            if (Playable(card))
            {
                c.BorderBrush = Accent;
                c.BorderThickness = new Avalonia.Thickness(1);
            }
        }

        // Suit picker + pass.
        _cells[40].Text = "Suit";
        _cells[40].Foreground = Muted;
        _cells[40].FontSize = 11;
        for (int s = 0; s < 4; s++)
        {
            var c = _cells[PickerCells[s]];
            c.Text = Suits[s];
            c.Foreground = RedSuit(s) ? Bad : Ink;
            c.FontSize = 20;
            c.Bold = true;
            c.IsEnabled = _awaitSuit;
            if (_awaitSuit)
            {
                c.Background = Cell;
                c.BorderBrush = Accent;
                c.BorderThickness = new Avalonia.Thickness(2);
            }
        }
        var pass = _cells[PassCell];
        pass.Text = "Pass";
        pass.FontSize = 13;
        pass.Bold = true;
        pass.IsEnabled = _canPass && !_over;
        pass.Background = _canPass && !_over ? Cell : Board;

        Spread(50, _msg, 8, 10);
    }

    /// <summary>Splits text evenly across adjacent cells so one row carries a sentence.</summary>
    private void Spread(int baseIndex, string text, int parts, int fontSize)
    {
        text = text.Trim();
        if (text.Length > parts * 7) text = Ellipsis(text, parts * 7);
        int chunk = (text.Length + parts - 1) / parts;
        for (int i = 0; i < parts; i++)
        {
            int at = i * chunk;
            _cells[baseIndex + i].Text = at < text.Length ? text[at..Math.Min(at + chunk, text.Length)] : "";
            _cells[baseIndex + i].FontSize = fontSize;
        }
    }
}
