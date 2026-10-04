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
    private const string StandaloneAppShim = """
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data: blob:; media-src data: blob:; font-src data:; connect-src 'none'; form-action 'none'; base-uri 'none'; frame-src 'none'">
        <script>(function(){
          var scope=__KH_SCOPE__, prefix="kh_app_"+scope+"_", memory={}, nativeStore=null;
          try { nativeStore=window.localStorage; var probe=prefix+"probe"; nativeStore.setItem(probe,"1"); nativeStore.removeItem(probe); } catch(e) { nativeStore=null; }
          function get(k){k=prefix+String(k);try{return nativeStore?nativeStore.getItem(k):(Object.prototype.hasOwnProperty.call(memory,k)?memory[k]:null);}catch(e){return Object.prototype.hasOwnProperty.call(memory,k)?memory[k]:null;}}
          function set(k,v){k=prefix+String(k);v=String(v);memory[k]=v;try{if(nativeStore)nativeStore.setItem(k,v);}catch(e){}}
          function remove(k){k=prefix+String(k);delete memory[k];try{if(nativeStore)nativeStore.removeItem(k);}catch(e){}}
          function keys(){var out={},i,k;try{if(nativeStore)for(i=0;i<nativeStore.length;i++){k=nativeStore.key(i);if(k&&k.indexOf(prefix)===0)out[k]=1;}}catch(e){}for(k in memory)if(Object.prototype.hasOwnProperty.call(memory,k)&&k.indexOf(prefix)===0)out[k]=1;return Object.keys(out);}
          var store={getItem:get,setItem:set,removeItem:remove,key:function(i){var a=keys();return i>=0&&i<a.length?a[i].slice(prefix.length):null;},clear:function(){var a=keys(),i;for(i=0;i<a.length;i++)remove(a[i].slice(prefix.length));}};
          try{Object.defineProperty(store,"length",{get:function(){return keys().length;}});Object.defineProperty(window,"localStorage",{configurable:true,value:store});Object.defineProperty(window,"sessionStorage",{configurable:true,value:store});}catch(e){}
          window._khRunSave=window._khRunSave||function(k,v){try{store.setItem("run:"+k,JSON.stringify(v==null?null:v));}catch(e){}};
          window._khRunLoad=window._khRunLoad||function(k){try{var v=store.getItem("run:"+k);return v==null?null:JSON.parse(v);}catch(e){return null;}};
          window.NOW=window.NOW||function(){return new Date();};window.saveGame=window.saveGame||function(){};
          function box(m,extra){try{var old=document.getElementById("__kh_notice");if(old)old.parentNode.removeChild(old);var d=document.createElement("div"),c=document.createElement("div"),t=document.createElement("div"),b=document.createElement("button");d.id="__kh_notice";d.style.cssText="position:fixed;inset:0;z-index:2147483647;background:rgba(0,0,0,.48);font-family:inherit";c.style.cssText="position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);max-width:82%;min-width:190px;background:#fff;color:#111;border:2px solid #111;border-radius:10px;padding:16px;text-align:center";t.style.cssText="font-size:15px;line-height:1.45;margin-bottom:14px;overflow-wrap:anywhere";t.appendChild(document.createTextNode(String(m==null?"":m)));c.appendChild(t);if(extra)c.appendChild(extra);b.type="button";b.appendChild(document.createTextNode("OK"));b.style.cssText="font:inherit;font-weight:700;padding:9px 26px;min-height:42px;border:0;border-radius:8px;background:#111;color:#fff";b.onclick=function(){if(d.parentNode)d.parentNode.removeChild(d);};c.appendChild(b);d.appendChild(c);(document.body||document.documentElement).appendChild(d);b.focus();}catch(e){}}
          window.alert=function(m){box(m);};window.confirm=function(m){box(m);return true;};window.prompt=function(m,d){box(m);return d==null?"":String(d);};
        })();</script>
        """;

    private readonly KindleHubCore _core;
    private readonly ILogger<AppStoreViewModel> _logger;

    private ObservableCollection<StoreAppItem> _apps = new();
    private StoreAppItem? _selectedApp;
    private readonly Dictionary<string, string> _downloadedApps = LoadDownloadedApps();
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

    public ObservableCollection<StoreAppItem> Apps { get => _apps; set => SetProperty(ref _apps, value); }

    public StoreAppItem? SelectedApp
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
            foreach (var app in apps)
            {
                var item = new StoreAppItem(app);
                item.IsDownloaded = TryGetDownloadedPath(app.Id, out _);
                Apps.Add(item);
            }
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
        await OpenOrDownloadAsync(SelectedApp);
    }

    public async Task OpenOrDownloadAsync(StoreAppItem item)
    {
        if (item.IsDownloaded && TryGetDownloadedPath(item.Id, out var existingPath) && File.Exists(existingPath))
        {
            if (OpenInBrowser(existingPath))
                StatusText = $"Launched \"{item.Name}\". Saved in {AppsDirectory}.";
            else
                StatusText = $"\"{item.Name}\" is saved in {AppsDirectory}, but no browser could be opened.";
            return;
        }

        IsLoading = true;
        StatusText = $"Downloading \"{item.Name}\"…";
        try
        {
            var html = await SaveAppHtmlAsync(item.Id, item.Name, null, CancellationToken.None);
            _downloadedApps[item.Id] = Path.GetFileName(html);
            SaveDownloadedApps();
            item.IsDownloaded = true;
            _ = _core.CountAppDownloadAsync(item.Id, CancellationToken.None);
            var opened = OpenInBrowser(html);
            StatusText = opened
                ? $"Downloaded \"{item.Name}\". Saved in {AppsDirectory}. Opened in your browser."
                : $"Downloaded \"{item.Name}\" to {AppsDirectory}, but no browser could be opened.";
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
        var safeId = new string(appId.Where(char.IsLetterOrDigit).Take(40).ToArray());
        if (safeId.Length == 0) safeId = "app";
        var htmlPath = Path.Combine(dir, safe + "-" + safeId + ".html");
        var html = AddStandaloneAppSupport(downloaded.Html ?? "", appId);
        await File.WriteAllTextAsync(htmlPath, html, ct);
        return htmlPath;
    }

    private static string AddStandaloneAppSupport(string html, string appId)
    {
        var scope = System.Text.Json.JsonSerializer.Serialize(new string(appId.Where(char.IsLetterOrDigit).Take(48).ToArray()));
        var shim = StandaloneAppShim.Replace("__KH_SCOPE__", scope, StringComparison.Ordinal);
        var head = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (head >= 0)
        {
            var end = html.IndexOf('>', head);
            if (end >= 0) return html.Insert(end + 1, shim);
        }

        var htmlTag = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        if (htmlTag >= 0)
        {
            var end = html.IndexOf('>', htmlTag);
            if (end >= 0) return html.Insert(end + 1, "<head>" + shim + "</head>");
        }

        return "<!doctype html><html><head><meta charset=\"utf-8\">" + shim + "</head><body>" + html + "</body></html>";
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
            var opened = OpenInBrowser(html);
            StatusText = opened
                ? $"Opened your app \"{item.Name}\". Saved in {AppsDirectory}."
                : $"Your app \"{item.Name}\" is saved in {AppsDirectory}, but no browser could be opened.";
        }
        catch (Exception ex)
        {
            LogException("RunOwnAppAsync failed", ex);
            StatusText = "Couldn't load it: " + ex.Message;
        }
    }

    private static string AppsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KindleHubPro", "apps");

    private static Dictionary<string, string> LoadDownloadedApps()
    {
        try
        {
            var json = File.ReadAllText(Path.Combine(AppsDirectory, "downloaded-apps.json"));
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (Exception) { return new Dictionary<string, string>(); }
    }

    private void SaveDownloadedApps()
    {
        Directory.CreateDirectory(AppsDirectory);
        File.WriteAllText(Path.Combine(AppsDirectory, "downloaded-apps.json"),
            System.Text.Json.JsonSerializer.Serialize(_downloadedApps));
    }

    private bool TryGetDownloadedPath(string appId, out string path)
    {
        if (_downloadedApps.TryGetValue(appId, out var fileName) && Path.GetFileName(fileName) == fileName)
        {
            path = Path.Combine(AppsDirectory, fileName);
            if (File.Exists(path)) return true;
        }
        path = string.Empty;
        return false;
    }

    private static bool OpenInBrowser(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                StartBrowserProcess("open", path);
            else
                StartBrowserProcess("xdg-open", path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void StartBrowserProcess(string command, string path)
    {
        var start = new System.Diagnostics.ProcessStartInfo(command) { UseShellExecute = false };
        start.ArgumentList.Add(path);
        System.Diagnostics.Process.Start(start);
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

public sealed class StoreAppItem : ViewModelBase
{
    private bool _isDownloaded;
    public StoreAppItem(AppCatalog app) => Catalog = app;
    public AppCatalog Catalog { get; }
    public string Id => Catalog.Id;
    public string Name => Catalog.Name;
    public string Author => Catalog.Author;
    public string Category => Catalog.Category;
    public string IconArt => Catalog.IconArt;
    public bool HasIcon => Catalog.HasIcon;
    public bool ShowInitials => Catalog.ShowInitials;
    public string Initials => Catalog.Initials;
    public string RatingFormatted => Catalog.RatingFormatted;
    public string DownloadsFormatted => Catalog.DownloadsFormatted;
    public bool IsDownloaded
    {
        get => _isDownloaded;
        set { if (SetProperty(ref _isDownloaded, value)) OnPropertyChanged(nameof(ActionLabel)); }
    }
    public string ActionLabel => IsDownloaded ? "Launch" : "Download";
}
