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
    private string _cloudAppId = "stronghold";
    private string _cloudRoom = "world";
    private MemeSceneOption _selectedMemeScene = null!;
    private Bitmap? _memePicture;
    private OutsideMemeTemplate? _selectedOutsideMemeTemplate;
    private bool _socialLoading;
    private string _profileName = "";
    private string _profilePronouns = "";
    private string _profileHobbies = "";
    private string _profileBio = "";
    private string _profileStatus = "";
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
    public ObservableCollection<Message> StarredMessages { get; } = new();
    public ObservableCollection<Message> ImportantMessages { get; } = new();
    public ObservableCollection<CommunityNote> Notes { get; } = new();
    public ObservableCollection<SavedFlipbook> SavedFlipbooks { get; } = new();
    public ObservableCollection<CommunityMember> Members { get; } = new();
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
            OnPropertyChanged(nameof(IsPeopleSection));
            OnPropertyChanged(nameof(IsMemesSection)); OnPropertyChanged(nameof(IsCloudSavesSection));
        }
    }
    public bool IsPostsSection => Section == "Posts";
    public bool IsPeopleSection => Section == "People";
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
    public RelayCommand SaveProfileCommand { get; }
    public RelayCommand EditAvatarCommand { get; }
    public RelayCommand CreateFlipbookCommand { get; }
    public RelayCommand<SavedFlipbook> PostFlipbookCommand { get; }
    public RelayCommand LoadCloudSavesCommand { get; }
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
        SaveProfileCommand = new RelayCommand(async () => await SaveProfileAsync());
        EditAvatarCommand = new RelayCommand(() => _nav.NavigateTo("Settings"));
        CreateFlipbookCommand = new RelayCommand(() => OpenFlipbookEditorRequested?.Invoke());
        PostFlipbookCommand = new RelayCommand<SavedFlipbook>(async book => await PostFlipbookAsync(book), book => book != null);
        LoadCloudSavesCommand = new RelayCommand(async () => await LoadCloudSavesAsync());

        SelectedMemeScene = MemeSceneOptions[0];
        LoadProfile();
        _ = RefreshCommunityAsync();
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
        public MemeCaptionDefinition Definition { get; }
        public string Label => Definition.Label;
        public string Text { get => _text; set => SetProperty(ref _text, value); }
        public double Left => _left;
        public double Top => _top;
        public string[] AnchorOptions { get; } = { "top", "middle", "bottom" };
        public string LeftText
        {
            get => _leftText;
            set
            {
                if (!SetProperty(ref _leftText, value)) return;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    _left = Math.Clamp(parsed, 0, 480);
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
                    _top = Math.Clamp(parsed, 0, 480);
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
            _leftText = definition.X.ToString("0", CultureInfo.InvariantCulture);
            _topText = definition.Y.ToString("0", CultureInfo.InvariantCulture);
            _at = definition.At;
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
            await Task.WhenAll(RefreshAsync(), RefreshPostsAsync(), RefreshSavedAsync(), RefreshPeopleAsync());
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
                var rows = await _core.FetchMessagesAsync(roomCode, 40, 0, CancellationToken.None);
                found.AddRange(rows.Where(m => string.IsNullOrEmpty(m.ReplyTo)));
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Neighbourhood day {Day} is unavailable", day); }
        }
        var posts = found.OrderByDescending(m => m.Timestamp).Take(100).ToList();
        try
        {
            var authors = posts.Select(m => m.UserId).Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var profiles = await _core.FetchPublicProfilesAsync(authors, CancellationToken.None);
            foreach (var post in posts)
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
        var postFrameIds = posts.Select(post => post.ProfileFrame).Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var postFrameTasks = postFrameIds.Select(async id => (Id: id, Image: await ProfileFrameImageLoader.LoadAsync(id))).ToArray();
        var postFrames = (await Task.WhenAll(postFrameTasks))
            .ToDictionary(item => item.Id, item => item.Image, StringComparer.OrdinalIgnoreCase);
        foreach (var post in posts)
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
        if (string.Equals((JoinCode ?? "").Trim(), string.Concat("virtual", "insanity"), StringComparison.OrdinalIgnoreCase))
        {
            SettingsViewModel.NoteFound(string.Concat("virtual", "insanity"));
            try { Process.Start(new ProcessStartInfo(string.Concat("https://www.youtube.com/watch?v=", "4JkIs", "37a2JE")) { UseShellExecute = true }); } catch { }
            return;
        }
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
