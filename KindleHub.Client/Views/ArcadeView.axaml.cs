using System;
using Avalonia.Controls;
using KindleHub.Client.ViewModels;

namespace KindleHub.Client.Views;

/// <summary>
/// The Arcade page is a catalogue; playing a game opens a <see cref="GameWindow"/>.
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
        _vm = DataContext as ArcadeViewModel;
        if (_vm != null) _vm.GameReady += OnGameReady;
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
}
