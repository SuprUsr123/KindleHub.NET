using Avalonia.Controls;

namespace KindleHub.Client.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        SizeChanged += (_, args) => AdaptToWidth(args.NewSize.Width);
    }

    private void AdaptToWidth(double width)
    {
        var narrow = width < 640;
        ContentStack.Margin = narrow ? new Avalonia.Thickness(16) : new Avalonia.Thickness(48, 40);
        HomeCards.ColumnDefinitions.Clear();
        HomeCards.RowDefinitions.Clear();
        if (narrow)
        {
            HomeCards.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (var index = 0; index < HomeCards.Children.Count; index++)
            {
                HomeCards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                Grid.SetColumn(HomeCards.Children[index], 0);
                Grid.SetRow(HomeCards.Children[index], index);
            }
        }
        else
        {
            HomeCards.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            HomeCards.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            HomeCards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            HomeCards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var index = 0; index < HomeCards.Children.Count; index++)
            {
                Grid.SetColumn(HomeCards.Children[index], index % 2);
                Grid.SetRow(HomeCards.Children[index], index / 2);
            }
        }
    }
}
