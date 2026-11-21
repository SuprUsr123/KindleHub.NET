using System;
using System.Collections.Generic;
using System.Linq;

namespace KindleHub.Client.Games;

/// <summary>
/// Traffic Jam (Rush Hour) — slide the cars along their own lanes until the red
/// R car can drive out on the right. Tap a car to grab it, then tap the square
/// it should move to; six hand-designed levels, each verified solvable.
/// </summary>
public sealed class TrafficGame : GameBase
{
    private const int Side = 6;
    private const int Rows = 7;      // board + one button row
    private const int Levels = 6;
    private const int EndCell = 6 * Side + 4;

    private sealed class Car
    {
        public char Letter = 'A';
        public int X, Y, Len = 2;
        public bool Vertical, Red;
    }

    // (letter, x, y, length, vertical?, red?). The red car always sits in row 2.
    private static readonly (char letter, int x, int y, int len, bool vertical, bool red)[][] LevelsData =
    {
        new[] { ('R', 0, 2, 2, false, true), ('A', 3, 2, 2, true, false) },
        new[] { ('R', 0, 2, 2, false, true), ('A', 3, 2, 3, true, false), ('B', 4, 4, 2, true, false) },
        new[] { ('R', 0, 2, 2, false, true), ('A', 3, 2, 2, true, false), ('C', 4, 1, 2, true, false) },
        new[] { ('R', 0, 2, 2, false, true), ('A', 3, 2, 2, true, false), ('C', 4, 2, 2, true, false), ('D', 5, 3, 2, true, false) },
        new[] { ('R', 0, 2, 2, false, true), ('B', 2, 1, 2, true, false), ('A', 3, 2, 2, true, false), ('C', 4, 2, 2, true, false) },
        new[]
        {
            ('R', 0, 2, 2, false, true), ('E', 1, 1, 3, false, false),
            ('B', 2, 2, 2, true, false), ('A', 3, 2, 2, true, false),
            ('C', 4, 2, 2, true, false), ('D', 2, 4, 2, true, false),
        },
    };

    private readonly List<GameCell> _cells = new();
    private readonly List<Car> _cars = new();
    private readonly int[,] _carAt = new int[Side, Side];
    private int _sel = -1;
    private int _moves;
    private int _level = 1;
    private int _score;
    private int _solvedCount;
    private bool _done;
    private string _message = "";
    private string _result = "";

    public TrafficGame()
    {
        for (int i = 0; i < Side * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "traffic";
    public override string Name => "Traffic Jam";
    public override int Columns => Side;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _score;
    public override string? ResultText => _done ? _result : null;

    public override string StatusText
    {
        get
        {
            if (_done) return $"Done — {_score} points.";
            if (_message.Length > 0) return _message;
            return $"Level {_level}/{Levels} · {_moves} moves · slide R to →";
        }
    }

    public override void Reset()
    {
        _level = 1;
        _score = 0;
        _solvedCount = 0;
        _done = false;
        _result = "";
        ScoreCounts = false;
        LoadLevel();
    }

    private void LoadLevel()
    {
        _cars.Clear();
        foreach (var (letter, x, y, len, vertical, red) in LevelsData[_level - 1])
            _cars.Add(new Car { Letter = letter, X = x, Y = y, Len = len, Vertical = vertical, Red = red });
        _sel = -1;
        _moves = 0;
        _message = "";
        RebuildOccupancy();
        Redraw();
    }

    private void RebuildOccupancy()
    {
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
                _carAt[x, y] = -1;
        for (int i = 0; i < _cars.Count; i++)
        {
            var car = _cars[i];
            for (int k = 0; k < car.Len; k++)
                _carAt[car.Vertical ? car.X : car.X + k, car.Vertical ? car.Y + k : car.Y] = i;
        }
    }

    public override bool OnTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;
        int x = index % Side, y = index / Side;

        if (y == Side)
        {
            if (index == EndCell && _solvedCount >= 1)
            {
                _done = true;
                ScoreCounts = true;
                _result = $"Ended after {_solvedCount} level(s) — {_score} points.";
                Redraw();
                return true;
            }
            return false;
        }

        int carIdx = _carAt[x, y];
        if (carIdx >= 0)
        {
            _sel = _sel == carIdx ? -1 : carIdx;
            _message = "";
            Redraw();
            return true;
        }
        if (_sel < 0) return false;

        // Slide the selected car so one of its ends lands on the tapped square.
        var car = _cars[_sel];
        int delta = 0;
        if (car.Vertical && x == car.X)
        {
            if (y > car.Y + car.Len - 1) delta = y - (car.Y + car.Len - 1);
            else if (y < car.Y) delta = y - car.Y;
        }
        else if (!car.Vertical && y == car.Y)
        {
            if (x > car.X + car.Len - 1) delta = x - (car.X + car.Len - 1);
            else if (x < car.X) delta = x - car.X;
        }
        if (delta == 0) return true;   // wrong lane or the car's own span

        TrySlide(car, delta);
        Redraw();
        return true;
    }

    /// <summary>Arrows nudge the selected car one square along its lane.</summary>
    public override bool OnKey(GameKey key)
    {
        if (_done || _sel < 0) return false;
        var car = _cars[_sel];
        bool handled = key switch
        {
            GameKey.Up when car.Vertical => TrySlide(car, -1),
            GameKey.Down when car.Vertical => TrySlide(car, 1),
            GameKey.Left when !car.Vertical => TrySlide(car, -1),
            GameKey.Right when !car.Vertical => TrySlide(car, 1),
            _ => false,
        };
        if (handled) Redraw();
        return handled;
    }

    private bool TrySlide(Car car, int delta)
    {
        if (delta == 0) return false;
        int step = Math.Sign(delta);
        for (int i = 1; i <= Math.Abs(delta); i++)
        {
            int lx = car.Vertical ? car.X : car.X + (step > 0 ? car.Len - 1 + i : -i);
            int ly = car.Vertical ? car.Y + (step > 0 ? car.Len - 1 + i : -i) : car.Y;
            if (lx < 0 || ly < 0 || lx >= Side || ly >= Side) return false;
            if (_carAt[lx, ly] >= 0) return false;
        }
        if (car.Vertical) car.Y += delta;
        else car.X += delta;
        _moves++;
        AfterSlide();
        return true;
    }

    private void AfterSlide()
    {
        RebuildOccupancy();
        var red = _cars.First(c => c.Red);
        if (red.X + red.Len - 1 != Side - 1) return;

        _score += Math.Max(40, 150 - 10 * _moves);
        _solvedCount++;
        if (_level == Levels)
        {
            _done = true;
            ScoreCounts = true;
            _result = $"Traffic cleared — {_score} points.";
            return;
        }
        _level++;
        LoadLevel();
        _message = "Level cleared!";
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
            c.FontSize = 18;
            c.Bold = false;
        }

        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
            {
                var c = _cells[y * Side + x];
                c.Background = Cell;
                if (_carAt[x, y] < 0 && y == 2 && x == Side - 1)
                {
                    c.Text = "→";
                    c.Foreground = Muted;
                    c.Bold = true;
                }
                c.IsEnabled = !_done;
            }

        for (int i = 0; i < _cars.Count; i++)
        {
            var car = _cars[i];
            for (int k = 0; k < car.Len; k++)
            {
                var c = _cells[(car.Vertical ? car.Y + k : car.Y) * Side + (car.Vertical ? car.X : car.X + k)];
                c.Text = car.Letter.ToString();
                c.Background = car.Red ? Bad : Tiles4[(i * 2 + 1) % Tiles4.Length];
                c.Foreground = Cell;
                c.Bold = true;
                c.FontSize = _sel == i ? 24 : 18;
            }
        }

        var end = _cells[EndCell];
        end.Text = "End";
        end.Background = Cell;
        end.Foreground = _solvedCount >= 1 ? Ink : Muted;
        end.IsEnabled = !_done && _solvedCount >= 1;
        end.Bold = true;
        end.FontSize = 14;
    }
}
