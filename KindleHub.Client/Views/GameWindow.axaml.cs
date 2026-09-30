using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using KindleHub.Client.Games;
using KindleHub.Client.ViewModels;

namespace KindleHub.Client.Views;

/// <summary>
/// Hosts one game. Games open here rather than inline on the Arcade page so the
/// catalogue stays a catalogue, and so a real-time game (Snake) keeps ticking
/// while you browse the rest of the app.
///
/// The window binds to the same <see cref="ArcadeViewModel"/> the page uses, so
/// taps and keys go through the same commands — there is no second copy of the
/// rules, and a score still posts from the same place.
/// </summary>
public partial class GameWindow : Window
{
    private ArcadeViewModel? _vm;
    private DispatcherTimer? _clock;
    private long _lastTick;

    public GameWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) => StopClock();
        Opened += (_, _) => StartClock();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as ArcadeViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            Title = $"KindleHub Pro — {_vm.GameName}";
        }
        StartClock();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArcadeViewModel.GameName)) Title = $"KindleHub Pro — {_vm?.GameName}";
        // Leaving a match (or going back) closes the window rather than stranding
        // an empty board.
        if (e.PropertyName == nameof(ArcadeViewModel.InGame) && _vm is { InGame: false }) Close();
    }

    /// <summary>Only real-time games need a clock, so an idle window costs nothing.</summary>
    private void StartClock()
    {
        StopClock();
        if (_vm is not { InGame: true, IsRealTime: true }) return;
        _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _clock.Tick += OnClock;
        _lastTick = Stopwatch.GetTimestamp();
        _clock.Start();
    }

    private void StopClock() { _clock?.Stop(); _clock = null; }

    private void OnClock(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_lastTick, now);
        _lastTick = now;
        _vm?.Tick(elapsed);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is not { InGame: true }) return;
        var key = e.Key switch
        {
            Key.Left or Key.A => GameKey.Left,
            Key.Right or Key.D => GameKey.Right,
            Key.Up or Key.W => GameKey.Up,
            Key.Down or Key.S => GameKey.Down,
            _ => GameKey.None,
        };
        if (key == GameKey.None) return;
        _vm.Press(key);
        e.Handled = true;
    }

    /// <summary>Cell taps arrive from the board, which carries the GameCell.</summary>
    private void OnBoardTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null) return;
        if (e.Source is not Control c || c.DataContext is not GameCell cell) return;

        var cells = _vm.Cells;
        for (int i = 0; i < cells.Count; i++)
        {
            if (!ReferenceEquals(cells[i], cell)) continue;
            _vm.TapCommand.Execute(i);
            e.Handled = true;
            return;
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
