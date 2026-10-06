using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
        SizeChanged += (_, args) => ConfigureForWidth(args.NewSize.Width);
        Loaded += (_, _) => ConfigureForWidth(Bounds.Width);
    }

    private void ConfigureForWidth(double width)
    {
        var compact = width < 760;
        ArcadeRoot.Margin = compact ? new Thickness(8) : new Thickness(0);
        ArcadeHero.ColumnDefinitions.Clear();
        ArcadeHero.RowDefinitions.Clear();
        ArcadeHero.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        ArcadeHero.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        ArcadeHero.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        ArcadeHero.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (compact)
        {
            ArcadeHero.ColumnDefinitions[0].Width = GridLength.Star;
            ArcadeHero.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetRow(ArcadeHero.Children[1], 1);
            Grid.SetColumn(ArcadeHero.Children[1], 0);
            Grid.SetRow(ArcadeHero.Children[2], 2);
            Grid.SetColumn(ArcadeHero.Children[2], 0);
            ArcadeHero.Children[1].HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            ArcadeHero.Children[1].Margin = new Thickness(0, 14, 0, 0);
        }
        else
        {
            ArcadeHero.ColumnDefinitions.Clear();
            ArcadeHero.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            ArcadeHero.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetRow(ArcadeHero.Children[1], 0);
            Grid.SetColumn(ArcadeHero.Children[1], 1);
            Grid.SetRow(ArcadeHero.Children[2], 1);
            Grid.SetColumn(ArcadeHero.Children[2], 0);
            ArcadeHero.Children[1].Margin = new Thickness(0);
        }

        ArcadeSearch.ColumnDefinitions.Clear();
        ArcadeSearch.RowDefinitions.Clear();
        ArcadeSearch.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        ArcadeSearch.ColumnDefinitions.Add(new ColumnDefinition(compact ? GridLength.Star : GridLength.Auto));
        ArcadeSearch.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        ArcadeSearch.RowDefinitions.Add(new RowDefinition(compact ? GridLength.Auto : new GridLength(0)));
        var searchBox = (Control)ArcadeSearch.Children[0];
        var filter = (Control)ArcadeSearch.Children[1];
        if (compact)
        {
            Grid.SetColumn(filter, 0);
            Grid.SetRow(filter, 1);
            filter.Margin = new Thickness(0, 8, 0, 0);
            filter.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            searchBox.Margin = new Thickness(0);
        }
        else
        {
            Grid.SetColumn(filter, 1);
            Grid.SetRow(filter, 0);
            filter.Margin = new Thickness(12, 0, 0, 0);
            filter.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        }

        foreach (var card in ArcadeRoot.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("game-card")))
            card.Width = compact ? Math.Max(250, width - 48) : 280;

        OnlineGameHeader.ColumnDefinitions.Clear();
        OnlineGameHeader.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        OnlineGameHeader.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        OnlineGameHeader.Width = compact ? double.NaN : 760;
        OnlineGameHeader.HorizontalAlignment = compact ? Avalonia.Layout.HorizontalAlignment.Stretch : Avalonia.Layout.HorizontalAlignment.Center;
        OnlineGameHeader.ColumnSpacing = compact ? 8 : 16;
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
