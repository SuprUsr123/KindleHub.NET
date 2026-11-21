using System;
using System.Collections.Generic;

namespace KindleHub.Client.Games;

/// <summary>
/// Picture Puzzle — a 4×4 picture is shuffled by swaps and rebuilt by tapping
/// two tiles at a time. Every tile is a unique shade/symbol pair: rows go from
/// light to dark and each column keeps its own symbol, so the target is
/// unambiguous and always reachable (swap-only shuffle).
/// </summary>
public sealed class PicPuzzleGame : GameBase
{
    private const int Side = 4;

    private const string Glyphs = "▲●■◆";

    // Four clearly stepped greys so rows read light → dark on e-ink.
    private static readonly Avalonia.Media.IBrush[] Shades =
    {
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#efe7d6")),
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#d9d9df")),
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#a9a9b3")),
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#70707a")),
    };

    private readonly List<GameCell> _cells = new();
    private readonly int[] _current = new int[Side * Side];
    private int _sel = -1;
    private int _moves;
    private bool _solved;
    private string _result = "";

    public PicPuzzleGame()
    {
        for (int i = 0; i < Side * Side; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "picpuzzle";
    public override string Name => "Picture Puzzle";
    public override int Columns => Side;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => Math.Max(50, 400 - 10 * _moves);
    public override string? ResultText => _solved ? _result : null;

    public override string StatusText => _solved
        ? $"Solved in {_moves} moves."
        : $"Moves {_moves} · rows light→dark, cols {Glyphs}";

    public override void Reset()
    {
        for (int i = 0; i < _current.Length; i++) _current[i] = i;

        // Shuffle purely by transpositions, so the target is always reachable.
        int swaps = 10 + Random.Shared.Next(11);
        for (int s = 0; s < swaps; s++)
        {
            int a = Random.Shared.Next(_current.Length);
            int b = Random.Shared.Next(_current.Length);
            if (a != b) (_current[a], _current[b]) = (_current[b], _current[a]);
        }
        if (IsSolved())
        {
            // All tiles are unique, so one swap always breaks a solved board.
            (_current[0], _current[1]) = (_current[1], _current[0]);
        }

        _sel = -1;
        _moves = 0;
        _solved = false;
        _result = "";
        ScoreCounts = false;
        Redraw();
    }

    public override bool OnTap(int index)
    {
        if (_solved || index < 0 || index >= _cells.Count) return false;

        if (_sel < 0)
        {
            _sel = index;
            Redraw();
            return true;
        }
        if (index == _sel)
        {
            _sel = -1;
            Redraw();
            return true;
        }

        (_current[_sel], _current[index]) = (_current[index], _current[_sel]);
        _sel = -1;
        _moves++;

        if (IsSolved())
        {
            _solved = true;
            ScoreCounts = true;
            _result = $"Solved in {_moves} moves.";
        }
        Redraw();
        return true;
    }

    private bool IsSolved()
    {
        for (int i = 0; i < _current.Length; i++)
            if (_current[i] != i) return false;
        return true;
    }

    public override void Redraw()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            int tile = _current[i];
            var c = _cells[i];
            c.Text = Glyphs[tile % Side].ToString();
            c.Background = Shades[tile / Side];
            c.Foreground = tile / Side < 2 ? Ink : Cell;
            c.IsEnabled = !_solved;
            c.Bold = i == _sel;
            c.FontSize = i == _sel ? 30 : 24;
        }
    }
}
