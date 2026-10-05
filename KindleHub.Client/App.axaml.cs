using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using KindleHub.Core;
using KindleHub.Client.ViewModels;
using KindleHub.Client.Views;

namespace KindleHub.Client;

public partial class App : Application
{
    public static string[] LaunchArguments { get; set; } = Array.Empty<string>();
    public static bool DebugMode { get; set; }
    public static bool RunWorldMachineSequence { get; set; }
    public static string? StartupJoinTarget { get; set; }
    public static IHost? Host { get; private set; }
    public static IServiceProvider? Services => Host?.Services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime argumentDesktop && RunWorldMachineSequence)
        {
            argumentDesktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnFrameworkInitializationCompleted();
            _ = RunArgumentSequenceAsync(argumentDesktop);
            return;
        }

        Host = CreateHostBuilder().Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Apply the saved theme before the window exists, so the app never
            // flashes the system theme on the way in. Safe when signed out — the
            // default applies until a profile is restored.
            try
            {
                var prefs = Services!.GetRequiredService<KindleHubCore>().CurrentPrefs();
                AppTheme.Apply(prefs.Theme);
                FontSizeScaler.SetFontSizePx(prefs.FontSizePx);
            }
            catch { AppTheme.Apply(AppTheme.Light); }

            var mainViewModel = Services!.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
            desktop.MainWindow.Opened += async (_, _) =>
            {
                await mainViewModel.InitializeAsync();
                if (!string.IsNullOrWhiteSpace(StartupJoinTarget))
                    await RunStartupJoinAsync(mainViewModel, StartupJoinTarget);
                if (!SettingsViewModel.HasFound("Grass, Mr. Freeman?") && TryFindLongSessions(out _))
                {
                    await ShowMessageAsync(string.Concat("JESUS CHRIST WHO THE FUCK DEVOTES ", "THEMSELVES TO GMOD LIKE THAT"));
                    SettingsViewModel.NoteFound("Grass, Mr. Freeman?");
                }
            };
            
            mainViewModel.NavigateTo("Home");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunStartupJoinAsync(MainViewModel mainViewModel, string target)
    {
        var parts = target.Split(':', 2, StringSplitOptions.TrimEntries);
        var code = new string(parts[0].Where(char.IsDigit).ToArray());
        if (code.Length == 12)
        {
            mainViewModel.NavigateTo("Messages");
            if (mainViewModel.CurrentView is MessagesViewModel messages)
            {
                messages.JoinCode = code;
                await messages.JoinByCodeAsync();
            }
            return;
        }

        mainViewModel.NavigateTo("Arcade");
        if (mainViewModel.CurrentView is ArcadeViewModel arcade)
            await arcade.JoinFromCommandAsync(parts[0], parts.Length > 1 ? parts[1] : "");
    }

    private static async Task RunArgumentSequenceAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        foreach (var line in new[] { "...", "......", string.Concat("Why are you", " still here?"), "...", string.Concat("So you really want to know ", "who I am, huh?"), string.Concat("Okay ", "then...") })
            await ShowMessageAsync(line);
        SettingsViewModel.NoteFound("TheWorldMachine");
        try { Process.Start(new ProcessStartInfo(string.Concat("https://store.steampowered.com/app/", "2915460", "/OneShot_World_Machine_Edition/")) { UseShellExecute = true }); } catch { }
        try { Process.Start(new ProcessStartInfo(string.Concat("steam://", "2915460")) { UseShellExecute = true }); } catch { }
        desktop.Shutdown();
    }

    private static Task ShowMessageAsync(string message)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            var panel = new StackPanel { Margin = new Thickness(20), Spacing = 18, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            var ok = new Button { Content = "OK", MinWidth = 90, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            panel.Children.Add(ok);
            var box = new Window { Title = "KindleHub Pro", Width = 420, Height = 170, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel };
            ok.Click += (_, _) => box.Close();
            box.Closed += (_, _) => done.TrySetResult();
            box.Show();
        });
        return done.Task;
    }

    private static bool TryFindLongSessions(out double hours)
    {
        hours = 0;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam") }
            : OperatingSystem.IsMacOS()
                ? new[] { Path.Combine(home, "Library/Application Support/Steam") }
                : new[] { Path.Combine(home, ".steam/steam"), Path.Combine(home, ".local/share/Steam"), Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam") };

        foreach (var root in roots)
        {
            var users = Path.Combine(root, "userdata");
            if (!Directory.Exists(users)) continue;
            foreach (var user in Directory.GetDirectories(users))
            {
                var local = Path.Combine(user, "config", "localconfig.vdf");
                if (!File.Exists(local)) continue;
                try
                {
                    var text = File.ReadAllText(local);
                    var appKey = string.Concat("40", "00");
                    var entry = Regex.Match(text, "\\\"" + Regex.Escape(appKey) + "\\\"\\s*\\{[^}]*\\\"Playtime\\\"\\s*\\\"(?<minutes>\\d+)\\\"", RegexOptions.Singleline);
                    if (entry.Success && double.TryParse(entry.Groups["minutes"].Value, out var minutes)) hours = Math.Max(hours, minutes / 60d);
                }
                catch { }
            }
        }
        return hours >= 1000;
    }

    private static IHostBuilder CreateHostBuilder()
    {
        return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                if (DebugMode) logging.SetMinimumLevel(LogLevel.Debug);
            })
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
                        sp.GetRequiredService<ILogger<MessagesViewModel>>(),
                        sp.GetRequiredService<MainViewModel>()));
                services.AddTransient<GamesViewModel>(sp => 
                    new GamesViewModel(
                        sp.GetRequiredService<KindleHubCore>(),
                        sp.GetRequiredService<ILogger<GamesViewModel>>()));
                services.AddTransient<MailViewModel>(sp =>
                    new MailViewModel(
                        sp.GetRequiredService<KindleHubCore>(),
                        sp.GetRequiredService<ILogger<MailViewModel>>()));
                services.AddTransient<ArcadeViewModel>(sp =>
                    new ArcadeViewModel(
                        sp.GetRequiredService<KindleHubCore>(),
                        sp.GetRequiredService<ILogger<ArcadeViewModel>>(),
                        // The Tic-Tac-Toe card hands off to the relay screen.
                        target => sp.GetRequiredService<MainViewModel>().NavigateTo(target),
                        showOnlinePage: vm => sp.GetRequiredService<MainViewModel>().ShowOnlineGamePage(vm)));
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
