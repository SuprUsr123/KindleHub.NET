using Avalonia.Controls;
using Avalonia.Interactivity;
using KindleHub.Client.ViewModels;
using System.ComponentModel;

namespace KindleHub.Client;

public partial class MobileMainView : UserControl
{
    public MobileMainView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateActiveNavigation(viewModel);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentView) && sender is MainViewModel viewModel)
            UpdateActiveNavigation(viewModel);
    }

    private void UpdateActiveNavigation(MainViewModel viewModel)
    {
        var current = viewModel.CurrentView;
        SetActive(HomeNavButton, current is HomeViewModel);
        SetActive(FeedNavButton, current is CommunityViewModel);
        SetActive(ChatsNavButton, current is MessagesViewModel);
        SetActive(ArcadeNavButton, current is ArcadeViewModel or GamesViewModel);
        SetActive(MoreNavButton, current is not (HomeViewModel or CommunityViewModel or MessagesViewModel or ArcadeViewModel or GamesViewModel));
    }

    private static void SetActive(Button button, bool active)
    {
        if (active)
        {
            if (!button.Classes.Contains("active"))
                button.Classes.Add("active");
        }
        else
        {
            button.Classes.Remove("active");
        }
    }
}
