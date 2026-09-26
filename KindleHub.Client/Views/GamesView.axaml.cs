using Avalonia.Controls;
using Avalonia.Interactivity;
using KindleHub.Client.ViewModels;

namespace KindleHub.Client.Views;

public partial class GamesView : UserControl
{
    public GamesView()
    {
        InitializeComponent();
    }

    private async void Cell_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TttCell cell } && DataContext is GamesViewModel vm)
        {
            await vm.CellClickedAsync(cell.Index);
        }
    }
}
