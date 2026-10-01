using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Hangman — guess the hidden word a letter at a time. The alphabet is laid out as
/// board cells (26 of them in 13 columns), so this needs no separate keyboard
/// control; the word slots sit above it in the same grid.
/// </summary>
public sealed class HangmanGame : GameBase
{
    public const int WordLength = 6;
    public const int AlphabetCols = 13;
    public const int WordCols = WordLength;
    public const int TotalCols = AlphabetCols;

    private static readonly string[] Words =
    {
        "PLANET","MARKET","GARDEN","CASTLE","BRIDGE","BASKET","COPPER","DRAGON",
        "FALCON","GINGER","HARBOR","ISLAND","JACKET","LEAVES","MEADOW","NOBODY",
        "ORCHID","OTTERS","QUARRY","RIBBON","SADDLE","TEMPLE","URCHIN","VIOLIN",
        "WALNUT","YARDEN","ZEBRAS","ANCHOR","BREEZE","CANDLE","DESIGN","ECHOES",
    };

    /// <summary>Exposed so the self-test can assert every word is exactly
    /// <see cref="WordLength"/>. A short word threw on the last index; a long one
    /// made the game unwinnable, and both were shipped for a while.</summary>
    public static IReadOnlyList<string> AllWords => Words;

    private readonly List<GameCell> _cells = new();
    private string _word = "";
    private readonly char[] _slots = new char[WordLength];
    private readonly bool[] _used = new bool[26];
    private int _lives = 6;
    private int _revealed;
    private bool _lost;
    private bool _won;

    public HangmanGame()
    {
        for (int i = 0; i < AlphabetOffset; i++) _cells.Add(Blank()); // word row + gutter
        for (int i = 0; i < 26; i++) _cells.Add(Blank());
        Reset();
    }

    /// <summary>First alphabet cell, after the word row and four drawing rows.</summary>
    public static int AlphabetOffset => 5 * AlphabetCols;

    public override string Slug => "hangman";
    public override string Name => "Hangman";
    public override int Columns => TotalCols;
    public override IReadOnlyList<GameCell> Cells => _cells;

    public override string? ResultText
    {
        get
        {
            if (_won) return $"Solved — {_word} with {_lives} lives left.";
            return _lost ? $"Out of lives — the word was {_word}." : null;
        }
    }

    public override string StatusText
    {
        get
        {
            if (_won) return "Solved. New game deals a new word.";
            if (_lost) return "Out of lives. New game deals a new word.";
            return $"{_lives} lives · {_revealed}/{WordLength} letters found · type A–Z or tap a letter";
        }
    }

    public override void Reset()
    {
        _word = Words[Random.Shared.Next(Words.Length)];
        Array.Clear(_slots, 0, WordLength);
        Array.Clear(_used, 0, 26);
        _lives = 6;
        _revealed = 0;
        _lost = _won = false;
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnLetter(char letter)
    {
        letter = char.ToUpperInvariant(letter);
        if (letter < 'A' || letter > 'Z') return false;
        return GuessLetter(letter - 'A');
    }

    public override bool OnTap(int index)
    {
        // Cells are laid out as [word row][gutter][alphabet].
        int letter = index - AlphabetOffset;
        if (letter < 0 || letter >= 26) return false;
        return GuessLetter(letter);
    }

    private bool GuessLetter(int letter)
    {
        if (_won || _lost || letter < 0 || letter >= 26) return false;
        if (_used[letter]) return true;

        _used[letter] = true;
        bool hit = false;
        for (int i = 0; i < WordLength; i++)
        {
            if (_word[i] != (char)('A' + letter)) continue;
            _slots[i] = _word[i];
            _revealed++;
            hit = true;
        }
        if (!hit) _lives--;

        if (_revealed == WordLength) { _won = true; ScoreCounts = true; }
        else if (_lives <= 0) _lost = true;
        Redraw();
        return true;
    }

    public override void Redraw()
    {
        // Row 0: the word slots, laid out inside the 13-wide grid.
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            c.IsEnabled = false;
            c.Bold = true;
            c.FontSize = 20;
            c.Foreground = Ink;
            c.Background = Board;
        }

        // Centre the word row: 13 columns, 6 slots.
        int start = (AlphabetCols - WordLength) / 2;
        for (int i = 0; i < WordLength; i++)
        {
            var c = _cells[start + i];
            bool shown = _slots[i] != '\0';
            c.Text = shown ? _slots[i].ToString() : "＿";
            c.Background = Cell;
            c.Foreground = shown ? Ink : Muted;
            c.FontSize = 26;
            c.Bold = true;
        }
        // Show a won word immediately, and a lost one as the full answer.
        for (int i = 0; i < WordLength; i++)
        {
            if (_won || _lost) _cells[start + i].Text = _word[i].ToString();
        }

        // Four clear drawing rows give the game a visual progress cue while
        // leaving two generous rows for the clickable alphabet.
        DrawArt(1, 4, "┌", Muted);
        for (int col = 5; col <= 8; col++) DrawArt(1, col, "─", Muted);
        DrawArt(1, 9, "┐", Muted);
        DrawArt(2, 4, "│", Muted);
        DrawArt(2, 9, "│", Muted);
        DrawArt(3, 4, "│", Muted);
        DrawArt(3, 9, "│", Muted);
        for (int col = 2; col <= 11; col++) DrawArt(4, col, "━", Muted);

        int misses = 6 - _lives;
        if (misses >= 1) DrawArt(2, 9, "●", Bad);
        if (misses >= 2) DrawArt(3, 9, "│", Bad);
        if (misses >= 3) DrawArt(3, 8, "╱", Bad);
        if (misses >= 4) DrawArt(3, 10, "╲", Bad);
        if (misses >= 5) DrawArt(4, 8, "╱", Bad);
        if (misses >= 6) DrawArt(4, 10, "╲", Bad);

        // Rows 5 and 6: the alphabet.
        int alphaBase = AlphabetOffset;
        for (int i = 0; i < 26; i++)
        {
            var c = _cells[alphaBase + i];
            char ch = (char)('A' + i);
            c.Text = ch.ToString();
            c.FontSize = 16;
            c.Bold = false;
            c.Foreground = _used[i] ? Muted : Ink;
            c.Background = Cell;
            c.IsEnabled = !_used[i] && !_won && !_lost;
            if (_used[i])
            {
                // Colour the guess by whether it was in the word.
                bool inWord = _word.Contains(ch);
                c.Foreground = inWord ? Good : Bad;
                c.Background = Board;
            }
        }
    }

    private void DrawArt(int row, int col, string text, Avalonia.Media.IBrush foreground)
    {
        var cell = _cells[row * AlphabetCols + col];
        cell.Text = text;
        cell.Foreground = foreground;
        cell.Background = Board;
        cell.FontSize = 23;
        cell.Bold = true;
    }
}
