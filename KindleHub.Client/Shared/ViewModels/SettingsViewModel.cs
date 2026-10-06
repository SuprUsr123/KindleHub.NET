using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia.Media;
using KindleHub.Client.Models;
using KindleHub.Core;
using Microsoft.Extensions.Logging;

namespace KindleHub.Client.ViewModels;

/// <summary>
/// Account + preferences screen. The preference rows mirror the official web settings
/// (profile name, font size, theme, simple mode, sync) and persist into the SAME encrypted
/// account state the web client uses, so changing a preference here shows up after a web
/// sign-in and vice-versa.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private static readonly string KeepsakePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KindleHubPro", "keepsakes.json");
    private static readonly HashSet<string> Keepsakes = ReadKeepsakes();
    public static event Action? KeepsakesChanged;

    public static bool HasFound(string item) => Keepsakes.Contains(item);

    public static void NoteFound(string item)
    {
        if (!Keepsakes.Add(item)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KeepsakePath)!);
            File.WriteAllText(KeepsakePath, JsonSerializer.Serialize(Keepsakes));
        }
        catch { }
        KeepsakesChanged?.Invoke();
    }

    private static HashSet<string> ReadKeepsakes()
    {
        try { return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(KeepsakePath)) ?? new HashSet<string>(StringComparer.Ordinal); }
        catch
        {
            // Read the previous preference file if present so older discoveries survive the move.
            var earlier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KindleHubPro", string.Concat("easter", "-eggs.json"));
            try { return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(earlier)) ?? new HashSet<string>(StringComparer.Ordinal); }
            catch { return new HashSet<string>(StringComparer.Ordinal); }
        }
    }

    private readonly KindleHubCore _core;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly MainViewModel _mainViewModel;
    private string _keepsakeText = string.Join("\n", Keepsakes);

    private string _username = "";
    private string _password = "";
    private string _statusText = "";

    // preference mirrors (bound to the UI; pushed into account state on change)
    private string _profileName = "";
    private int _fontSizePx = 16;
    private string _themeName = "Light";
    private bool _simpleMode;
    private bool _syncEnabled = true;
    private int _noteCount;
    private bool _busy;
    private bool _loading;
    private int[] _avatarCells = new int[ProfileAvatar.CellCount];
    private int _avatarBackgroundIndex;
    private int _avatarInkIndex = 1;
    private string _savedAvatarCode = "";
    private string _recoveryEmail = "";
    private string _inviteCode = "";
    private string _moderatorCode = "";
    private string _modStatsText = "";
    private string _applicationReason = "";
    private string _moderationExperience = "";
    private string _timeUsing = "One to six months";
    private bool _showRecoveryCode;
    private bool _ageEligible;
    private string _storageUsageText = "Account data is compressed and encrypted when synced.";
    private double _storagePercent;

    public string Username { get => _username; set => SetProperty(ref _username, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public bool Busy { get => _busy; set => SetProperty(ref _busy, value); }
    public string RecoveryEmail { get => _recoveryEmail; set => SetProperty(ref _recoveryEmail, value); }
    public string InviteCode { get => _inviteCode; set => SetProperty(ref _inviteCode, value.ToUpperInvariant()); }
    public string ModeratorCode { get => _moderatorCode; set => SetProperty(ref _moderatorCode, value); }
    public string ModStatsText { get => _modStatsText; set => SetProperty(ref _modStatsText, value); }
    public string ApplicationReason { get => _applicationReason; set => SetProperty(ref _applicationReason, value); }
    public string ModerationExperience { get => _moderationExperience; set => SetProperty(ref _moderationExperience, value); }
    public string TimeUsing { get => _timeUsing; set => SetProperty(ref _timeUsing, value); }
    public bool AgeEligible { get => _ageEligible; set => SetProperty(ref _ageEligible, value); }
    public string[] TimeUsingOptions { get; } = { "Less than a month", "One to six months", "Six months to a year", "More than a year" };
    public bool ShowRecoveryCode { get => _showRecoveryCode; set { if (SetProperty(ref _showRecoveryCode, value)) OnPropertyChanged(nameof(RecoveryCode)); } }
    public string RecoveryCode
    {
        get
        {
            var token = _core.CurrentAuthToken;
            if (token.Length != 64) return "Sign in to view your recovery code.";
            var formatted = string.Join("-", Enumerable.Range(0, 8).Select(i => token.Substring(i * 8, 8).ToUpperInvariant()));
            return ShowRecoveryCode ? formatted : "••••••••-••••••••-••••••••-••••••••-••••••••-••••••••-••••••••-••••••••";
        }
    }
    public string StorageUsageText { get => _storageUsageText; private set => SetProperty(ref _storageUsageText, value); }
    public double StoragePercent { get => _storagePercent; private set => SetProperty(ref _storagePercent, value); }

    public bool IsLoggedIn => _mainViewModel.IsAuthenticated;
    public string KeepsakeText { get => _keepsakeText; private set => SetProperty(ref _keepsakeText, value); }
    public bool HasKeepsakes => Keepsakes.Count > 0;
    public string CurrentUserDisplay => _mainViewModel.UserDisplayName;

    public string ProfileName
    {
        get => _profileName;
        set
        {
            if (SetProperty(ref _profileName, value)) SaveProfileNameDebounced();
        }
    }

    public ObservableCollection<AvatarPixelCell> AvatarPixels { get; } = new();
    public string[] AvatarBackgroundOptions => ProfileAvatar.BackgroundNames;
    public string[] AvatarInkOptions => ProfileAvatar.InkNames;
    public int AvatarBackgroundIndex
    {
        get => _avatarBackgroundIndex;
        set { if (SetProperty(ref _avatarBackgroundIndex, Math.Clamp(value, 0, ProfileAvatar.BackgroundColors.Length - 1))) RebuildAvatarPixels(); }
    }
    public int AvatarInkIndex { get => _avatarInkIndex; set => SetProperty(ref _avatarInkIndex, Math.Clamp(value, 0, ProfileAvatar.InkColors.Length - 1)); }
    public DrawingImage? AvatarPreview => ProfileAvatar.Render(ProfileAvatar.Encode(_avatarBackgroundIndex, _avatarCells));
    public bool HasSavedAvatar => ProfileAvatar.TryDecode(_savedAvatarCode, out _, out _);

    public int FontSizePx
    {
        get => _fontSizePx;
        set
        {
            if (SetProperty(ref _fontSizePx, value))
            {
                FontSizeScaler.SetFontSizePx(value);
                if (_core.SetFontSize(value)) PushAsync();
            }
        }
    }

    public string[] FontSizeOptions { get; } = { "Small", "Default", "Large", "Extra large" };
    public string FontSizeName
    {
        get => _fontSizePx switch { 13 => "Small", 19 => "Large", 22 => "Extra large", _ => "Default" };
        set
        {
            var px = value switch { "Small" => 13, "Large" => 19, "Extra large" => 22, _ => 16 };
            FontSizePx = px;
        }
    }

    public string[] ThemeOptions { get; } = AppTheme.Options;

    public string ThemeName
    {
        get => _themeName;
        set
        {
            if (SetProperty(ref _themeName, value))
            {
                // Persist the choice AND apply it — the setting used to be stored
                // but never reached Avalonia, which left the app on the system theme.
                AppTheme.Apply(value);
                if (_core.SetTheme(value.ToLowerInvariant())) PushAsync();
            }
        }
    }

    public bool SimpleMode
    {
        get => _simpleMode;
        set
        {
            if (SetProperty(ref _simpleMode, value) && _core.SetSimpleMode(value))
                PushAsync();
        }
    }

    public bool SyncEnabled
    {
        get => _syncEnabled;
        set
        {
            if (SetProperty(ref _syncEnabled, value) && _core.SetSyncEnabled(value))
                PushAsync();
        }
    }

    public int NoteCount { get => _noteCount; private set => SetProperty(ref _noteCount, value); }
    public string NoteCountLabel => NoteCount == 0 ? "No saved notes yet" : $"{NoteCount} saved note(s)";

    public RelayCommand LoginCommand { get; }
    public RelayCommand RegisterCommand { get; }
    public RelayCommand LogoutCommand { get; }
    public RelayCommand SyncNowCommand { get; }
    public RelayCommand ApplyProfileNameCommand { get; }
    public RelayCommand SaveAvatarCommand { get; }
    public RelayCommand RemoveAvatarCommand { get; }
    public RelayCommand SaveRecoveryEmailCommand { get; }
    public RelayCommand ToggleRecoveryCodeCommand { get; }
    public RelayCommand ViewModeratorStatsCommand { get; }
    public RelayCommand ClaimModeratorCodeCommand { get; }
    public RelayCommand SubmitModeratorApplicationCommand { get; }
    public RelayCommand CompressNowCommand { get; }


    public SettingsViewModel(KindleHubCore core, ILogger<SettingsViewModel> logger, MainViewModel mainViewModel)
    {
        _core = core;
        _logger = logger;
        _mainViewModel = mainViewModel;
        KeepsakesChanged += RefreshKeepsakes;

        SaveAvatarCommand = new RelayCommand(async () => await SaveAvatarAsync(false));
        RemoveAvatarCommand = new RelayCommand(async () => await SaveAvatarAsync(true));
        SaveRecoveryEmailCommand = new RelayCommand(async () => await SaveRecoveryEmailAsync());
        ToggleRecoveryCodeCommand = new RelayCommand(() => ShowRecoveryCode = !ShowRecoveryCode);
        ViewModeratorStatsCommand = new RelayCommand(async () => await ViewModeratorStatsAsync());
        ClaimModeratorCodeCommand = new RelayCommand(async () => await ClaimModeratorCodeAsync());
        SubmitModeratorApplicationCommand = new RelayCommand(async () => await SubmitModeratorApplicationAsync());
        CompressNowCommand = new RelayCommand(async () => await CompressNowAsync());

        LoginCommand = new RelayCommand(async () => await LoginAsync(), () => !Busy);
        RegisterCommand = new RelayCommand(async () => await RegisterAsync(), () => !Busy);
        LogoutCommand = new RelayCommand(Logout);
        SyncNowCommand = new RelayCommand(async () => await SyncNowAsync(), () => _mainViewModel.IsAuthenticated && !Busy);
        ApplyProfileNameCommand = new RelayCommand(async () =>
        {
            _nameTimer?.Stop();
            await SaveProfileNameAsync();
        });

        _mainViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsAuthenticated) or nameof(MainViewModel.UserDisplayName))
            {
                OnPropertyChanged(nameof(IsLoggedIn));
                OnPropertyChanged(nameof(CurrentUserDisplay));
                LoadPreferences();
            }
        };

        LoadPreferences();
    }

    private void RefreshKeepsakes()
    {
        KeepsakeText = string.Join("\n", Keepsakes);
        OnPropertyChanged(nameof(HasKeepsakes));
    }

    /// <summary>Refresh the preference mirrors from the core's account state without
    /// re-triggering the save-on-change logic (the _loading guard blocks it).</summary>
    private void LoadPreferences()
    {
        _loading = true;
        try
        {
            if (!_mainViewModel.IsAuthenticated)
            {
                _profileName = "";
                OnPropertyChanged(nameof(ProfileName));
                LoadAvatar("");
                return;
            }
            var p = _core.CurrentPrefs();
            _profileName = p.ProfileName ?? "";
            OnPropertyChanged(nameof(ProfileName));
            LoadAvatar(p.ProfileAvatar ?? "");
            _fontSizePx = p.FontSizePx is 13 or 16 or 19 or 22 ? p.FontSizePx : 16;
            OnPropertyChanged(nameof(FontSizePx));
            OnPropertyChanged(nameof(FontSizeName));
            _themeName = AppTheme.Normalise(p.Theme);
            OnPropertyChanged(nameof(ThemeName));
            // Apply the stored preference too, so opening Settings is enough to
            // correct a window that started on the system theme.
            AppTheme.Apply(_themeName);
            _simpleMode = p.SimpleMode;
            OnPropertyChanged(nameof(SimpleMode));
            _syncEnabled = p.SyncEnabled;
            OnPropertyChanged(nameof(SyncEnabled));
            NoteCount = p.NoteCount;
            RecoveryEmail = p.RecoveryEmail ?? "";
            ModeratorCode = AccountStateValue("modCode");
            OnPropertyChanged(nameof(RecoveryCode));
            UpdateStorageUsage();
            OnPropertyChanged(nameof(NoteCountLabel));
        }
        finally { _loading = false; }
    }

    private string AccountStateValue(string key)
        => KindleHub.Core.AccountState.GetText(_core.AccountStateJson ?? "", key, "");

    private async Task SaveRecoveryEmailAsync()
    {
        if (!_mainViewModel.IsAuthenticated) return;
        try
        {
            await _core.SetRecoveryEmailAsync(RecoveryEmail, CancellationToken.None);
            await SyncNowAsync(silent: true);
            StatusText = "Recovery email saved. Use it to receive password reset codes.";
        }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    private async Task ViewModeratorStatsAsync()
    {
        try
        {
            var stats = await _core.FetchModeratorStatsAsync(ModeratorCode, CancellationToken.None);
            ModStatsText = $"{stats.Level} moderator · {stats.Users:N0} total users · {stats.OnlineNow:N0} online now · {stats.VisitsToday:N0} visits today · {stats.Visitors7d:N0} unique visitors (7d) · {stats.Messages:N0} messages · {stats.Groups:N0} rooms · {stats.FeedbackOpen:N0} open reports" +
                           (stats.AppsPending.HasValue ? $" · {stats.AppsPending.Value:N0} apps pending" : "") +
                           "\nCounts only. No names, messages, mail, or personal records are returned.";
            _core.SetAccountState(KindleHub.Core.AccountState.WithText(_core.AccountStateJson, "modCode", ModeratorCode.Trim()));
            await SyncNowAsync(silent: true);
            StatusText = "Moderator stats loaded.";
        }
        catch (Exception) { ModStatsText = "Could not load stats. Check the moderator code and connection."; }
    }

    private async Task ClaimModeratorCodeAsync()
    {
        try
        {
            await _core.ClaimModeratorCodeAsync(InviteCode, CancellationToken.None);
            ModeratorCode = InviteCode.Trim().ToUpperInvariant();
            _core.SetAccountState(KindleHub.Core.AccountState.WithText(_core.AccountStateJson, "modCode", ModeratorCode));
            await SyncNowAsync(silent: true);
            InviteCode = "";
            StatusText = "Request sent. The admin will approve access shortly.";
        }
        catch (Exception) { StatusText = "That code could not be claimed. It may be invalid, used, or temporarily unavailable."; }
    }

    private async Task SubmitModeratorApplicationAsync()
    {
        if (!AgeEligible)
        {
            StatusText = "Moderator applications are only available to readers aged 13 or over.";
            return;
        }
        try
        {
            await _core.SubmitModeratorApplicationAsync(TimeUsing, "13 or over", ApplicationReason, ModerationExperience, CancellationToken.None);
            ApplicationReason = "";
            ModerationExperience = "";
            StatusText = "Application sent. The owner reviews applications by hand.";
        }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    private async Task CompressNowAsync()
    {
        await SyncNowAsync(silent: false);
        UpdateStorageUsage();
        StatusText = "Account state has been compressed, encrypted, and synced.";
    }

    private void UpdateStorageUsage()
    {
        var state = _core.AccountStateJson ?? "{}";
        var packed = KindleHub.Core.AccountEncryption.PackAccount(state, _core.CurrentAuthToken);
        var bytes = System.Text.Encoding.UTF8.GetByteCount(packed);
        StoragePercent = Math.Clamp(bytes / 1048576d * 100d, 0d, 100d);
        StorageUsageText = $"Encrypted compressed cloud copy: {bytes:N0} bytes of 1.00 MB ({bytes / 1048576d:P0}). Local JSON: {System.Text.Encoding.UTF8.GetByteCount(state):N0} bytes.";
    }

    // ── auth ──────────────────────────────────────────────────────────────
    public async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            StatusText = "Enter username and password";
            return;
        }
        Busy = true;
        StatusText = "Logging in…";
        try
        {
            var ok = await _mainViewModel.LoginAsync(Username, Password);
            if (ok) { Username = ""; Password = ""; StatusText = "Signed in."; }
            else StatusText = _mainViewModel.StatusText;
        }
        finally { Busy = false; }
    }

    public async Task RegisterAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            StatusText = "Choose a username and a password (8+ characters)";
            return;
        }
        Busy = true;
        StatusText = "Registering…";
        try
        {
            var ok = await _mainViewModel.RegisterAsync(Username, Password);
            if (ok) { Username = ""; Password = ""; StatusText = "Account created and signed in."; }
            else StatusText = _mainViewModel.StatusText;
        }
        finally { Busy = false; }
    }

    public void Logout()
    {
        _mainViewModel.Logout();
        LoadPreferences();
        StatusText = "Logged out";
    }

    // ── profile name (debounced ~1.2s after typing stops; Enter commits immediately) ──
    private Avalonia.Threading.DispatcherTimer? _nameTimer;

    private void SaveProfileNameDebounced()
    {
        if (_loading || !_mainViewModel.IsAuthenticated) return;
        if (_nameTimer == null)
        {
            _nameTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1200)
            };
            _nameTimer.Tick += (_, _) =>
            {
                _nameTimer!.Stop();
                _ = SaveProfileNameAsync();
            };
        }
        _nameTimer.Stop();
        _nameTimer.Start();
    }

    private async Task SaveProfileNameAsync()
    {
        if (_loading || !_mainViewModel.IsAuthenticated) return;
        var name = ProfileName.Trim();
        if (name.Length == 0) name = _mainViewModel.UserDisplayName;
        if (!_core.SetProfileName(name)) return;
        // The display name the rest of the app shows follows the same key the web uses.
        _core.UpdateDisplayName(name);
        await SyncNowAsync(silent: true);
        StatusText = "Profile name saved (synced).";
    }

    private void LoadAvatar(string code)
    {
        _savedAvatarCode = code;
        if (ProfileAvatar.TryDecode(code, out var background, out var cells))
        {
            _avatarBackgroundIndex = background;
            _avatarCells = cells;
        }
        else
        {
            _avatarBackgroundIndex = 0;
            _avatarCells = new int[ProfileAvatar.CellCount];
        }
        OnPropertyChanged(nameof(AvatarBackgroundIndex));
        OnPropertyChanged(nameof(HasSavedAvatar));
        OnPropertyChanged(nameof(AvatarPreview));
        RebuildAvatarPixels();
    }

    public void PaintAvatarPixel(int index)
    {
        if (index < 0 || index >= _avatarCells.Length) return;
        _avatarCells[index] = AvatarInkIndex;
        RebuildAvatarPixels();
        OnPropertyChanged(nameof(AvatarPreview));
    }

    private void RebuildAvatarPixels()
    {
        if (AvatarPixels is null) return;
        AvatarPixels.Clear();
        var background = new SolidColorBrush(Color.Parse(ProfileAvatar.BackgroundColors[_avatarBackgroundIndex]));
        for (var i = 0; i < _avatarCells.Length; i++)
        {
            var ink = _avatarCells[i];
            var color = ink == 0 ? background : new SolidColorBrush(Color.Parse(ProfileAvatar.InkColors[ink]));
            AvatarPixels.Add(new AvatarPixelCell(i, color));
        }
        OnPropertyChanged(nameof(AvatarPixels));
        OnPropertyChanged(nameof(AvatarPreview));
    }

    private async Task SaveAvatarAsync(bool remove)
    {
        if (!_mainViewModel.IsAuthenticated) return;
        var code = remove ? "" : ProfileAvatar.Encode(_avatarBackgroundIndex, _avatarCells);
        if (!_core.SetProfileAvatar(code))
        {
            StatusText = "Couldn't save that profile picture.";
            return;
        }
        _savedAvatarCode = code;
        OnPropertyChanged(nameof(HasSavedAvatar));
        await SyncNowAsync(silent: false);
        await _core.PingPresenceAsync("", CancellationToken.None);
    }

    // ── sync ──────────────────────────────────────────────────────────────
    private async void PushAsync()
    {
        if (_loading) return;
        try { await SyncNowAsync(silent: true); }
        catch (Exception ex) { _logger.LogDebug(ex, "preference push failed"); }
    }

    public async Task SyncNowAsync(bool silent = false)
    {
        if (!_mainViewModel.IsAuthenticated) { StatusText = "Not logged in"; return; }
        if (!silent) StatusText = "Syncing…";
        try
        {
            // Push the locally held state (which the preference setters mutate). Never push
            // an empty object over a populated vault — that would wipe the account.
            var state = _core.AccountStateJson;
            if (string.IsNullOrEmpty(state))
            {
                // Nothing loaded locally yet: fetch first so we never overwrite the vault blindly.
                var pulled = await _core.LoadAccountStateAsync(CancellationToken.None);
                if (!pulled)
                {
                    state = "{}";
                }
                else
                {
                    LoadPreferences();
                    if (silent) return;
                    StatusText = "Downloaded the cloud copy. Make a change and it'll sync back.";
                    return;
                }
            }
            var ok = await _core.SyncAccountAsync(state, CancellationToken.None);
            OnPropertyChanged(nameof(NoteCountLabel));
            if (!silent) StatusText = ok ? "Sync complete." : "Sync failed.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed");
            if (!silent) StatusText = $"Sync failed: {ex.Message}";
        }
    }
}

public sealed class AvatarPixelCell
{
    public int Index { get; }
    public IBrush Color { get; }
    public AvatarPixelCell(int index, IBrush color) { Index = index; Color = color; }
}
