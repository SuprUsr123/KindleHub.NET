using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KindleHub.Core;
using Microsoft.Extensions.Logging;

namespace KindleHub.Client.ViewModels;

/// <summary>
/// Topics view: the signed-in KV directory for "TOPIC:" rooms (the gateway refuses a
/// blind kh_groups scan, so the official web client enumerates them the same way).
/// Opening a topic hands the room to Messages via RoomRegistry + MainViewModel.NavigateTo.
/// </summary>
public class CommunityViewModel : ViewModelBase
{
    private readonly KindleHubCore _core;
    private readonly ILogger<CommunityViewModel> _logger;
    private readonly MainViewModel _nav;

    private ObservableCollection<TopicListing> _topics = new();
    private System.Collections.Generic.List<TopicListing> _allTopics = new();
    private TopicListing? _selectedTopic;
    private string _searchText = "";
    private string _selectedCategory = "All";
    private bool _isLoading;
    private string _statusText = "Loading topics…";

    // create-topic fields
    private bool _showCreate;
    private string _newTitle = "";
    private string _newBlurb = "";
    private string _newCategory = "General";
    private string _joinCode = "";

    public ObservableCollection<TopicListing> Topics { get => _topics; set => SetProperty(ref _topics, value); }
    public TopicListing? SelectedTopic { get => _selectedTopic; set { if (SetProperty(ref _selectedTopic, value)) { OpenCommand.RaiseCanExecuteChanged(); OnPropertyChanged(nameof(HasSelection)); } } }
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) ApplyFilter(); } }
    public string SelectedCategory { get => _selectedCategory; set { if (SetProperty(ref _selectedCategory, value)) ApplyFilter(); } }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public bool HasSelection => SelectedTopic != null;
    public bool HasTopics => Topics.Count > 0;
    public string JoinCode { get => _joinCode; set { if (SetProperty(ref _joinCode, value)) JoinCommand.RaiseCanExecuteChanged(); } }

    public bool ShowCreate { get => _showCreate; set => SetProperty(ref _showCreate, value); }
    public string NewTitle { get => _newTitle; set { if (SetProperty(ref _newTitle, value)) CreateCommand.RaiseCanExecuteChanged(); } }
    public string NewBlurb { get => _newBlurb; set => SetProperty(ref _newBlurb, value); }
    public string NewCategory { get => _newCategory; set => SetProperty(ref _newCategory, value); }

    public string[] AllCategories { get; } = new[] { "All" }.Concat(TopicHelper.Categories.Select(c => c.Name)).ToArray();
    public string[] TopicCategories { get; } = TopicHelper.Categories.Select(c => c.Name).ToArray();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand CreateCommand { get; }
    public RelayCommand CancelCreateCommand { get; }
    public RelayCommand JoinCommand { get; }
    public RelayCommand ShowCreateCommand { get; }

    public CommunityViewModel(KindleHubCore core, ILogger<CommunityViewModel> logger, MainViewModel nav)
    {
        _core = core;
        _logger = logger;
        _nav = nav;

        RefreshCommand = new RelayCommand(async () => await RefreshAsync());
        OpenCommand = new RelayCommand(async () => await OpenSelectedAsync(), () => SelectedTopic != null);
        ShowCreateCommand = new RelayCommand(() =>
        {
            if (!_core.IsAuthenticated) { StatusText = "Sign in to start a topic."; return; }
            ShowCreate = true;
        });
        CreateCommand = new RelayCommand(async () => await CreateTopicAsync(), () => !string.IsNullOrWhiteSpace(NewTitle));
        CancelCreateCommand = new RelayCommand(() => { ShowCreate = false; NewTitle = ""; NewBlurb = ""; });
        JoinCommand = new RelayCommand(async () => await JoinByCodeAsync(), () => !string.IsNullOrWhiteSpace(JoinCode));

        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (!TryAuthed()) { Topics.Clear(); return; }
        IsLoading = true;
        try
        {
            _allTopics = await _core.ListOpenTopicsAsync(200, CancellationToken.None);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Topic list failed");
            StatusText = "Couldn't load topics. Sign in or check the network.";
            _allTopics.Clear();
            Topics.Clear();
            OnPropertyChanged(nameof(HasTopics));
        }
        finally { IsLoading = false; }
    }

    private void ApplyFilter()
    {
        var needle = SearchText.Trim();
        var want = _selectedCategory == "All" ? null : TopicHelper.CategoryIdByName(_selectedCategory);
        var filtered = _allTopics.Where(x => (want == null || x.CategoryId == want) &&
            (needle.Length == 0 || x.Title.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
             (x.Blurb ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase) ||
             (x.Creator ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.CreatedAt).ToList();
        if (SelectedTopic != null && !filtered.Any(x => x.Code == SelectedTopic.Code))
            SelectedTopic = null;
        Topics = new ObservableCollection<TopicListing>(filtered);
        StatusText = $"Showing {filtered.Count} of {_allTopics.Count} topics.";
        OnPropertyChanged(nameof(HasTopics));
    }

    public async Task OpenSelectedAsync()
    {
        var t = SelectedTopic;
        if (t == null) return;
        try
        {
            var group = await _core.LookupGroupAsync(t.Code, CancellationToken.None)
                        ?? new Group { Code = t.Code, Name = t.RawName ?? "Topic", Creator = t.Creator ?? "" };
            RoomRegistry.Open(group);
            _nav.NavigateTo("Messages");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Open topic failed");
            StatusText = "Couldn't join that topic.";
        }
    }

    public async Task JoinByCodeAsync()
    {
        var digits = new string((JoinCode ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length < 12)
        {
            digits = digits.PadLeft(12, '0');
            if (digits.Length != 12) { StatusText = "Room code is 12 digits."; return; }
        }
        else digits = digits[^12..];
        try
        {
            var group = await _core.JoinGroupByCodeAsync(digits, CancellationToken.None);
            RoomRegistry.Open(group);
            _nav.NavigateTo("Messages");
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Join by code failed");
            StatusText = "Couldn't load that room — check the code.";
        }
    }

    private async Task CreateTopicAsync()
    {
        var title = NewTitle.Trim();
        if (title.Length == 0) return;
        if (!TryAuthed()) return;
        ShowCreate = false;
        try
        {
            var categoryId = TopicHelper.CategoryIdByName(NewCategory);
            var made = await _core.CreateTopicAsync(title, NewBlurb.Trim(), categoryId, CancellationToken.None);
            NewTitle = ""; NewBlurb = "";
            await RefreshAsync();
            SelectedTopic = Topics.FirstOrDefault(x => x.Code == made.Code);
            if (SelectedTopic != null)
            {
                RoomRegistry.Open(made);
                _nav.NavigateTo("Messages");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Create topic failed");
            StatusText = ex.Message.Contains("TOPIC:", StringComparison.Ordinal) ? "That name is taken." : "Couldn't create the topic.";
        }
    }

    private bool TryAuthed()
    {
        if (!_core.IsAuthenticated) { StatusText = "Sign in to browse topics (they use your account)."; return false; }
        return true;
    }
}
