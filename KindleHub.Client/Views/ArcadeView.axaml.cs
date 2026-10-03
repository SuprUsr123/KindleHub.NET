using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KindleHub.Client.Games;
using KindleHub.Client.ViewModels;

namespace KindleHub.Client.Views;

/// <summary>
/// The Arcade page hosts relay games inline and opens a <see cref="GameWindow"/> for solo games.
/// One window is kept and re-raised rather than a second one being opened, so
/// clicking Play twice doesn't stack windows for the same view model.
/// </summary>
public partial class ArcadeView : UserControl
{
    private ArcadeViewModel? _vm;
    private GameWindow? _open;

    public ArcadeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null) _vm.GameReady -= OnGameReady;
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as ArcadeViewModel;
        if (_vm != null)
        {
            _vm.GameReady += OnGameReady;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArcadeViewModel.IsOnlineGamePage) && _vm is { IsOnlineGamePage: true } && _open is { IsVisible: true })
            _open.Close();
    }

    private void OnGameReady()
    {
        if (_vm is null) return;
        if (_open is { } existing && existing.IsVisible)
        {
            existing.Activate();
            return;
        }
        _open = new GameWindow { DataContext = _vm };
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }

    private void OnOnlineBoardTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null || e.Source is not Control control || control.DataContext is not GameCell cell) return;
        var cells = _vm.Cells;
        for (int i = 0; i < cells.Count; i++)
        {
            if (!ReferenceEquals(cells[i], cell)) continue;
            _vm.TapCommand.Execute(i);
            e.Handled = true;
            return;
        }
    }
}
