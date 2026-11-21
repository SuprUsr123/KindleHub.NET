using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Blackjack, ported from the official client — ten rounds against the dealer,
/// starting with 100 chips and a flat 10-chip bet. Face cards count ten, an ace
/// is one or eleven, the dealer draws to 17, and a natural blackjack pays 3:2.
/// </summary>
public sealed class BlackjackGame : GameBase
{
    private const int Cols = 10;
    private const int Rows = 5;
    private const int TotalRounds = 10;
    private const int StartChips = 100;
    private const int Bet = 10;

    private const int DealBtn = 31, HitBtn = 32, StandBtn = 33;

    private readonly record struct Card(int Rank, int Suit); // rank 1..13, suit 0..3

    private static readonly string[] Suits = { "♠", "♥", "♦", "♣" };
    private static readonly string[] RankName = { "", "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };

    private readonly List<GameCell> _cells = new();
    private readonly List<Card> _deck = new();
    private readonly List<Card> _dealer = new();
    private readonly List<Card> _player = new();
    private bool _revealed;
    private bool _active;
    private bool _over;
    private int _chips = StartChips;
    private int _round;
    private string _msg = "Tap Deal to start.";

    public BlackjackGame()
    {
        for (int i = 0; i < Cols * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "blackjack";
    public override string Name => "Blackjack";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override int Score => _over ? Math.Max(1, _chips) : 0;

    public override string? ResultText => !_over ? null
        : _chips < Bet ? "Out of chips."
        : $"{TotalRounds} rounds done — {_chips} chips.";

    public override string StatusText
    {
        get
        {
            if (_over) return ResultText ?? "Game over.";
            if (_active)
                return $"You {Value(_player)} · dealer {(Value(_dealer.Take(1)).ToString())} · hit or stand";
            return $"Chips {_chips} · round {Math.Min(_round + 1, TotalRounds)}/{TotalRounds} · tap Deal";
        }
    }

    public override void Reset()
    {
        _deck.Clear();
        _dealer.Clear();
        _player.Clear();
        NewDeck();
        _revealed = false;
        _active = false;
        _over = false;
        _chips = StartChips;
        _round = 0;
        ScoreCounts = false;
        _msg = "Tap Deal to start.";
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (index < 0 || index >= _cells.Count || _over) return false;
        if (index == DealBtn) return Deal();
        if (!_active) return false;
        if (index == HitBtn) return Hit();
        if (index == StandBtn) return Stand();
        return false;
    }

    private bool Deal()
    {
        if (_active || _chips < Bet) return false;
        _dealer.Clear();
        _player.Clear();
        _player.Add(Draw());
        _dealer.Add(Draw());
        _player.Add(Draw());
        _dealer.Add(Draw());
        _revealed = false;
        _active = true;

        bool playerBJ = Value(_player) == 21;
        bool dealerBJ = Value(_dealer) == 21;
        if (playerBJ || dealerBJ)
        {
            _revealed = true;
            if (playerBJ && dealerBJ) _msg = "Both blackjack — push.";
            else if (playerBJ) { _chips += 15; _msg = "Blackjack! +15"; }
            else { _chips -= Bet; _msg = "Dealer blackjack. -10"; }
            EndRound();
        }
        else _msg = "Hit or stand?";
        Redraw();
        return true;
    }

    private bool Hit()
    {
        _player.Add(Draw());
        int v = Value(_player);
        if (v > 21)
        {
            _revealed = true;
            _chips -= Bet;
            _msg = $"Bust with {v}. -10";
            EndRound();
        }
        else if (v == 21)
        {
            _msg = "21!";
            Stand();
        }
        else _msg = $"You have {v}.";
        Redraw();
        return true;
    }

    private bool Stand()
    {
        _revealed = true;
        int d = Value(_dealer);
        while (d < 17)
        {
            _dealer.Add(Draw());
            d = Value(_dealer);
        }

        int p = Value(_player);
        if (d > 21) { _chips += Bet; _msg = $"Dealer busts with {d}. +10"; }
        else if (p > d) { _chips += Bet; _msg = $"{p} beats {d}. +10"; }
        else if (p == d) { _msg = $"Push at {p}."; }
        else { _chips -= Bet; _msg = $"{d} beats {p}. -10"; }
        EndRound();
        Redraw();
        return true;
    }

    private void EndRound()
    {
        _active = false;
        _round++;
        if (_round >= TotalRounds || _chips < Bet)
        {
            _over = true;
            ScoreCounts = true;
        }
    }

    private Card Draw()
    {
        if (_deck.Count < 15) NewDeck();
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
        for (int i = _deck.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (_deck[i], _deck[j]) = (_deck[j], _deck[i]);
        }
    }

    /// <summary>Best hand value ≤ 21 where an ace counts 11 when it helps.</summary>
    private static int Value(IEnumerable<Card> hand)
    {
        int sum = 0, aces = 0;
        foreach (var c in hand)
        {
            int v = c.Rank >= 10 ? 10 : c.Rank;
            sum += v;
            if (c.Rank == 1) aces++;
        }
        while (aces > 0 && sum + 10 <= 21)
        {
            sum += 10;
            aces--;
        }
        return sum;
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

        Label(0, "Dealer");
        for (int j = 0; j < 6 && j < _dealer.Count; j++)
        {
            var c = _cells[1 + j];
            if (j == 1 && !_revealed)
            {
                c.Text = "▓";
                c.Background = Ink;
                c.Foreground = Cell;
            }
            else
            {
                var card = _dealer[j];
                c.Text = CardText(card);
                c.Foreground = RedSuit(card.Suit) ? Bad : Ink;
                c.Background = Cell;
            }
            c.FontSize = 16;
        }
        if (_dealer.Count > 0)
        {
            var t = _cells[7];
            int v = _revealed ? Value(_dealer) : Value(_dealer.Take(1));
            t.Text = v.ToString();
            t.FontSize = 13;
            t.Bold = true;
            t.Foreground = _revealed && v > 21 ? Bad : Ink;
        }

        Label(20, "You");
        for (int j = 0; j < 6 && j < _player.Count; j++)
        {
            var c = _cells[21 + j];
            var card = _player[j];
            c.Text = CardText(card);
            c.Foreground = RedSuit(card.Suit) ? Bad : Ink;
            c.Background = Cell;
            c.FontSize = 16;
        }
        if (_player.Count > 0)
        {
            var t = _cells[27];
            int v = Value(_player);
            t.Text = v.ToString();
            t.FontSize = 13;
            t.Bold = true;
            t.Foreground = v > 21 ? Bad : Ink;
        }

        Button(DealBtn, "Deal", !_over && !_active && _chips >= Bet);
        Button(HitBtn, "Hit", _active);
        Button(StandBtn, "Stand", _active);

        Label(40, "Chips");
        var chips = _cells[41];
        chips.Text = _chips.ToString();
        chips.FontSize = 14;
        chips.Bold = true;
        Label(42, "Round");
        var round = _cells[43];
        round.Text = $"{Math.Min(_round + (_over ? 0 : 1), TotalRounds)}/{TotalRounds}";
        round.FontSize = 12;
        Spread(44, _msg, 6, 10);
    }

    private void Label(int index, string text)
    {
        var c = _cells[index];
        c.Text = text;
        c.Foreground = Muted;
        c.FontSize = 11;
    }

    private void Button(int index, string text, bool enabled)
    {
        var c = _cells[index];
        c.Text = text;
        c.FontSize = 14;
        c.Bold = true;
        c.Background = enabled ? Cell : Board;
        c.IsEnabled = enabled;
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
