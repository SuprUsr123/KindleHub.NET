using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using KindleHub.Client.Converters;
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
    private static readonly HttpClient MemeTemplateHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
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
    private string _section = "Posts";
    private string _postText = "";
    private Message? _reportingPost;
    private string _postReportNote = "";
    private CommunityMember? _selectedMember;
    private string _cloudAppId = "stronghold";
    private string _cloudRoom = "world";
    private MemeSceneOption _selectedMemeScene = null!;
    private Bitmap? _memePicture;
    private MemeCaptionSlot? _selectedMemeCaption;
    private OutsideMemeTemplate? _selectedOutsideMemeTemplate;
    private bool _socialLoading;
    private string _profileName = "";
    private string _profilePronouns = "";
    private string _profileHobbies = "";
    private string _profileBio = "";
    private string _profileStatus = "";
    private string _friendSearch = "";
    private readonly HashSet<string> _friendInboxSeen = new(StringComparer.Ordinal);
    private ProfileDecoration _selectedFrame = ProfileFrames[0];
    private ProfileDecoration _selectedNameEffect = NameEffects[0];

    private static readonly ProfileDecoration[] ProfileFrames =
    {
        new("", "No frame"), new("plus_book", "Reader's Ring"), new("plus_laurel", "Ember Laurel"),
        new("plus_star", "Emberpoint"), new("plus_wings", "Gilded Wings"), new("plus_ring", "Ring of Flame"),
        new("pro_darklaurel", "Cinder Laurel"), new("pro_gold", "Goldflame Crest"), new("pro_obsidian", "Obsidian Circle"),
        new("pro_crown", "Flame Crown"), new("pro_sigils", "Embermark Sigils"), new("max_phoenix", "Phoenix Ascendant"),
        new("max_dragons", "Twin Wyrms"), new("max_sun", "Solar Crown"), new("max_void", "Voidflame Wings"),
        new("max_gate", "Infernal Gate")
    };

    private static readonly ProfileDecoration[] NameEffects =
    {
        new("", "None"), new("mark", "Diamond mark"), new("bold", "Heavy capitals"),
        new("under", "Underline"), new("rule", "Message rule"), new("both", "Diamond + rule"),
        new("star", "Star mark"), new("box", "Name box"), new("serif", "Large serif"),
        new("crown", "Crown mark"), new("chip", "High contrast tag"), new("frame", "Message frame")
    };

    public sealed class ProfileDecoration
    {
        public string Id { get; }
        public string Name { get; }
        public ProfileDecoration(string id, string name) { Id = id; Name = name; }
        public override string ToString() => Name;
    }

    public ObservableCollection<Message> Posts { get; } = new();
    public Message? ReportingPost { get => _reportingPost; private set { if (SetProperty(ref _reportingPost, value)) OnPropertyChanged(nameof(IsReportingPost)); } }
    public bool IsReportingPost => ReportingPost != null;
    public CommunityMember? SelectedMember { get => _selectedMember; private set { if (SetProperty(ref _selectedMember, value)) OnPropertyChanged(nameof(HasSelectedMember)); } }
    public bool HasSelectedMember => SelectedMember != null;
    public string PostReportNote { get => _postReportNote; set => SetProperty(ref _postReportNote, value); }
    public ObservableCollection<Message> StarredMessages { get; } = new();
    public ObservableCollection<Message> ImportantMessages { get; } = new();
    public ObservableCollection<CommunityNote> Notes { get; } = new();
    public ObservableCollection<SavedFlipbook> SavedFlipbooks { get; } = new();
    public ObservableCollection<CommunityMember> Members { get; } = new();
    public ObservableCollection<FriendUser> FriendResults { get; } = new();
    public ObservableCollection<FriendEntry> Friends { get; } = new();
    public ObservableCollection<FriendEntry> FriendRequests { get; } = new();
    public ObservableCollection<CloudSave> CloudSaves { get; } = new();
    public string[] Sections { get; } = { "Posts", "People", "Memes", "Cloud saves", "Stars & notes", "Flipbooks", "My profile", "Topics" };
    public MemeSceneOption[] MemeSceneOptions { get; } = MemeSceneOption.All;
    public ObservableCollection<OutsideMemeTemplate> OutsideMemeTemplates { get; } = new();
    public OutsideMemeTemplate? SelectedOutsideMemeTemplate
    {
        get => _selectedOutsideMemeTemplate;
        set => SetProperty(ref _selectedOutsideMemeTemplate, value);
    }
    public ObservableCollection<MemeCaptionSlot> MemeCaptions { get; } = new();
    public MemeCaptionSlot? SelectedMemeCaption
    {
        get => _selectedMemeCaption;
        set => SetProperty(ref _selectedMemeCaption, value);
    }
    public Bitmap? MemePicture => _memePicture;
    public bool HasMemePicture => _memePicture is not null;
    public ProfileDecoration[] FrameOptions => ProfileFrames;
    public ProfileDecoration[] NameEffectOptions => NameEffects;
    public string Section
    {
        get => _section;
        set
        {
            if (!SetProperty(ref _section, value)) return;
            OnPropertyChanged(nameof(IsPostsSection)); OnPropertyChanged(nameof(IsStarsNotesSection));
            OnPropertyChanged(nameof(IsFlipbooksSection)); OnPropertyChanged(nameof(IsProfileSection)); OnPropertyChanged(nameof(IsTopicsSection));
            OnPropertyChanged(nameof(IsPeopleSection)); OnPropertyChanged(nameof(IsFriendsSection));
            OnPropertyChanged(nameof(IsMemesSection)); OnPropertyChanged(nameof(IsCloudSavesSection));
            if (IsFriendsSection) _ = RefreshFriendsSectionAsync();
        }
    }
    public bool IsPostsSection => Section == "Posts";
    public bool IsPeopleSection => Section == "People";
    public bool IsFriendsSection => Section == "Friends";
    public string FriendSearch { get => _friendSearch; set => SetProperty(ref _friendSearch, value); }
    public bool IsMemesSection => Section == "Memes";
    public bool IsCloudSavesSection => Section == "Cloud saves";
    public bool IsStarsNotesSection => Section == "Stars & notes";
    public bool IsFlipbooksSection => Section == "Flipbooks";
    public bool IsProfileSection => Section == "My profile";
    public bool IsTopicsSection => Section == "Topics";
    public string PostText { get => _postText; set { if (SetProperty(ref _postText, value)) PostCommand.RaiseCanExecuteChanged(); } }
    public string CloudAppId { get => _cloudAppId; set => SetProperty(ref _cloudAppId, value); }
    public string CloudRoom { get => _cloudRoom; set => SetProperty(ref _cloudRoom, value); }
    public MemeSceneOption SelectedMemeScene
    {
        get => _selectedMemeScene;
        set
        {
            if (!SetProperty(ref _selectedMemeScene, value ?? MemeSceneOptions[0])) return;
            ReplaceMemePicture(null);
            SetMemeCaptions(_selectedMemeScene.CaptionSlots);
        }
    }
    public bool SocialLoading { get => _socialLoading; set => SetProperty(ref _socialLoading, value); }
    public string ProfileName { get => _profileName; set => SetProperty(ref _profileName, value); }
    public string ProfilePronouns { get => _profilePronouns; set => SetProperty(ref _profilePronouns, value); }
    public string ProfileHobbies { get => _profileHobbies; set => SetProperty(ref _profileHobbies, value); }
    public string ProfileBio { get => _profileBio; set => SetProperty(ref _profileBio, value); }
    public string ProfileStatus { get => _profileStatus; set => SetProperty(ref _profileStatus, value); }
    public ProfileDecoration SelectedFrame { get => _selectedFrame; set => SetProperty(ref _selectedFrame, value ?? ProfileFrames[0]); }
    public ProfileDecoration SelectedNameEffect { get => _selectedNameEffect; set => SetProperty(ref _selectedNameEffect, value ?? NameEffects[0]); }

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
    public RelayCommand RefreshCommunityCommand { get; }
    public RelayCommand<string> SectionCommand { get; }
    public RelayCommand PostCommand { get; }
    public RelayCommand<Message> TogglePostCommentsCommand { get; }
    public RelayCommand<Message> CommentOnPostCommand { get; }
    public RelayCommand<Message> UpvotePostCommand { get; }
    public RelayCommand<Message> ReportPostCommand { get; }
    public RelayCommand<Message> EditPostCommand { get; }
    public RelayCommand<Message> SavePostEditCommand { get; }
    public RelayCommand<Message> CancelPostEditCommand { get; }
    public RelayCommand<Message> DeletePostCommand { get; }
    public RelayCommand SubmitPostReportCommand { get; }
    public RelayCommand CancelPostReportCommand { get; }
    public RelayCommand SaveProfileCommand { get; }
    public RelayCommand EditAvatarCommand { get; }
    public RelayCommand CreateFlipbookCommand { get; }
    public RelayCommand<SavedFlipbook> PostFlipbookCommand { get; }
    public RelayCommand LoadCloudSavesCommand { get; }
    public RelayCommand SearchFriendsCommand { get; }
    public RelayCommand<FriendUser> AddFriendCommand { get; }
    public RelayCommand<FriendEntry> AcceptFriendCommand { get; }
    public RelayCommand<FriendEntry> DeclineFriendCommand { get; }
    public RelayCommand<FriendEntry> RemoveFriendCommand { get; }
    public RelayCommand<FriendEntry> MessageFriendCommand { get; }
    public RelayCommand<CommunityMember> ViewMemberProfileCommand { get; }
    public RelayCommand CloseMemberProfileCommand { get; }
    public RelayCommand<CommunityMember> MessageMemberCommand { get; }
    public RelayCommand<CommunityMember> AddMemberFriendCommand { get; }
    public event Action? OpenFlipbookEditorRequested;

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
        RefreshCommunityCommand = new RelayCommand(async () => await RefreshCommunityAsync());
        SectionCommand = new RelayCommand<string>(section => Section = section ?? "Posts");
        PostCommand = new RelayCommand(async () => await CreatePostAsync(), () => !string.IsNullOrWhiteSpace(PostText));
        TogglePostCommentsCommand = new RelayCommand<Message>(post => { if (post != null) post.CommunityCommentsExpanded = !post.CommunityCommentsExpanded; });
        CommentOnPostCommand = new RelayCommand<Message>(async post => await CommentOnPostAsync(post), post => post != null);
        UpvotePostCommand = new RelayCommand<Message>(async post => await UpvotePostAsync(post), post => post != null);
        ReportPostCommand = new RelayCommand<Message>(post => { if (post != null) { ReportingPost = post; PostReportNote = ""; } });
        EditPostCommand = new RelayCommand<Message>(post =>
        {
            if (post == null || !post.IsMine || string.IsNullOrEmpty(post.OwnerSecret)) return;
            post.CommunityPostEditDraft = post.Text ?? "";
            post.IsCommunityPostEditing = true;
        });
        SavePostEditCommand = new RelayCommand<Message>(async post => await SavePostEditAsync(post));
        CancelPostEditCommand = new RelayCommand<Message>(post => { if (post != null) post.IsCommunityPostEditing = false; });
        DeletePostCommand = new RelayCommand<Message>(async post => await DeletePostAsync(post));
        SubmitPostReportCommand = new RelayCommand(async () => await SubmitPostReportAsync(), () => ReportingPost != null && !string.IsNullOrWhiteSpace(PostReportNote));
        CancelPostReportCommand = new RelayCommand(() => { ReportingPost = null; PostReportNote = ""; });
        SaveProfileCommand = new RelayCommand(async () => await SaveProfileAsync());
        EditAvatarCommand = new RelayCommand(() => _nav.NavigateTo("Settings"));
        CreateFlipbookCommand = new RelayCommand(() => OpenFlipbookEditorRequested?.Invoke());
        PostFlipbookCommand = new RelayCommand<SavedFlipbook>(async book => await PostFlipbookAsync(book), book => book != null);
        LoadCloudSavesCommand = new RelayCommand(async () => await LoadCloudSavesAsync());
        SearchFriendsCommand = new RelayCommand(async () => await SearchFriendsAsync());
        AddFriendCommand = new RelayCommand<FriendUser>(async user => { if (user != null) await SendFriendRequestAsync(user); }, user => user != null);
        AcceptFriendCommand = new RelayCommand<FriendEntry>(async friend => { if (friend != null) await ResolveFriendRequestAsync(friend, true); }, friend => friend != null);
        DeclineFriendCommand = new RelayCommand<FriendEntry>(async friend => { if (friend != null) await ResolveFriendRequestAsync(friend, false); }, friend => friend != null);
        RemoveFriendCommand = new RelayCommand<FriendEntry>(async friend => { if (friend != null) await RemoveFriendAsync(friend); }, friend => friend != null);
        MessageFriendCommand = new RelayCommand<FriendEntry>(async friend => { if (friend != null) await MessageFriendAsync(friend); }, friend => friend != null);
        ViewMemberProfileCommand = new RelayCommand<CommunityMember>(member => SelectedMember = member);
        CloseMemberProfileCommand = new RelayCommand(() => SelectedMember = null);
        MessageMemberCommand = new RelayCommand<CommunityMember>(async member => { if (member != null) await MessageMemberAsync(member); });
        AddMemberFriendCommand = new RelayCommand<CommunityMember>(async member => { if (member != null) await SendFriendRequestAsync(new FriendUser { Hash = member.UserId, Name = member.DisplayName }); });

        SelectedMemeScene = MemeSceneOptions[0];
        LoadProfile();
        _ = RefreshCommunityAsync();
    }

    private JsonObject FriendState()
    {
        try { return JsonNode.Parse(_core.AccountStateJson ?? "{}") as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }
    private void LoadFriends()
    {
        var state = FriendState();
        Friends.Clear(); FriendRequests.Clear();
        foreach (var node in SafeArray(state["friends"]))
            if (node is JsonObject obj) { var hash = ReadFriendValue(obj, "hash", "uid", "userId"); if (!string.IsNullOrWhiteSpace(hash)) Friends.Add(new FriendEntry(hash, ReadFriendValue(obj, "name", "displayName") is { Length: > 0 } name ? name : "Friend", ReadFriendValue(obj, "uid", "userId"), ReadFriendValue(obj, "mid", "messageId"))); }
        foreach (var node in SafeArray(state["friendRequests"]))
            if (node is JsonObject obj) { var hash = ReadFriendValue(obj, "hash", "uid", "userId"); if (!string.IsNullOrWhiteSpace(hash)) FriendRequests.Add(new FriendEntry(hash, ReadFriendValue(obj, "name", "displayName") is { Length: > 0 } name ? name : "Someone", ReadFriendValue(obj, "uid", "userId"), ReadFriendValue(obj, "mid", "messageId"))); }
    }
    private static JsonArray SafeArray(JsonNode? node) => node is JsonArray array ? array : new JsonArray();
    private static string ReadFriendValue(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys) if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)) return text;
        return "";
    }
    private async Task SaveFriendsAsync(JsonArray friends, JsonArray requests, JsonArray? tombstones = null)
    {
        var state = FriendState(); state["friends"] = friends.DeepClone(); state["friendRequests"] = requests.DeepClone();
        if (tombstones != null) state["friendReqTombs"] = tombstones.DeepClone();
        _core.SetAccountState(state.ToJsonString()); await _core.SyncAccountAsync(_core.AccountStateJson, CancellationToken.None); LoadFriends();
    }
    private async Task SearchFriendsAsync()
    {
        if (!_core.IsAuthenticated) { StatusText = "Sign in to find friends."; return; }
        try { var results = await _core.SearchFriendUsersAsync(FriendSearch, CancellationToken.None); FriendResults.Clear(); foreach (var result in results) FriendResults.Add(result); StatusText = $"Found {results.Count} account(s)."; }
        catch { StatusText = "Couldn't search for accounts."; }
    }
    private async Task SendFriendRequestAsync(FriendUser user)
    {
        await _core.SendFriendInboxEventAsync(user.Hash, "FRIEND_REQUEST", CancellationToken.None);
        StatusText = $"Friend request sent to {user.Name}.";
    }
    private async Task ResolveFriendRequestAsync(FriendEntry req, bool accept)
    {
        var state = FriendState(); var friends = state["friends"] as JsonArray ?? new JsonArray(); var requests = state["friendRequests"] as JsonArray ?? new JsonArray();
        var match = requests.FirstOrDefault(n => string.Equals((string?)n?["hash"], req.Hash, StringComparison.OrdinalIgnoreCase)); if (match == null) return;
        requests.Remove(match);
        var tombs = state["friendReqTombs"] as JsonArray ?? new JsonArray(); if (!string.IsNullOrEmpty(req.MessageId) && !tombs.Any(n => string.Equals((string?)n, req.MessageId, StringComparison.Ordinal))) tombs.Add(req.MessageId);
        if (accept)
        {
            if (!friends.Any(n => string.Equals((string?)n?["hash"], req.Hash, StringComparison.OrdinalIgnoreCase))) friends.Add(new JsonObject { ["hash"] = req.Hash, ["uid"] = req.UserId, ["name"] = req.Name, ["since"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ["mid"] = req.MessageId });
            await _core.SendFriendInboxEventAsync(req.Hash, "FRIEND_ACCEPT", CancellationToken.None);
        }
        await SaveFriendsAsync(friends, requests, tombs);
    }
    private async Task RemoveFriendAsync(FriendEntry entry)
    {
        var state = FriendState(); var friends = state["friends"] as JsonArray ?? new JsonArray(); var requests = state["friendRequests"] as JsonArray ?? new JsonArray();
        foreach (var node in friends.Where(n => string.Equals((string?)n?["hash"], entry.Hash, StringComparison.OrdinalIgnoreCase)).ToArray()) friends.Remove(node);
        var tombs = state["friendReqTombs"] as JsonArray ?? new JsonArray();
        if (!string.IsNullOrEmpty(entry.MessageId) && !tombs.Any(n => string.Equals((string?)n, entry.MessageId, StringComparison.Ordinal))) tombs.Add(entry.MessageId);
        await SaveFriendsAsync(friends, requests, tombs);
    }

    private async Task MessageFriendAsync(FriendEntry entry)
    {
        if (!_core.IsAuthenticated) { StatusText = "Sign in to message friends."; return; }
        var targetId = string.IsNullOrWhiteSpace(entry.UserId) ? entry.Hash : entry.UserId;
        try
        {
            var expectedA = ("DM: " + (_core.CurrentProfile?.DisplayName ?? "Reader") + " & " + entry.Name).Trim();
            var expectedB = ("DM: " + entry.Name + " & " + (_core.CurrentProfile?.DisplayName ?? "Reader")).Trim();
            var existing = _core.GetOpenedMessageRooms().FirstOrDefault(room =>
                string.Equals(room.Name, expectedA, StringComparison.OrdinalIgnoreCase)
                || string.Equals(room.Name, expectedB, StringComparison.OrdinalIgnoreCase));
            var group = await _core.OpenDmAsync(targetId, entry.Name, existing?.Code ?? "", CancellationToken.None);
            ChatPrefsStore.Current.RememberDmRoom(targetId, group.Code);
            RoomRegistry.Open(group);
            _nav.NavigateTo("Messages");
        }
        catch (Exception ex) { _logger.LogInformation(ex, "Open friend chat failed"); StatusText = "Couldn't open that chat."; }
    }

    private async Task MessageMemberAsync(CommunityMember member)
    {
        if (!_core.IsAuthenticated || string.IsNullOrWhiteSpace(member.UserId)) { StatusText = "Sign in to message this member."; return; }
        try
        {
            var known = ChatPrefsStore.Current.DmRoomFor(member.UserId);
            var group = await _core.OpenDmAsync(member.UserId, member.DisplayName, known ?? "", CancellationToken.None);
            ChatPrefsStore.Current.RememberDmRoom(member.UserId, group.Code);
            RoomRegistry.Open(group);
            _nav.NavigateTo("Messages");
        }
        catch (Exception ex) { _logger.LogInformation(ex, "Open member chat failed"); StatusText = "Couldn't open that chat."; }
    }
    private async Task PollFriendInboxAsync()
    {
        if (!_core.IsAuthenticated) return;
        List<RoomMessageEnvelope> events;
        try { events = await _core.PollGameEventsAsync(_core.MyInboxRoomCode, CancellationToken.None); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Friend inbox refresh failed");
            StatusText = "Couldn't refresh friend requests right now. Your saved friends are still available.";
            LoadFriends();
            return;
        }
        var state = FriendState(); var friends = state["friends"] as JsonArray ?? new JsonArray(); var requests = state["friendRequests"] as JsonArray ?? new JsonArray(); var tombs = state["friendReqTombs"] as JsonArray ?? new JsonArray();
        var changed = false;
        foreach (var evt in events.Where(e => e.Type == "FRIEND_REQUEST" || e.Type == "FRIEND_ACCEPT"))
        {
            var id = evt.Message?.Id ?? ""; if (!_friendInboxSeen.Add(id) || tombs.Any(n => string.Equals((string?)n, id, StringComparison.Ordinal))) continue;
            var hash = evt.Str("fromHash"); if (hash.Length < 6 || hash.Length > 16 || !hash.All(Uri.IsHexDigit)) continue;
            var name = evt.Str("fromName"); var uid = evt.Str("fromUserId");
            if (evt.Type == "FRIEND_REQUEST")
            {
                if (!friends.Any(n => string.Equals((string?)n?["hash"], hash, StringComparison.OrdinalIgnoreCase)) && !requests.Any(n => string.Equals((string?)n?["hash"], hash, StringComparison.OrdinalIgnoreCase)))
                { requests.Add(new JsonObject { ["hash"] = hash, ["uid"] = uid, ["name"] = name, ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ["mid"] = id }); changed = true; }
            }
            else
            {
                if (!friends.Any(n => string.Equals((string?)n?["hash"], hash, StringComparison.OrdinalIgnoreCase))) friends.Add(new JsonObject { ["hash"] = hash, ["uid"] = uid, ["name"] = name, ["since"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ["mid"] = id });
                changed = true;
            }
        }
        if (changed) await SaveFriendsAsync(friends, requests, tombs); else LoadFriends();
    }

    private async Task RefreshFriendsSectionAsync()
    {
        if (!_core.IsAuthenticated) { LoadFriends(); return; }
        try
        {
            var cloudState = await _core.FetchAccountStateAsync(CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(cloudState)) _core.SetAccountState(cloudState);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Could not refresh the synced friends list"); }
        LoadFriends();
        await PollFriendInboxAsync();
    }
    public sealed class FriendEntry
    {
        public string Hash { get; } public string Name { get; } public string UserId { get; } public string MessageId { get; }
        public FriendEntry(string hash, string name, string userId, string messageId = "") { Hash = hash; Name = name; UserId = userId; MessageId = messageId; }
    }

    private async Task LoadCloudSavesAsync()
    {
        if (!_core.IsAuthenticated) { StatusText = "Sign in to view cloud saves."; return; }
        if (string.IsNullOrWhiteSpace(CloudAppId)) { StatusText = "Enter the app ID used by its cloud saves."; return; }
        try
        {
            var saves = await _core.FetchCloudSavesAsync(CloudAppId, CloudRoom, 200, CancellationToken.None);
            CloudSaves.Clear();
            foreach (var save in saves) CloudSaves.Add(save);
            StatusText = $"Loaded {saves.Count} cloud save row(s) for {CloudAppId.Trim()} / {CloudRoom.Trim()}.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloud save list failed for {AppId}/{Room}", CloudAppId, CloudRoom);
            StatusText = "Couldn't load those cloud saves. Check the app ID, room, and connection.";
        }
    }

    public void UseMemePicture(Bitmap picture)
    {
        ReplaceMemePicture(picture);
        SetMemeCaptions(new[]
        {
            new MemeCaptionDefinition("Top line", 15, 8, 450, 110, "top"),
            new MemeCaptionDefinition("Bottom line", 15, 362, 450, 110, "bottom")
        });
    }

    public async Task LoadOutsideMemeTemplatesAsync()
    {
        StatusText = "Loading outside meme templates…";
        try
        {
            using var response = await MemeTemplateHttp.GetAsync("https://api.imgflip.com/get_memes", CancellationToken.None);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;
            if (!root.TryGetProperty("success", out var success) || !success.GetBoolean() ||
                !root.TryGetProperty("data", out var data) || !data.TryGetProperty("memes", out var memes))
                throw new InvalidDataException("Imgflip returned an unexpected template list.");

            var templates = new List<OutsideMemeTemplate>();
            foreach (var meme in memes.EnumerateArray().Take(100))
            {
                var id = meme.TryGetProperty("id", out var idNode) ? idNode.ToString() : "";
                var name = meme.TryGetProperty("name", out var nameNode) ? nameNode.GetString() : null;
                var urlText = meme.TryGetProperty("url", out var urlNode) ? urlNode.GetString() : null;
                if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                    string.IsNullOrWhiteSpace(name) || !Uri.TryCreate(urlText, UriKind.Absolute, out var url) ||
                    url.Scheme != Uri.UriSchemeHttps || !string.Equals(url.Host, "i.imgflip.com", StringComparison.OrdinalIgnoreCase))
                    continue;
                var boxCount = meme.TryGetProperty("box_count", out var boxes) && boxes.TryGetInt32(out var count) ? count : 2;
                templates.Add(new OutsideMemeTemplate(id, name, url, Math.Clamp(boxCount, 1, 8)));
            }

            OutsideMemeTemplates.Clear();
            foreach (var template in templates) OutsideMemeTemplates.Add(template);
            SelectedOutsideMemeTemplate = OutsideMemeTemplates.FirstOrDefault();
            StatusText = templates.Count == 0 ? "No outside templates were returned." : $"Loaded {templates.Count} outside templates from Imgflip.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load outside meme templates");
            StatusText = "Couldn't load outside templates. Check your internet connection and try again.";
        }
    }

    public async Task UseSelectedOutsideMemeTemplateAsync()
    {
        var template = SelectedOutsideMemeTemplate;
        if (template is null) { StatusText = "Load and select an outside template first."; return; }
        try
        {
            using var response = await MemeTemplateHttp.GetAsync(template.ImageUrl, CancellationToken.None);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 10_000_000)
                throw new InvalidDataException("The selected template image is too large.");
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0 || bytes.Length > 10_000_000)
                throw new InvalidDataException("The selected template image is empty or too large.");
            using var stream = new System.IO.MemoryStream(bytes);
            ReplaceMemePicture(new Bitmap(stream));

            var count = template.BoxCount;
            var slotHeight = 460d / count;
            var definitions = Enumerable.Range(0, count).Select(index => new MemeCaptionDefinition(
                $"Caption {index + 1}", 15, 10 + index * slotHeight, 450, Math.Max(50, slotHeight),
                index == 0 ? "top" : index == count - 1 ? "bottom" : "middle"));
            SetMemeCaptions(definitions);
            StatusText = $"Loaded outside template: {template.Name}. Adjust each caption's X/Y and alignment.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load outside meme template {TemplateId}", template.Id);
            StatusText = "Couldn't download that template image.";
        }
    }

    public sealed class OutsideMemeTemplate
    {
        public string Id { get; }
        public string Name { get; }
        public Uri ImageUrl { get; }
        public int BoxCount { get; }
        public OutsideMemeTemplate(string id, string name, Uri imageUrl, int boxCount)
        { Id = id; Name = name; ImageUrl = imageUrl; BoxCount = boxCount; }
        public override string ToString() => Name;
    }

    public void ClearMemePicture()
    {
        ReplaceMemePicture(null);
        SetMemeCaptions(SelectedMemeScene.CaptionSlots);
    }

    private void ReplaceMemePicture(Bitmap? picture)
    {
        if (ReferenceEquals(_memePicture, picture)) return;
        _memePicture?.Dispose();
        _memePicture = picture;
        OnPropertyChanged(nameof(MemePicture));
        OnPropertyChanged(nameof(HasMemePicture));
    }

    private void SetMemeCaptions(IEnumerable<MemeCaptionDefinition> definitions)
    {
        SelectedMemeCaption = null;
        foreach (var caption in MemeCaptions) caption.PropertyChanged -= MemeCaptionChanged;
        MemeCaptions.Clear();
        foreach (var definition in definitions)
        {
            var caption = new MemeCaptionSlot(definition);
            caption.PropertyChanged += MemeCaptionChanged;
            MemeCaptions.Add(caption);
        }
        OnPropertyChanged(nameof(MemeCaptions));
    }

    private void MemeCaptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => OnPropertyChanged(nameof(MemeCaptions));

    public sealed class MemeCaptionSlot : ViewModelBase
    {
        private string _text = "";
        private string _leftText;
        private string _topText;
        private string _at;
        private double _left;
        private double _top;
        private double _width;
        private double _height;
        private double _fontSize = 48;
        public MemeCaptionDefinition Definition { get; }
        public string Label => Definition.Label;
        public string Text { get => _text; set => SetProperty(ref _text, value); }
        public double Left => _left;
        public double Top => _top;
        public double Width => _width;
        public double Height => _height;
        public double FontSize
        {
            get => _fontSize;
            set
            {
                var clamped = Math.Clamp(value, 8, 100);
                if (Math.Abs(_fontSize - clamped) < 0.01) return;
                _fontSize = clamped;
                OnPropertyChanged(nameof(FontSize));
            }
        }
        public string[] AnchorOptions { get; } = { "top", "middle", "bottom" };
        public string LeftText
        {
            get => _leftText;
            set
            {
                if (!SetProperty(ref _leftText, value)) return;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    _left = Math.Clamp(parsed, 0, Math.Max(0, 480 - _width));
                    OnPropertyChanged(nameof(Left));
                }
            }
        }
        public string TopText
        {
            get => _topText;
            set
            {
                if (!SetProperty(ref _topText, value)) return;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    _top = Math.Clamp(parsed, 0, Math.Max(0, 480 - _height));
                    OnPropertyChanged(nameof(Top));
                }
            }
        }
        public string At
        {
            get => _at;
            set { if (AnchorOptions.Contains(value, StringComparer.Ordinal)) SetProperty(ref _at, value); }
        }
        public MemeCaptionSlot(MemeCaptionDefinition definition)
        {
            Definition = definition;
            _left = definition.X;
            _top = definition.Y;
            _width = definition.Width;
            _height = definition.Height;
            _leftText = definition.X.ToString("0", CultureInfo.InvariantCulture);
            _topText = definition.Y.ToString("0", CultureInfo.InvariantCulture);
            _at = definition.At;
        }

        public void MoveTo(double left, double top)
        {
            _left = Math.Clamp(left, 0, Math.Max(0, 480 - _width));
            _top = Math.Clamp(top, 0, Math.Max(0, 480 - _height));
            _leftText = _left.ToString("0", CultureInfo.InvariantCulture);
            _topText = _top.ToString("0", CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(Left));
            OnPropertyChanged(nameof(Top));
            OnPropertyChanged(nameof(LeftText));
            OnPropertyChanged(nameof(TopText));
        }

        public void ResizeTo(double width, double height)
        {
            _width = Math.Clamp(width, 60, Math.Max(60, 480 - _left));
            _height = Math.Clamp(height, 40, Math.Max(40, 480 - _top));
            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Height));
        }
    }

    public sealed record MemeCaptionDefinition(string Label, double X, double Y, double Width, double Height, string At);

    public sealed class MemeSceneOption
    {
        public static readonly MemeSceneOption[] All =
        {
            new("blank", "Nothing", new("Top line", 15, 8, 450, 110, "top"), new("Bottom line", 15, 362, 450, 110, "bottom")),
            new("two", "Two doors", new("Top line", 15, 8, 450, 110, "top"), new("Bottom line", 15, 362, 450, 110, "bottom")),
            new("brain", "Big brain", new("Top line", 15, 8, 450, 110, "top"), new("Bottom line", 15, 362, 450, 110, "bottom")),
            new("fine", "This is fine", new("Top line", 15, 8, 450, 110, "top"), new("Bottom line", 15, 362, 450, 110, "bottom")),
            new("point", "Pointing", new("Top line", 15, 8, 450, 110, "top"), new("Bottom line", 15, 362, 450, 110, "bottom")),
            new("drake", "No / yes", new("The no", 230, 20, 230, 200, "middle"), new("The yes", 230, 260, 230, 200, "middle")),
            new("brain4", "Four brains", new("Smallest idea", 12, 6, 210, 110, "middle"), new("Bigger", 12, 126, 210, 110, "middle"), new("Bigger still", 12, 246, 210, 110, "middle"), new("Galaxy brain", 12, 366, 210, 106, "middle")),
            new("two_btn", "Two buttons", new("Left button", 40, 96, 170, 80, "middle"), new("Right button", 262, 96, 170, 80, "middle"), new("Who is choosing", 15, 392, 450, 80, "bottom")),
            new("panik", "Panik / kalm", new("Panik", 12, 8, 250, 140, "middle"), new("Kalm", 12, 170, 250, 140, "middle"), new("PANIK", 12, 332, 250, 140, "middle")),
            new("sign", "Change my mind", new("What the sign says", 60, 250, 360, 110, "middle"), new("A line above, if you want one", 15, 8, 450, 90, "top"))
        };

        public string Id { get; }
        public string Name { get; }
        public MemeCaptionDefinition[] CaptionSlots { get; }
        public MemeSceneOption(string id, string name, params MemeCaptionDefinition[] captionSlots)
        { Id = id; Name = name; CaptionSlots = captionSlots; }
        public override string ToString() => Name;
    }

    public async Task RefreshCommunityAsync()
    {
        SocialLoading = true;
        try
        {
            if (_core.IsAuthenticated)
            {
                try
                {
                    var cloudState = await _core.FetchAccountStateAsync(CancellationToken.None);
                    if (!string.IsNullOrWhiteSpace(cloudState)) _core.SetAccountState(cloudState);
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Could not refresh synced social account state"); }
            }
            LoadFriends();
            await Task.WhenAll(RefreshAsync(), RefreshPostsAsync(), RefreshSavedAsync(), RefreshPeopleAsync(), PollFriendInboxAsync());
            RefreshFlipbooks();
        }
        finally { SocialLoading = false; }
    }

    private async Task RefreshPeopleAsync()
    {
        try
        {
            var presence = await _core.FetchPresenceAsync(1440, 100, CancellationToken.None);
            var frameIds = presence.Select(entry => ReadProfileField(entry.Profile, "fr"))
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var frameTasks = frameIds.Select(async id => (Id: id, Image: await ProfileFrameImageLoader.LoadAsync(id))).ToArray();
            var loadedFrames = await Task.WhenAll(frameTasks);
            var frameImages = loadedFrames.ToDictionary(item => item.Id, item => item.Image, StringComparer.OrdinalIgnoreCase);
            var members = presence.Select(entry =>
            {
                var frame = ReadProfileField(entry.Profile, "fr");
                return new CommunityMember(entry, frameImages.GetValueOrDefault(frame));
            }).ToList();
            Members.Clear();
            foreach (var member in members) Members.Add(member);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Community member list refresh failed");
            StatusText = "Couldn't load community profiles.";
        }
    }

    private static string ReadProfileField(string? profile, string key)
    {
        try
        {
            using var document = JsonDocument.Parse(profile ?? "{}");
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                using var nested = JsonDocument.Parse(root.GetString() ?? "{}");
                root = nested.RootElement.Clone();
            }
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "" : "";
        }
        catch { return ""; }
    }

    public sealed class CommunityMember
    {
        private static readonly IReadOnlyDictionary<string, string> Frames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["plus_book"] = "Reader's Ring", ["plus_laurel"] = "Ember Laurel", ["plus_star"] = "Emberpoint",
            ["plus_wings"] = "Gilded Wings", ["plus_ring"] = "Ring of Flame", ["pro_darklaurel"] = "Cinder Laurel",
            ["pro_gold"] = "Goldflame Crest", ["pro_obsidian"] = "Obsidian Circle", ["pro_crown"] = "Flame Crown",
            ["pro_sigils"] = "Embermark Sigils", ["max_phoenix"] = "Phoenix Ascendant", ["max_dragons"] = "Twin Wyrms",
            ["max_sun"] = "Solar Crown", ["max_void"] = "Voidflame Wings", ["max_gate"] = "Infernal Gate"
        };
        private static readonly IReadOnlyDictionary<string, string> Styles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mark"] = "Diamond mark", ["bold"] = "Heavy capitals", ["under"] = "Underline",
            ["rule"] = "Message rule", ["both"] = "Diamond + rule", ["star"] = "Star mark",
            ["box"] = "Name box", ["serif"] = "Large serif", ["crown"] = "Crown mark",
            ["chip"] = "High contrast tag", ["frame"] = "Message frame"
        };

        public string DisplayName { get; }
        public string Initial => string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Trim()[0].ToString().ToUpperInvariant();
        public string UserId { get; }
        public string Avatar { get; }
        public object? ProfileFrameImage { get; }
        public string ProfileFrameId { get; }
        public string ProfileFrame { get; }
        public bool HasProfileFrame => !string.IsNullOrEmpty(ProfileFrameId);
        public string Role { get; }
        public string NameStyle { get; }
        public string LastSeen { get; }
        public bool HasNameStyle => !string.IsNullOrEmpty(NameStyle);
        public string Pronouns { get; } = "";
        public string Hobbies { get; } = "";
        public string Bio { get; } = "";
        public string Status { get; } = "";
        public bool HasPronouns => !string.IsNullOrWhiteSpace(Pronouns);
        public bool HasHobbies => !string.IsNullOrWhiteSpace(Hobbies);
        public bool HasBio => !string.IsNullOrWhiteSpace(Bio);
        public bool HasStatus => !string.IsNullOrWhiteSpace(Status);

        public CommunityMember(PresenceEntry entry, object? frameImage)
        {
            DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? "Reader" : entry.DisplayName;
            UserId = entry.UserId ?? "";
            Avatar = entry.Avatar ?? "";
            ProfileFrameImage = frameImage;
            LastSeen = entry.AgeFormatted;
            string frame = "", role = "", plan = "", nameStyle = "";
            try
            {
                using var document = JsonDocument.Parse(entry.Profile ?? "{}");
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.String)
                {
                    using var nested = JsonDocument.Parse(root.GetString() ?? "{}");
                    root = nested.RootElement.Clone();
                }
                if (root.ValueKind == JsonValueKind.Object)
                {
                    frame = ReadText(root, "fr"); role = ReadText(root, "r");
                    plan = ReadText(root, "pl"); nameStyle = ReadText(root, "ns");
                    Pronouns = ReadText(root, "p"); Hobbies = ReadText(root, "h");
                    Bio = ReadText(root, "b"); Status = ReadText(root, "s");
                }
            }
            catch { }
            ProfileFrameId = frame;
            ProfileFrame = Frames.TryGetValue(frame, out var label) ? label : string.IsNullOrEmpty(frame) ? "No frame" : frame;
            NameStyle = string.IsNullOrEmpty(nameStyle) ? "" : $"Name effect: {(Styles.TryGetValue(nameStyle, out var styleLabel) ? styleLabel : nameStyle)}";
            Role = (role switch { "creator" => "Creator", "ultra" => "Ultra", "mod" => "Moderator", _ => "" });
            var planLabel = plan switch { "plus" => "Plus", "pro" => "Pro", "max" => "Max", _ => "" };
            if (!string.IsNullOrEmpty(planLabel)) Role = string.IsNullOrEmpty(Role) ? planLabel : $"{Role} · {planLabel}";
            if (string.IsNullOrEmpty(Role)) Role = "Member";
        }

        private static string ReadText(JsonElement root, string key)
            => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }

    private async Task RefreshPostsAsync()
    {
        var found = new List<Message>();
        for (var day = 0; day < 7; day++)
        {
            var roomCode = NeighbourhoodCode(day);
            try
            {
                found.AddRange(await _core.FetchMessagesAsync(roomCode, 100, 0, CancellationToken.None));
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Neighbourhood day {Day} is unavailable", day); }
        }
        var posts = found.Where(m => string.IsNullOrEmpty(m.ReplyTo)).OrderByDescending(m => m.Timestamp).Take(100).ToList();
        var postIds = posts.Select(post => post.Id).ToHashSet(StringComparer.Ordinal);
        var comments = found.Where(m => !string.IsNullOrEmpty(m.ReplyTo) && postIds.Contains(m.ReplyTo))
            .GroupBy(m => m.ReplyTo!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.OrderBy(m => m.Timestamp).TakeLast(60).ToList(), StringComparer.Ordinal);
        foreach (var post in posts)
        {
            post.CommunityComments.Clear();
            if (comments.TryGetValue(post.Id, out var replies))
                foreach (var reply in replies) post.CommunityComments.Add(reply);
        }
        try
        {
            var authors = posts.Concat(posts.SelectMany(post => post.CommunityComments)).Select(m => m.UserId).Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var profiles = await _core.FetchPublicProfilesAsync(authors, CancellationToken.None);
            foreach (var post in posts.Concat(posts.SelectMany(post => post.CommunityComments)))
            {
                if (!profiles.TryGetValue(post.UserId ?? "", out var profile)) continue;
                post.AvatarCode = profile.Avatar ?? "";
                post.ProfileFrame = profile.ProfileFrame ?? "";
                post.NameStyle = profile.NameStyle ?? "";
                post.ProfileRole = profile.Role ?? "";
                post.ProfilePlan = profile.Plan ?? "";
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Community post profile lookup failed"); }
        var postFrameIds = posts.Concat(posts.SelectMany(post => post.CommunityComments)).Select(post => post.ProfileFrame).Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var postFrameTasks = postFrameIds.Select(async id => (Id: id, Image: await ProfileFrameImageLoader.LoadAsync(id))).ToArray();
        var postFrames = (await Task.WhenAll(postFrameTasks))
            .ToDictionary(item => item.Id, item => item.Image, StringComparer.OrdinalIgnoreCase);
        foreach (var post in posts.Concat(posts.SelectMany(post => post.CommunityComments)))
            post.ProfileFrameImage = postFrames.GetValueOrDefault(post.ProfileFrame ?? "");
        Posts.Clear();
        foreach (var post in posts) Posts.Add(post);
    }

    private async Task RefreshSavedAsync()
    {
        var starred = new List<Message>();
        var keys = ChatPrefsStore.Current.StarredKeys();
        foreach (var group in keys.Select(key => key.Split('|', 2)).Where(parts => parts.Length == 2)
                     .GroupBy(parts => parts[0], StringComparer.Ordinal))
        {
            try
            {
                var rows = await _core.FetchMessagesAsync(group.Key, 100, 0, CancellationToken.None);
                var wanted = group.Select(parts => parts[1]).ToHashSet(StringComparer.Ordinal);
                starred.AddRange(rows.Where(m => wanted.Contains(m.Id)));
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not load starred messages for {Group}", group.Key); }
        }
        StarredMessages.Clear();
        foreach (var item in starred.OrderByDescending(m => m.Timestamp)) { item.IsStarred = true; StarredMessages.Add(item); }

        Notes.Clear();
        foreach (var note in _core.GetCommunityNotes()) Notes.Add(note);

        var rooms = _core.GetOpenedMessageRooms().Concat(RoomRegistry.Rooms)
            .Append(new Group { Code = KindleHubCore.GlobalGroupCode, Name = "Global Chat" })
            .Where(g => g != null && !string.IsNullOrWhiteSpace(g.Code) && !g.Code.StartsWith("mp-", StringComparison.OrdinalIgnoreCase)
                        && !g.Code.StartsWith("800000", StringComparison.Ordinal))
            .GroupBy(g => g.Code, StringComparer.Ordinal).Select(g => g.First()).Take(24).ToList();
        var important = new List<Message>();
        foreach (var room in rooms)
        {
            try
            {
                var rows = await _core.FetchMessagesAsync(room.Code, 50, 0, CancellationToken.None);
                important.AddRange(rows.Where(m => m.Important));
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not inspect important messages in {Group}", room.Code); }
        }
        ImportantMessages.Clear();
        foreach (var item in important.DistinctBy(m => m.GroupCode + "|" + m.Id).OrderByDescending(m => m.Timestamp).Take(100))
            ImportantMessages.Add(item);
    }

    private void RefreshFlipbooks()
    {
        SavedFlipbooks.Clear();
        foreach (var book in _core.GetSavedFlipbooks()) SavedFlipbooks.Add(book);
    }

    private void LoadProfile()
    {
        try
        {
            using var state = JsonDocument.Parse(_core.AccountStateJson ?? "{}");
            var root = state.RootElement;
            ProfileName = GetText(root, "profileName", _core.CurrentProfile?.DisplayName ?? "");
            ProfilePronouns = GetText(root, "profilePronouns", "");
            ProfileHobbies = GetText(root, "profileHobbies", "");
            ProfileBio = GetText(root, "profileBio", "");
            ProfileStatus = GetText(root, "profileStatus", "");
            SelectedFrame = ProfileFrames.FirstOrDefault(x => x.Id == GetText(root, "profileFrame", "")) ?? ProfileFrames[0];
            SelectedNameEffect = NameEffects.FirstOrDefault(x => x.Id == GetText(root, "nameStyle", "")) ?? NameEffects[0];
        }
        catch { ProfileName = _core.CurrentProfile?.DisplayName ?? ""; }
    }

    private static string GetText(JsonElement root, string key, string fallback)
        => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    public async Task<bool> SaveFlipbookAsync(string wire)
    {
        var ok = await _core.SaveCommunityFlipbookAsync(wire, CancellationToken.None);
        if (ok) { RefreshFlipbooks(); StatusText = "Flipbook saved to your account."; }
        else StatusText = "Couldn't save the flipbook. Sign in and try again.";
        return ok;
    }

    private async Task CreatePostAsync()
    {
        var text = (PostText ?? "").Trim();
        if (text.Length == 0 || !TryAuthed()) return;
        if (text.Length > 800) { StatusText = "Posts are limited to 800 characters."; return; }
        try
        {
            var code = NeighbourhoodCode(0);
            await _core.JoinGroupByCodeAsync(code, CancellationToken.None);
            await _core.SendMessageAsync(code, text, false, null, CancellationToken.None);
            PostText = "";
            StatusText = "Post shared with the neighbourhood.";
            await RefreshPostsAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Neighbourhood post failed"); StatusText = "Couldn't publish the post."; }
    }

    private async Task CommentOnPostAsync(Message? post)
    {
        if (post == null || !TryAuthed()) return;
        var text = (post.CommunityCommentDraft ?? "").Trim();
        if (text.Length == 0) return;
        if (text.Length > 500) { StatusText = "Comments are limited to 500 characters."; return; }
        try
        {
            await _core.SendMessageAsync(post.GroupCode, text, false, post.Id, CancellationToken.None);
            post.CommunityCommentDraft = "";
            post.CommunityCommentsExpanded = true;
            StatusText = "Comment posted.";
            await RefreshPostsAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Neighbourhood comment failed"); StatusText = "Couldn't post the comment."; }
    }

    private async Task UpvotePostAsync(Message? post)
    {
        if (post == null || !TryAuthed()) return;
        try
        {
            if (await _core.ToggleReactionAsync(post.Id, "👍", CancellationToken.None))
            {
                StatusText = "Vote updated.";
                await RefreshPostsAsync();
            }
            else StatusText = "Couldn't update your vote.";
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Neighbourhood upvote failed"); StatusText = "Couldn't update your vote."; }
    }

    private async Task SubmitPostReportAsync()
    {
        var post = ReportingPost;
        var note = (PostReportNote ?? "").Trim();
        if (post == null || note.Length == 0 || !TryAuthed()) return;
        try
        {
            var reported = await _core.ReportMessageAsync("Neighbourhood post", note, post, "Neighbourhood", CancellationToken.None);
            StatusText = reported ? "Reported. A moderator will review it." : "That report could not be sent.";
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Neighbourhood post report failed"); StatusText = "That report could not be sent."; }
        ReportingPost = null;
        PostReportNote = "";
    }

    private async Task SavePostEditAsync(Message? post)
    {
        if (post == null || !post.IsMine || string.IsNullOrEmpty(post.OwnerSecret)) return;
        var text = (post.CommunityPostEditDraft ?? "").Trim();
        if (text.Length == 0 || text.Length > 800) { StatusText = "Posts must contain 1–800 characters."; return; }
        try
        {
            if (!await _core.EditMessageAsync(post.GroupCode, post.Id, post.OwnerSecret, text, CancellationToken.None))
            { StatusText = "Couldn't save the post edit."; return; }
            post.Text = text;
            post.Edited = true;
            post.IsCommunityPostEditing = false;
            StatusText = "Post updated.";
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Neighbourhood post edit failed"); StatusText = "Couldn't save the post edit."; }
    }

    private async Task DeletePostAsync(Message? post)
    {
        if (post == null || !post.IsMine || string.IsNullOrEmpty(post.OwnerSecret)) return;
        try
        {
            if (!await _core.UnsendMessageAsync(post.Id, post.OwnerSecret, CancellationToken.None))
            { StatusText = "Couldn't delete the post."; return; }
            Posts.Remove(post);
            StatusText = "Post deleted.";
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Neighbourhood post deletion failed"); StatusText = "Couldn't delete the post."; }
    }

    public async Task PostMemeAsync(byte[] jpegBytes)
    {
        if (!TryAuthed()) return;
        var dataUri = "data:image/jpeg;base64," + Convert.ToBase64String(jpegBytes);
        var wire = ChatMedia.EncodeImage(dataUri);
        if (wire is null)
        {
            StatusText = "This meme is too large to post. Try a smaller picture or simpler template.";
            return;
        }

        try
        {
            var code = NeighbourhoodCode(0);
            await _core.JoinGroupByCodeAsync(code, CancellationToken.None);
            await _core.SendMessageAsync(code, wire, false, null, CancellationToken.None);
            StatusText = "Meme posted to the neighbourhood.";
            await RefreshPostsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Neighbourhood meme post failed");
            StatusText = "Couldn't publish the meme.";
        }
    }

    private async Task PostFlipbookAsync(SavedFlipbook? book)
    {
        if (book == null || !TryAuthed()) return;
        try
        {
            var code = NeighbourhoodCode(0);
            await _core.JoinGroupByCodeAsync(code, CancellationToken.None);
            await _core.SendMessageAsync(code, book.Wire, false, null, CancellationToken.None);
            StatusText = "Flipbook posted for everyone.";
            await RefreshPostsAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Flipbook post failed"); StatusText = "Couldn't post that flipbook."; }
    }

    private async Task SaveProfileAsync()
    {
        if (!TryAuthed()) return;
        var state = _core.AccountStateJson ?? "{}";
        state = AccountState.WithText(state, "profileName", (ProfileName ?? "").Trim());
        state = AccountState.WithText(state, "profilePronouns", (ProfilePronouns ?? "").Trim());
        state = AccountState.WithText(state, "profileHobbies", (ProfileHobbies ?? "").Trim());
        state = AccountState.WithText(state, "profileBio", (ProfileBio ?? "").Trim());
        state = AccountState.WithText(state, "profileStatus", (ProfileStatus ?? "").Trim());
        state = AccountState.WithText(state, "profileFrame", SelectedFrame.Id);
        state = AccountState.WithText(state, "nameStyle", SelectedNameEffect.Id);
        _core.SetAccountState(state);
        if (_core.CurrentProfile != null && !string.IsNullOrWhiteSpace(ProfileName)) _core.CurrentProfile.DisplayName = ProfileName.Trim();
        var ok = await _core.SyncAccountAsync(state, CancellationToken.None);
        if (ok)
        {
            await _core.PingPresenceAsync("", CancellationToken.None);
            StatusText = "Profile saved and synced. Frames and name effects are all available.";
        }
        else StatusText = "Couldn't sync your profile changes.";
    }

    private static string NeighbourhoodCode(int daysAgo)
        => "7" + DateTime.Now.Date.AddDays(-daysAgo).ToString("yyyyMMdd") + "000";

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
        var enteredCode = (JoinCode ?? "").Trim();
        var codeDigits = new string(enteredCode.Where(char.IsDigit).ToArray());
        var playVirtualInsanity = string.Equals(enteredCode, string.Concat("virtual", "insanity"), StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(codeDigits, string.Concat("4817", "9141", "0929"), StringComparison.Ordinal);
        if (playVirtualInsanity)
        {
            SettingsViewModel.NoteFound(string.Concat("virtual", "insanity"));
            codeDigits = string.Concat("4817", "9141", "0929");
        }
        var digits = codeDigits;
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
            if (playVirtualInsanity)
            {
                try { Process.Start(new ProcessStartInfo(string.Concat("https://www.youtube.com/watch?v=", "4JkIs", "37a2JE")) { UseShellExecute = true }); } catch { }
            }
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
