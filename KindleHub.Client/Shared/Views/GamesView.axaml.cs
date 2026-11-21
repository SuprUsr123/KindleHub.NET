using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using KindleHub.Client.ViewModels;

namespace KindleHub.Client.Views;

public partial class GamesView : UserControl
{
    public GamesView()
    {
        InitializeComponent();
        SizeChanged += (_, args) => ConfigureForWidth(args.NewSize.Width);
        Loaded += (_, _) => ConfigureForWidth(Bounds.Width);
    }

    private void ConfigureForWidth(double width)
    {
        var compact = width < 760;
        GameTitlePanel.Margin = compact ? new Thickness(12, 12, 12, 0) : new Thickness(20, 18, 20, 0);
        GameActions.Margin = compact ? new Thickness(12, 10, 12, 6) : new Thickness(20, 10, 20, 10);
        MatchLayout.Margin = compact ? new Thickness(12, 0, 12, 12) : new Thickness(20, 0, 20, 20);
        MatchLayout.ColumnDefinitions.Clear();
        MatchLayout.RowDefinitions.Clear();

        if (compact)
        {
            MatchLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            MatchLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            MatchLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(MatchPanel, 0);
            Grid.SetRow(MatchPanel, 0);
            Grid.SetColumn(RoomsPanel, 0);
            Grid.SetRow(RoomsPanel, 1);
            RoomsPanel.Margin = new Thickness(0, 12, 0, 0);
            RoomsPanel.MaxHeight = 190;
            MatchPanel.HorizontalAlignment = HorizontalAlignment.Center;
            MatchPanel.VerticalAlignment = VerticalAlignment.Top;
            MatchPanel.Width = Math.Max(210, Math.Min(340, width - 40));
            MatchPanel.Spacing = 8;
            Board.Width = Math.Max(210, Math.Min(340, width - 44));
            Board.Height = Board.Width;
            Board.HorizontalAlignment = HorizontalAlignment.Center;
        }
        else
        {
            MatchLayout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(360)));
            MatchLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(RoomsPanel, 0);
            Grid.SetRow(RoomsPanel, 0);
            Grid.SetColumn(MatchPanel, 1);
            Grid.SetRow(MatchPanel, 0);
            RoomsPanel.Margin = new Thickness(0, 0, 16, 0);
            RoomsPanel.ClearValue(Layoutable.MaxHeightProperty);
            MatchPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            MatchPanel.Width = double.NaN;
            MatchPanel.Spacing = 10;
            Board.Width = 340;
            Board.Height = 340;
            Board.HorizontalAlignment = HorizontalAlignment.Left;
        }

        var markSize = compact ? Math.Clamp(Board.Width / 7.2, 32, 46) : 60;
        foreach (var mark in Board.GetVisualDescendants().OfType<TextBlock>().Where(block => block.Classes.Contains("ttt-mark")))
            mark.FontSize = markSize;
    }

    private async void Cell_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TttCell cell } && DataContext is GamesViewModel vm)
        {
            await vm.CellClickedAsync(cell.Index);
        }
    }
}
