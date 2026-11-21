using System;
using System.Collections.Generic;

namespace KindleHub.Client.Games;

/// <summary>
/// Sokoban — push every box onto a ring target. Boxes can only be pushed, never
/// pulled. Eight small hand-verified levels; arrows or a tap on an adjacent
/// square move the worker, and every level is short enough to restart freely.
/// </summary>
public sealed class SokobanGame : GameBase
{
    private const int Cols = 9;
    private const int Rows = 9;      // 8 maze rows at most + 1 button row
    private const int LevelCount = 8;
    private const int RestartCell = 8 * Cols + 3;
    private const int EndCell = 8 * Cols + 5;

    private static readonly Avalonia.Media.IBrush TargetTint =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#f3e6c8"));

    // # wall, . floor, @ worker, $ box, * target, + worker-on-target, B box-on-target.
    private static readonly string[][] Levels =
    {
        new[]
        {
            "######",
            "#....#",
            "#@$*.#",
            "#....#",
            "######",
        },
        new[]
        {
            "#######",
            "#.....#",
            "#..#..#",
            "#.*...#",
            "#..$..#",
            "#..@..#",
            "#######",
        },
        new[]
        {
            "########",
            "#......#",
            "#..**..#",
            "#..$$..#",
            "#..@...#",
            "#......#",
            "########",
        },
        new[]
        {
            "#######",
            "#.....#",
            "#@$...#",
            "#.....#",
            "#.....#",
            "#....*#",
            "#######",
        },
        new[]
        {
            "########",
            "#......#",
            "#@$...*#",
            "#......#",
            "#..$...#",
            "#..*...#",
            "########",
        },
        new[]
        {
            "########",
            "#......#",
            "#.##...#",
            "#.*$@..#",
            "#.##...#",
            "#.$..*.#",
            "########",
        },
        new[]
        {
            "#########",
            "#.......#",
            "#.**....#",
            "#.$$....#",
            "#..@....#",
            "#....$..#",
            "#....*..#",
            "#########",
        },
        new[]
        {
            "#########",
            "#@......#",
            "#.$#..$.#",
            "#..*#...#",
            "#..#....#",
            "#.....*.#",
            "#...*.$.#",
            "#########",
        },
    };

    private readonly List<GameCell> _cells = new();
    private readonly bool[,] _wall = new bool[Cols, Rows];
    private readonly bool[,] _void = new bool[Cols, Rows];
    private readonly bool[,] _target = new bool[Cols, Rows];
    private readonly bool[,] _box = new bool[Cols, Rows];
    private int _wx, _wy;
    private int _moves;
    private int _level;             // 0-based level index
    private int _score;
    private int _solvedLevels;
    private bool _done;
    private string _message = "";
    private string _result = "";

    public SokobanGame()
    {
        for (int i = 0; i < Cols * Rows; i++) _cells.Add(Blank());
        Reset();
    }

    public override string Slug => "sokoban";
    public override string Name => "Sokoban";
    public override int Columns => Cols;
    public override IReadOnlyList<GameCell> Cells => _cells;
    public override int Score => _score;
    public override string? ResultText => _done ? _result : null;

    public override string StatusText
    {
        get
        {
            if (_done) return $"Done — {_score} points.";
            if (_message.Length > 0) return _message;
            return $"Level {_level + 1}/{LevelCount} · {_moves} moves · push ■ onto ◆";
        }
    }

    public override void Reset()
    {
        _level = 0;
        _score = 0;
        _solvedLevels = 0;
        _done = false;
        _result = "";
        ScoreCounts = false;
        LoadLevel();
        Redraw();
    }

    private void LoadLevel()
    {
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                _wall[x, y] = false;
                _void[x, y] = true;   // everything outside the level strings is void
                _target[x, y] = false;
                _box[x, y] = false;
            }

        string[] map = Levels[_level];
        for (int y = 0; y < map.Length && y < Rows - 1; y++)
        {
            for (int x = 0; x < map[y].Length && x < Cols; x++)
            {
                char ch = map[y][x];
                _void[x, y] = ch == ' ';
                _wall[x, y] = ch == '#';
                if (ch == '*' || ch == '+' || ch == 'B') _target[x, y] = true;
                if (ch == '$' || ch == 'B') _box[x, y] = true;
                if (ch == '@' || ch == '+') { _wx = x; _wy = y; }
            }
        }
        _moves = 0;
        _message = "";
    }

    public override bool OnKey(GameKey key)
    {
        if (_done) return false;
        var (dx, dy) = key switch
        {
            GameKey.Up => (0, -1),
            GameKey.Down => (0, 1),
            GameKey.Left => (-1, 0),
            GameKey.Right => (1, 0),
            _ => (0, 0),
        };
        if (dx == 0 && dy == 0) return false;
        Move(dx, dy);
        Redraw();
        return true;
    }

    public override bool OnTap(int index)
    {
        if (_done || index < 0 || index >= _cells.Count) return false;
        int x = index % Cols, y = index / Cols;

        if (y == Rows - 1)
        {
            if (index == RestartCell)
            {
                LoadLevel();
                _message = "Level restarted.";
                Redraw();
                return true;
            }
            if (index == EndCell)
            {
                if (_solvedLevels >= 1)
                {
                    _done = true;
                    ScoreCounts = true;
                    _result = $"Ended at level {_level + 1} — {_score} points.";
                }
                else _message = "Clear at least one level first.";
                Redraw();
                return true;
            }
            return false;
        }

        // Only a tap on a square next to the worker steps there.
        int dx = x - _wx, dy = y - _wy;
        if (Math.Abs(dx) + Math.Abs(dy) != 1) return false;
        Move(dx, dy);
        Redraw();
        return true;
    }

    /// <summary>Steps the worker; pushes a box when one is in the way.</summary>
    private void Move(int dx, int dy)
    {
        int nx = _wx + dx, ny = _wy + dy;
        if (nx < 0 || ny < 0 || nx >= Cols || ny >= Rows) return;
        if (_wall[nx, ny] || _void[nx, ny]) return;

        if (_box[nx, ny])
        {
            int bx = nx + dx, by = ny + dy;
            if (bx < 0 || by < 0 || bx >= Cols || by >= Rows) return;
            if (_wall[bx, by] || _void[bx, by] || _box[bx, by]) return;
            _box[nx, ny] = false;
            _box[bx, by] = true;
        }
        _wx = nx;
        _wy = ny;
        _moves++;
        _message = "";

        if (AllBoxesPlaced()) LevelComplete();
    }

    private bool AllBoxesPlaced()
    {
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                if (_box[x, y] && !_target[x, y]) return false;
        return true;
    }

    private void LevelComplete()
    {
        _score += Math.Max(50, 200 - 5 * _moves);
        _solvedLevels++;
        if (_level == LevelCount - 1)
        {
            _done = true;
            ScoreCounts = true;
            _result = $"All {LevelCount} levels cleared — {_score} points.";
            return;
        }
        _level++;
        LoadLevel();
        _message = $"Level cleared! On to {_level + 1}.";
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
        }

        for (int y = 0; y < Rows - 1; y++)
            for (int x = 0; x < Cols; x++)
            {
                var c = _cells[y * Cols + x];
                if (_void[x, y]) continue;
                if (_wall[x, y])
                {
                    c.Background = Ink;
                    continue;
                }
                if (_box[x, y])
                {
                    c.Text = "■";
                    c.Bold = true;
                    if (_target[x, y])
                    {
                        c.Background = Good;
                        c.Foreground = Cell;
                    }
                    else
                    {
                        c.Background = Cell;
                        c.Foreground = Ink;
                    }
                }
                else if (_target[x, y])
                {
                    c.Text = "◆";
                    c.Background = TargetTint;
                    c.Foreground = Muted;
                }
                else
                {
                    c.Background = Cell;
                }

                if (x == _wx && y == _wy)
                {
                    c.Text = "●";
                    c.Bold = true;
                    c.Foreground = Ink;
                    if (!_target[x, y] && !_box[x, y]) c.Background = Cell;
                }
            }

        DrawButton(RestartCell, "Restart", !_done);
        DrawButton(EndCell, "End", !_done && _solvedLevels >= 1);
    }

    private void DrawButton(int index, string text, bool enabled)
    {
        var c = _cells[index];
        c.Text = text;
        c.Background = Cell;
        c.Foreground = enabled ? Ink : Muted;
        c.IsEnabled = enabled;
        c.Bold = true;
        c.FontSize = 14;
    }
}
