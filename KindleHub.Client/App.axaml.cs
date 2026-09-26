using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using KindleHub.Core;
using KindleHub.Client.ViewModels;
using KindleHub.Client.Views;

namespace KindleHub.Client;

public partial class App : Application
{
    public static IHost? Host { get; private set; }
    public static IServiceProvider? Services => Host?.Services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Host = CreateHostBuilder().Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = Services!.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
            
            _ = mainViewModel.InitializeAsync();
            mainViewModel.NavigateTo("Home");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IHostBuilder CreateHostBuilder()
    {
        return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Core services
                services.AddSingleton(sp => 
                {
                    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                    var secretPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "KindleHubPro",
                        "msg_secrets.json");
                    return KindleHubCoreFactory.CreateCore(loggerFactory, secretPath);
                });
                
                // Navigation is shared — MainViewModel is the single source of truth.
                services.AddSingleton<MainViewModel>(sp => 
                    new MainViewModel(
                        sp.GetRequiredService<KindleHubCore>(),
                        sp.GetRequiredService<ILogger<MainViewModel>>(),
                        sp));
                services.AddTransient<HomeViewModel>(sp => 
                    new HomeViewModel(
                        sp.GetRequiredService<KindleHubCore>(),
                        sp.GetRequiredService<ILogger<HomeViewModel>>()));
                services.AddTransient<LeaderboardViewModel>(sp => 
                    new LeaderboardViewModel(
                        sp.GetRequiredService<KindleHubCore>(),
                        sp.GetRequiredService<ILogger<LeaderboardViewModel>>()));
                services.AddTransient<CommunityViewModel>(sp => 
                    new CommunityViewModel(
                        sp.GetRequiredService<KindleHubCore>(),
                        sp.GetRequiredService<ILogger<CommunityViewModel>>(),
                        sp.GetRequiredService<MainViewModel>()));
                services.AddTransient<MessagesViewModel>(sp => 
                    new MessagesViewModel(
                        sp.GetRequiredService<KindleHubCore>(),
                        sp.GetRequiredService<ILogger<MessagesViewModel>>()));
                services.AddTransient<GamesViewModel>(sp => 
                    new GamesViewModel(
                        sp.GetRequiredService<KindleHubCore>(),
                        sp.GetRequiredService<ILogger<GamesViewModel>>()));
                services.AddTransient<AppStoreViewModel>(sp => 
                    new AppStoreViewModel(
                        sp.GetRequiredService<KindleHubCore>(),
                        sp.GetRequiredService<ILogger<AppStoreViewModel>>()));
                services.AddTransient<SettingsViewModel>(sp => 
                    new SettingsViewModel(
                        sp.GetRequiredService<KindleHubCore>(),
                        sp.GetRequiredService<ILogger<SettingsViewModel>>(),
                        sp.GetRequiredService<MainViewModel>()));
                
                // Logging
                services.AddLogging(builder => builder.AddDebug());
            });
    }
}