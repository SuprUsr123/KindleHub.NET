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
        Opened += (_, _) =>
        {
            StartClock();
            // Nothing on the board is focusable — the cells are Borders, not
            // buttons — so without this the window never becomes the keyboard
            // target and arrow keys silently do nothing. 2048 has no other input.
            Focus();
        };
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

    /// <summary>
/// Runs the game's Tick. Memory and Simon advance a phase machine on it (flip
/// two mismatched cards back, flash the next pad) even though they are not
/// real-time, so gating this on IsRealTime left Memory softlocked with its
/// cards face-up and Simon never showing a pattern.
/// </summary>
private void StartClock()
    {
        StopClock();
        if (_vm is not { InGame: true }) return;
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

        // Letters first: Hangman and Wordle are typed, not tapped. Matching on
        // e.Key covers A–Z on any layout and doesn't depend on Shift being held,
        // so both cases work without an explicit shift check.
        if (e.Key is >= Key.A and <= Key.Z)
        {
            if (_vm.Type((char)e.Key)) e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Back:
            case Key.Delete:
                if (_vm.Backspace()) e.Handled = true;
                return;
            // Key.Enter and Key.Return are the same value in Avalonia 11, and
            // listing both is a compile error, so this covers the numpad too.
            case Key.Enter:
                if (_vm.SubmitTyped()) e.Handled = true;
                return;
        }

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
