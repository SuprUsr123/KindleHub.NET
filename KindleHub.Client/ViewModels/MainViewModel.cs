using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Input;
using KindleHub.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KindleHub.Client.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly KindleHubCore _core;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    private ViewModelBase? _currentView;
    private ArcadeViewModel? _arcadeViewModel;
    private UserProfile? _currentUser;
    private string _statusText = "Not connected";

    public ViewModelBase? CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public UserProfile? CurrentUser
    {
        get => _currentUser;
        set
        {
            if (SetProperty(ref _currentUser, value))
            {
                OnPropertyChanged(nameof(UserDisplayName));
                OnPropertyChanged(nameof(IsAuthenticated));

                if (value != null && !string.IsNullOrEmpty(value.AuthToken))
                {
                    try
                    {
                        var theme = _core.CurrentPrefs().Theme;
                        AppTheme.Apply(theme);
                    }
                    catch
                    {
                        AppTheme.Apply(AppTheme.Light);
                    }
                }
            }
        }
    }

    public string UserDisplayName => _currentUser?.DisplayName ?? "Guest";
    public bool IsAuthenticated => _currentUser != null && !string.IsNullOrEmpty(_currentUser.AuthToken);

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ICommand NavigateCommand { get; }

    public MainViewModel(KindleHubCore core, ILogger<MainViewModel> logger, IServiceProvider serviceProvider)
    {
        _core = core;
        _logger = logger;
        _serviceProvider = serviceProvider;

        NavigateCommand = new RelayCommand<string>(NavigateTo);

        _core.ProfileChanged += (s, e) => 
        {
            CurrentUser = e;
            FontSizeScaler.SetFontSizePx(_core.CurrentPrefs().FontSizePx);
            OnPropertyChanged(nameof(IsAuthenticated));
            OnPropertyChanged(nameof(UserDisplayName));
        };

    }

    public async Task InitializeAsync()
    {
        StatusText = "Connecting...";
        try
        {
            var status = await _core.CheckServerStatusAsync(CancellationToken.None);
            StatusText = status.Online ? "Connected" : "Server Offline";
            if (status.Online)
            {
                // Restore a previously saved session so the user doesn't have to
                // re-enter credentials on every launch.
                var restored = await _core.RestoreSessionAsync(CancellationToken.None);
                if (restored)
                {
                    StatusText = "Restored your session";
                    NavigateTo("Home");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to server");
            StatusText = "Connection Failed";
        }
    }

    public async Task<bool> TryRestoreSessionAsync()
    {
        try
        {
            var restored = await _core.RestoreSessionAsync(CancellationToken.None);
            if (restored)
            {
                StatusText = "Restored your session";
                NavigateTo("Home");
                return true;
            }
            StatusText = "No saved session — sign in to continue.";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session restore failed");
            StatusText = "Couldn't restore session: " + ex.Message;
            return false;
        }
    }

    public void NavigateTo(string? page)
    {
        if (page == null) page = "Home";
        ViewModelBase target;
        switch (page)
        {
            case "Home":
                target = _serviceProvider.GetRequiredService<HomeViewModel>();
                break;
            case "Leaderboards":
                target = _serviceProvider.GetRequiredService<LeaderboardViewModel>();
                break;
            case "Community":
                target = _serviceProvider.GetRequiredService<CommunityViewModel>();
                break;
            case "Messages":
                target = _serviceProvider.GetRequiredService<MessagesViewModel>();
                break;
            case "Mail":
                target = _serviceProvider.GetRequiredService<MailViewModel>();
                break;
            case "Games":
                target = _serviceProvider.GetRequiredService<GamesViewModel>();
                break;
            case "Arcade":
                target = _arcadeViewModel ??= _serviceProvider.GetRequiredService<ArcadeViewModel>();
                break;
            case "App Store":
                target = _serviceProvider.GetRequiredService<AppStoreViewModel>();
                break;
            case "Settings":
                target = _serviceProvider.GetRequiredService<SettingsViewModel>();
                break;
            default:
                target = _serviceProvider.GetRequiredService<HomeViewModel>();
                break;
        }

        var previous = CurrentView;
        if (previous is IDisposable disposable && !ReferenceEquals(previous, target))
        {
            try { disposable.Dispose(); }
            catch (Exception ex) { _logger.LogDebug(ex, "View cleanup failed for {View}", previous.GetType().Name); }
        }

        Console.WriteLine($"[KindleHub Debug] NavigateTo('{page}') -> {target.GetType().Name}");
        CurrentView = target;
        Console.WriteLine($"[KindleHub Debug] CurrentView is now: {CurrentView?.GetType().Name}");
    }

    public void ShowOnlineGamePage(ArcadeViewModel viewModel)
    {
        _arcadeViewModel = viewModel;
        if (!ReferenceEquals(CurrentView, viewModel))
        {
            var previous = CurrentView;
            if (previous is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch (Exception ex) { _logger.LogDebug(ex, "View cleanup failed for {View}", previous.GetType().Name); }
            }
            CurrentView = viewModel;
        }
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        StatusText = "Logging in...";
        try
        {
            var result = await _core.LoginAsync(username, password, CancellationToken.None);
            if (result.Success)
            {
                StatusText = $"Logged in as {result.Username}";
                NavigateTo("Home");
                return true;
            }
            else
            {
                StatusText = $"Login failed: {result.Error}";
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login error");
            StatusText = $"Login error: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> RegisterAsync(string username, string password)
    {
        StatusText = "Registering...";
        try
        {
            var result = await _core.RegisterAsync(username, password, CancellationToken.None);
            if (result.Success)
            {
                StatusText = $"Registered as {result.Username}";
                NavigateTo("Home");
                return true;
            }
            else
            {
                StatusText = $"Registration failed: {result.Error}";
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration error");
            StatusText = $"Registration error: {ex.Message}";
            return false;
        }
    }

    public void Logout()
    {
        _core.Logout();
        CurrentUser = null;
        NavigateTo("Home");
        StatusText = "Logged out";
    }
}
