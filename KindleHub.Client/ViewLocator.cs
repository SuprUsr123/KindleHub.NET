using Avalonia.Controls;
using Avalonia.Controls.Templates;
using KindleHub.Client.ViewModels;
using KindleHub.Client.Views;

namespace KindleHub.Client;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        return param switch
        {
            HomeViewModel => new HomeView(),
            LeaderboardViewModel => new LeaderboardView(),
            CommunityViewModel => new CommunityView(),
            MessagesViewModel => new MessagesView(),
            GamesViewModel => new GamesView(),
            AppStoreViewModel => new AppStoreView(),
            SettingsViewModel => new SettingsView(),
            _ => new TextBlock { Text = "Unknown ViewModel: " + param?.GetType().Name }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
