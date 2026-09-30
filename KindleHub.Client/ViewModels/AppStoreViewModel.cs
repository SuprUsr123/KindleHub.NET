using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using KindleHub.Core;
using Microsoft.Extensions.Logging;

namespace KindleHub.Client.ViewModels;

public class AppStoreViewModel : ViewModelBase
{
    private readonly KindleHubCore _core;
    private readonly ILogger<AppStoreViewModel> _logger;

    private ObservableCollection<AppCatalog> _apps = new();
    private AppCatalog? _selectedApp;
    private string _searchText = "";
    private string _selectedCategory = "All";
    private bool _isLoading;
    private string _statusText = "";

    // publish flow
    private bool _publishOpen;
    private string _publishName = "";
    private string _publishCategory = "Fun";
    private string _publishHtmlPath = "";
    private int _publishHtmlSize;
    private bool _publishing;

    // my published apps
    private ObservableCollection<OwnAppItem> _myApps = new();

    public ObservableCollection<AppCatalog> Apps { get => _apps; set => SetProperty(ref _apps, value); }

    public AppCatalog? SelectedApp
    {
        get => _selectedApp;
        set
        {
            if (SetProperty(ref _selectedApp, value))
                DownloadAndOpenCommand.RaiseCanExecuteChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) { _ = SearchAppsAsync(); } }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set { if (SetProperty(ref _selectedCategory, value)) { _ = SearchAppsAsync(); } }
    }

    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string[] Categories { get; } = { "All", "Games", "Fun", "Tools", "News & Media", "Education" };

    public bool PublishOpen { get => _publishOpen; set { _publishOpen = value; OnPropertyChanged(); CancelPublishCommand.RaiseCanExecuteChanged(); } }
    public string PublishName { get => _publishName; set { if (SetProperty(ref _publishName, value)) PublishAppCommand.RaiseCanExecuteChanged(); } }
    public string PublishCategory { get => _publishCategory; set => SetProperty(ref _publishCategory, value); }
    public string PublishHtmlPath { get => _publishHtmlPath; set => SetProperty(ref _publishHtmlPath, value); }
    public int PublishHtmlSize { get => _publishHtmlSize; set => SetProperty(ref _publishHtmlSize, value); }
    public bool Publishing { get => _publishing; set => SetProperty(ref _publishing, value); }
    public string PublishSizeLabel => PublishHtmlSize > 0 ? $"{PublishHtmlSize / 1024.0:0.#} KB · {PublishHtmlSize:N0} chars" : "";

    /// <summary>Describes where the pending HTML came from — a file, or the clipboard.</summary>
    public string PublishSourceLabel
    {
        get
        {
            if (string.IsNullOrEmpty(_pendingHtml)) return "No HTML loaded yet.";
            if (!string.IsNullOrEmpty(PublishHtmlPath)) return System.IO.Path.GetFileName(PublishHtmlPath);
            return "Pasted from clipboard";
        }
    }

    /// <summary>True once we actually hold the HTML to publish. The payload is the
    /// source of truth; a file on disk is no longer required.</summary>
    public bool HasPublishHtml => !string.IsNullOrEmpty(_pendingHtml);

    public ObservableCollection<OwnAppItem> MyApps { get => _myApps; set => SetProperty(ref _myApps, value); }
    public bool HasMyApps => _myApps.Count > 0;
    public bool CanPublish => _core.IsAuthenticated;

    public RelayCommand DownloadAndOpenCommand { get; }
    public RelayCommand ShowPublishCommand { get; }
    public RelayCommand CancelPublishCommand { get; }
    public RelayCommand PublishAppCommand { get; }
    public RelayCommand RefreshMyAppsCommand { get; }
    public RelayCommand<OwnAppItem> RemoveAppCommand { get; }
    public RelayCommand<OwnAppItem> RunOwnAppCommand { get; }

    public AppStoreViewModel(KindleHubCore core, ILogger<AppStoreViewModel> logger)
    {
        _core = core;
        _logger = logger;
        DownloadAndOpenCommand = new RelayCommand(async () => await DownloadAndOpenAsync(), () => SelectedApp != null);
        ShowPublishCommand = new RelayCommand(() => { if (!CanPublish) { StatusText = "Sign in to publish an app."; return; } PublishOpen = true; });
        CancelPublishCommand = new RelayCommand(() =>
        {
            PublishOpen = false;
            ResetPublishForm();
        });
        PublishAppCommand = new RelayCommand(async () => await PublishAsync(),
            () => !Publishing && CanPublish && !string.IsNullOrWhiteSpace(PublishName) && HasPublishHtml);
        RefreshMyAppsCommand = new RelayCommand(() => RefreshMyApps());
        RemoveAppCommand = new RelayCommand<OwnAppItem>(async a => await RemoveOwnAppAsync(a));
        RunOwnAppCommand = new RelayCommand<OwnAppItem>(async a => await RunOwnAppAsync(a));

        RefreshMyApps();
        Dispatcher.UIThread.Post(async () => await SearchAppsAsync());
    }

    private void LogException(string context, Exception ex)
    {
        Console.WriteLine($"[KindleHub Debug] {context}: {ex}");
        _logger.LogError(ex, "[KindleHub Debug] {Context}", context);
    }

    public async Task SearchAppsAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusText = "Loading store…";
        try
        {
            var category = SelectedCategory == "All" ? "" : SelectedCategory;
            var apps = await _core.FetchAppsAsync(category, SearchText, 50, CancellationToken.None);
            Apps.Clear();
            foreach (var app in apps) Apps.Add(app);
            StatusText = apps.Count > 0 ? $"{apps.Count} app(s)" : "No apps found";
        }
        catch (Exception ex)
        {
            LogException("Store search failed", ex);
            StatusText = "Couldn't reach the store.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task DownloadAndOpenAsync()
    {
        if (SelectedApp == null) return;
        IsLoading = true;
        StatusText = $"Downloading \"{SelectedApp.Name}\"…";
        try
        {
            var html = await SaveAppHtmlAsync(SelectedApp.Id, SelectedApp.Name, null, CancellationToken.None);
            _ = _core.CountAppDownloadAsync(SelectedApp.Id, CancellationToken.None);
            StatusText = $"Opened \"{SelectedApp.Name}\" in your browser.";
            OpenInBrowser(html);
        }
        catch (Exception ex)
        {
            LogException("Failed to download/open app", ex);
            StatusText = "Failed to download app.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<string> SaveAppHtmlAsync(string appId, string name, string? ownerSecret, CancellationToken ct)
    {
        var downloaded = string.IsNullOrEmpty(ownerSecret)
            ? await _core.DownloadAppAsync(appId, ct)
            : await _core.DownloadAppWithSecretAsync(appId, ownerSecret, ct);
        var safe = new string(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        if (safe.Length == 0) safe = "app";
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KindleHubPro", "apps");
        Directory.CreateDirectory(dir);
        var htmlPath = Path.Combine(dir, safe + ".html");
        await File.WriteAllTextAsync(htmlPath, downloaded.Html ?? "", ct);
        return htmlPath;
    }

    // ── publish from a single HTML file, or straight from the clipboard ─────
    public async Task SetPublishFileAsync(string path, byte[] bytes)
    {
        try
        {
            var html = System.Text.Encoding.UTF8.GetString(bytes);
            AcceptPublishHtml(html, path, Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception ex)
        {
            _pendingHtml = null;
            LogException("SetPublishFileAsync failed while reading HTML", ex);
            StatusText = "That file doesn't look like UTF-8 HTML.";
        }
        PublishAppCommand.RaiseCanExecuteChanged();
        await Task.CompletedTask;
    }

    /// <summary>Use HTML that came from the clipboard rather than a file. The
    /// payload is what actually gets published, so a backing file is optional —
    /// <paramref name="origin"/> is only a label describing where it came from.</summary>
    public async Task SetPublishHtmlTextAsync(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            StatusText = "The clipboard doesn't contain any text.";
            return;
        }
        AcceptPublishHtml(html, null, "pasted from clipboard");
        PublishAppCommand.RaiseCanExecuteChanged();
        await Task.CompletedTask;
    }

    private void AcceptPublishHtml(string html, string? path, string defaultName)
    {
        _pendingHtml = html;
        PublishHtmlPath = path ?? "";
        PublishHtmlSize = html.Length;
        OnPropertyChanged(nameof(PublishSizeLabel));
        OnPropertyChanged(nameof(PublishSourceLabel));
        if (string.IsNullOrWhiteSpace(PublishName)) PublishName = defaultName;
        if (html.Length > 550000) StatusText = $"That's {html.Length:N0} characters — the store cap is ~512 KB.";
        else StatusText = $"Loaded {html.Length:N0} characters of HTML.";
    }

    private string? _pendingHtml;

    private void ResetPublishForm()
    {
        PublishName = "";
        PublishHtmlPath = "";
        PublishHtmlSize = 0;
        _pendingHtml = null;
        OnPropertyChanged(nameof(PublishSizeLabel));
        OnPropertyChanged(nameof(PublishSourceLabel));
        OnPropertyChanged(nameof(HasPublishHtml));
        PublishAppCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Reads HTML straight off the system clipboard. Routed through the
    /// view because only a TopLevel owns a clipboard.</summary>
    public void SetPublishHtmlText(string html)
    {
        _ = SetPublishHtmlTextAsync(html);
    }

    public void NotifyHtmlChanged()
    {
        OnPropertyChanged(nameof(PublishSizeLabel));
        OnPropertyChanged(nameof(PublishSourceLabel));
        OnPropertyChanged(nameof(HasPublishHtml));
        PublishAppCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Drop the pending HTML without closing the dialog, so the author can
    /// load a different source.</summary>
    public void ClearPublishHtml()
    {
        _pendingHtml = null;
        PublishHtmlPath = "";
        PublishHtmlSize = 0;
        StatusText = "Cleared. Load a file or paste new HTML.";
        NotifyHtmlChanged();
    }

    private async Task PublishAsync()
    {
        if (!CanPublish) { StatusText = "Sign in to publish an app."; return; }
        if (string.IsNullOrEmpty(_pendingHtml)) { StatusText = "Choose an HTML file or paste your HTML first."; return; }
        if (string.IsNullOrWhiteSpace(PublishName)) { StatusText = "Give the app a name."; return; }
        if (_pendingHtml.Length > 550000) { StatusText = "App exceeds the 512 KB store cap."; return; }

        Publishing = true;
        StatusText = "Publishing…";
        try
        {
            var cat = PublishCategory == "All" ? "Fun" : PublishCategory;
            var result = await _core.PublishAppAsync(PublishName.Trim(), _pendingHtml, cat, CancellationToken.None);
            StatusText = $"Published \"{result.Name}\". It stays pending until the store auto-reviewer approves it — then it appears in the catalogue for everyone.";
            RefreshMyApps();
            PublishOpen = false;
            ResetPublishForm();
            await SearchAppsAsync();
        }
        catch (Exception ex)
        {
            LogException("Publish failed", ex);
            StatusText = "Publish failed: " + ex.Message;
        }
        finally
        {
            Publishing = false;
        }
    }

private void RefreshMyApps()
    {
        MyApps.Clear();
        foreach (var a in _core.OwnPublishedApps())
            MyApps.Add(new OwnAppItem(a.Id, a.Name, a.OwnerSecret));
        RemoveAppCommand.RaiseCanExecuteChanged();
    }

    public async Task RemoveOwnAppAsync(OwnAppItem? item)
    {
        if (item == null) return;
        try
        {
            var ok = await _core.UnpublishAppAsync(item.Id, CancellationToken.None);
            StatusText = ok ? $"Removed \"{item.Name}\"." : "Couldn't remove that app.";
            if (ok) RefreshMyApps();
        }
        catch (Exception ex)
        {
            LogException("RemoveOwnAppAsync failed", ex);
            StatusText = "Remove failed: " + ex.Message;
        }
    }

    public async Task RunOwnAppAsync(OwnAppItem? item)
    {
        if (item == null) return;
        try
        {
            StatusText = $"Loading \"{item.Name}\"…";
            var html = await SaveAppHtmlAsync(item.Id, item.Name, item.OwnerSecret, CancellationToken.None);
            OpenInBrowser(html);
            StatusText = $"Opened your app \"{item.Name}\".";
        }
        catch (Exception ex)
        {
            LogException("RunOwnAppAsync failed", ex);
            StatusText = "Couldn't load it: " + ex.Message;
        }
    }

    private void OpenInBrowser(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"@\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                System.Diagnostics.Process.Start("open", $"@\"{path}\"");
            else
                System.Diagnostics.Process.Start("xdg-open", $"@\"{path}\"");
        }
        catch
        {
            // Headless session: the file is still on disk.
            StatusText = $"No browser available. The HTML was saved to: {path}";
        }
    }
}

/// <summary>One of this device's own published apps (tracked locally since the
/// server hides unapproved rows from catalogue reads). Carries the owner_secret
/// so the row can be re-fetched while still review='pending'.</summary>
    public sealed class OwnAppItem
    {
        public OwnAppItem(string id, string name, string ownerSecret) { Id = id; Name = name; OwnerSecret = ownerSecret; }
        public string Id { get; }
        public string Name { get; }
        public string OwnerSecret { get; }
        public bool Pending => true; // server auto-review approves within ~15 s for clean apps
    }
