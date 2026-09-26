using Avalonia.Controls;
using Avalonia.Input;
using KindleHub.Client.ViewModels;

namespace KindleHub.Client.Views;

public partial class CommunityView : UserControl
{
    public CommunityView()
    {
        InitializeComponent();
    }

    private async void Topic_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is CommunityViewModel vm && vm.SelectedTopic != null)
        {
            await vm.OpenSelectedAsync();
        }
    }
}
