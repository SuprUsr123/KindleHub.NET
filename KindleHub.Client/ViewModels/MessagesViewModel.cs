// KindleHub.NET — by SuprUsr123, with </3.
// This app, if you modify it in any way, must be contributed back to the main branch
// at https://github.com/SuprUsr123/KindleHub.NET. Please keep the author credit
// ("By SuprUsr123, with </3", in Settings → About) intact in whatever you ship.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using KindleHub.Client.Media;
using KindleHub.Core;
using Microsoft.Extensions.Logging;

namespace KindleHub.Client.ViewModels;

/// <summary>
/// Chat rooms over the official encrypted transport. Rooms = Global Chat + Topics opened from
/// Community + DMs. Text is decrypted on read and encrypted on send by KindleHubCore.
/// Tapping a message selects it and shows its action bar (React / Reply / Star / To notes / DM /
/// Report / Report name / Copy / Unsend / Pin), matching the official web client's per-message menu.
/// </summary>
public class MessagesViewModel : ViewModelBase, IDisposable
{
    private const int RecentGlobalChatWindowSize = 54;
    private const int RecentTopicWindowSize = 60;

    private static int RecentWindowSizeFor(Group? room)
        => room != null && string.Equals(room.Code, KindleHubCore.GlobalGroupCode, StringComparison.Ordinal)
            ? RecentGlobalChatWindowSize
            : RecentTopicWindowSize;

    private static bool IsGlobalRoom(string? code)
        => string.Equals(code, KindleHubCore.GlobalGroupCode, StringComparison.Ordinal);

    public static readonly string[] QuickReactions = { "+1", "-1", "lol", "?", "!", "<3", "♥", "★", "✓", "☺" };
    /// <summary>Instance mirror for XAML ItemsSource binding.</summary>
    public List<string> QuickReactionsList => QuickReactions.ToList();
    public static readonly string[] MsgReportReasons = { "Spam", "Harassment", "Hate speech", "Sexual content", "Threats / violence", "Misinformation", "Other" };
    public static readonly string[] NameReportReasons = { "Offensive / slur", "Sexual", "Harassment", "Impersonation", "Spam / ad", "Possible hacking / account-security issue", "Other" };

    private readonly KindleHubCore _core;
    private readonly ILogger<MessagesViewModel> _logger;
    private readonly List<Message> _pendingHydrate = new();
    private readonly DispatcherTimer _settleTimer;
    private readonly Timer _pollTimer;
    private readonly HashSet<string> _seenInvites = new(StringComparer.Ordinal);
    private DateTimeOffset _inboxSince = DateTimeOffset.UtcNow;
    private Control? _host;
    private int _polling;
    private bool _disposed;

    private ObservableCollection<Group> _groups = new();
    private Group? _selectedGroup;
    private ObservableCollection<Message> _messages = new();
    private ObservableCollection<Message> _activePolls = new();
    private List<Message> _pollVotes = new();

    public ObservableCollection<Message> ActivePolls { get => _activePolls; set => SetProperty(ref _activePolls, value); }
    private string _newMessageText = "";
    private string _joinCode = "";
    private bool _isLoading;
    private bool _sending;
    private string _statusText = "Select a room.";

    private Message? _activeMessage;
    private bool _replyCompose;
    private bool _reportOpen;
    private bool _isNameReport;
    private string _reportReason = "";
    private string _reportNote = "";
    private MediaSendMode _mediaSendMode = MediaSendMode.None;

    private string _pollQuestion = "";
    private string _pollOption1 = "";
    private string _pollOption2 = "";
    private string _pollOption3 = "";
    private string _pollOption4 = "";
    private bool _pollIsOpen = true;

    private string _flipbookName = "";
    private string? _flipbookWire;

    private string _storySetting = "";
    private string _storyTheme = "";
    private string _storyLogText = "";

    private string _appLabel = "";
    private string _appHtml = "";

    public class StickerItem
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Svg { get; set; } = "";
    }

    public enum MediaSendMode { None, Sticker, Poll, Flipbook, Story, App }

    public ObservableCollection<Group> Groups { get => _groups; set => SetProperty(ref _groups, value); }

    public Group? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                ClearActiveMessage();
                _messages.Clear();
                StatusText = value == null ? "Select a room." : $"#{value.Code}";
                SendCommand.RaiseCanExecuteChanged();
                ReloadCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(IsChatting));
                IsGlobalChat = IsGlobalRoom(value?.Code);
                OnPropertyChanged(nameof(IsGlobalChat));
                _ = ReloadRoomAsync();
            }
        }
    }

    public bool IsGlobalChat { get; private set; }
    public bool IsChatting => _selectedGroup != null;

    public bool IsFollowing { get; private set; } = true;

    public void SetFollowState(bool following)
    {
        if (IsFollowing == following) return;
        IsFollowing = following;
        if (following) RequestScroll();
    }

    /// <summary>Raised after new content arrives. The view scrolls down only when
    /// the reader is already following the conversation, so scrolling back through
    /// history is never yanked away by the next poll.</summary>
    public event EventHandler? ScrollRequested;
    public void RequestScroll()
    {
        if (!IsFollowing) return;
        ScrollRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised for reader-initiated jumps — opening a room, or sending a
    /// message — where the newest row must be visible regardless of scroll state.</summary>
    public event EventHandler? ForceScrollRequested;
    public void ForceScroll() => ForceScrollRequested?.Invoke(this, EventArgs.Empty);

    public ObservableCollection<Message> Messages { get => _messages; set => SetProperty(ref _messages, value); }
    public string NewMessageText { get => _newMessageText; set { if (SetProperty(ref _newMessageText, value)) SendCommand.RaiseCanExecuteChanged(); } }
    public string JoinCode { get => _joinCode; set { if (SetProperty(ref _joinCode, value)) JoinCommand.RaiseCanExecuteChanged(); } }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
    public bool IsSending { get => _sending; set => SetProperty(ref _sending, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    public Message? ActiveMessage
    {
        get => _activeMessage;
        private set
        {
            if (ReferenceEquals(_activeMessage, value)) return;
            if (_activeMessage != null) _activeMessage.IsSelected = false;
            _activeMessage = value;
            if (value != null) value.IsSelected = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasActiveMessage));
        }
    }

    public bool HasActiveMessage => _activeMessage != null;
    public bool CanUnsend => ActiveMessage != null && ActiveMessage.IsMine && !string.IsNullOrEmpty(ActiveMessage.OwnerSecret);

    public bool ReplyCompose { get => _replyCompose; private set { _replyCompose = value; OnPropertyChanged(); CancelReplyCommand.RaiseCanExecuteChanged(); } }
    public string ReplyTargetLabel { get; private set; } = "";

    public bool ReportOpen { get => _reportOpen; private set { _reportOpen = value; OnPropertyChanged(); CancelReportCommand.RaiseCanExecuteChanged(); SubmitReportCommand.RaiseCanExecuteChanged(); } }
    public bool IsNameReport { get => _isNameReport; private set => SetProperty(ref _isNameReport, value); }
    public string ReportReason { get => _reportReason; set => SetProperty(ref _reportReason, value); }
    public string ReportNote { get => _reportNote; set => SetProperty(ref _reportNote, value); }
    public string[] ReportReasons => IsNameReport ? NameReportReasons : MsgReportReasons;

    public bool MediaSendOpen { get => _mediaSendMode != MediaSendMode.None; }
    public string MediaSendTitle => _mediaSendMode switch
    {
        MediaSendMode.Sticker => "Send sticker",
        MediaSendMode.Poll => "Create poll",
        MediaSendMode.Flipbook => "Create flipbook",
        MediaSendMode.Story => "Create story",
        MediaSendMode.App => "Share app",
        _ => ""
    };
    public bool IsStickerMode => _mediaSendMode == MediaSendMode.Sticker;
    public bool IsPollMode => _mediaSendMode == MediaSendMode.Poll;
    public bool IsFlipbookMode => _mediaSendMode == MediaSendMode.Flipbook;
    public bool IsStoryMode => _mediaSendMode == MediaSendMode.Story;
    public bool IsAppMode => _mediaSendMode == MediaSendMode.App;

    public ObservableCollection<StickerItem> StickerEntries { get; } = new();

    public string PollQuestion { get => _pollQuestion; set => SetProperty(ref _pollQuestion, value); }
    public string PollOption1 { get => _pollOption1; set => SetProperty(ref _pollOption1, value); }
    public string PollOption2 { get => _pollOption2; set => SetProperty(ref _pollOption2, value); }
    public string PollOption3 { get => _pollOption3; set => SetProperty(ref _pollOption3, value); }
    public string PollOption4 { get => _pollOption4; set => SetProperty(ref _pollOption4, value); }
    public bool PollIsOpen { get => _pollIsOpen; set => SetProperty(ref _pollIsOpen, value); }

    public string FlipbookName { get => _flipbookName; set => SetProperty(ref _flipbookName, value); }
    public ObservableCollection<string> FlipbookFrames { get; } = new();

    /// <summary>Number of frames the editor handed over, shown next to the preview.</summary>
    public string FlipbookFrameCountText => FlipbookFrames.Count == 0
        ? "No frames yet — tap Draw flipbook to start drawing."
        : $"{FlipbookFrames.Count} frame{(FlipbookFrames.Count == 1 ? "" : "s")} ready to send.";

    private Avalonia.Media.Imaging.Bitmap? _flipbookPreview;
    /// <summary>Rendered first frame, so the composer shows the drawing rather than
    /// the packed hex string.</summary>
    public Avalonia.Media.Imaging.Bitmap? FlipbookPreview
    {
        get => _flipbookPreview;
        private set => SetProperty(ref _flipbookPreview, value);
    }

    public string StorySetting { get => _storySetting; set => SetProperty(ref _storySetting, value); }
    public string StoryTheme { get => _storyTheme; set => SetProperty(ref _storyTheme, value); }
    public string StoryLogText { get => _storyLogText; set => SetProperty(ref _storyLogText, value); }
    public ObservableCollection<string> StoryLog { get; } = new();

    public string AppLabel { get => _appLabel; set => SetProperty(ref _appLabel, value); }
    public string AppHtml { get => _appHtml; set { if (SetProperty(ref _appHtml, value)) OnPropertyChanged(nameof(AppHtmlSizeLabel)); } }

    /// <summary>Character count for the pasted HTML, so a large paste is visibly
    /// accepted rather than looking like nothing happened.</summary>
    public string AppHtmlSizeLabel
    {
        get
        {
            var n = _appHtml?.Length ?? 0;
            if (n == 0) return "Paste HTML directly with Ctrl+V.";
            return $"{n:N0} characters";
        }
    }

    public RelayCommand ToggleStickerPanelCommand { get; }
    public RelayCommand TogglePollPanelCommand { get; }
    public RelayCommand ToggleFlipbookPanelCommand { get; }
    public RelayCommand ToggleStoryPanelCommand { get; }
    public RelayCommand ToggleAppPanelCommand { get; }
    public RelayCommand<string> SendStickerCommand { get; }
    public RelayCommand<string> VoteCommand { get; }
    public RelayCommand SendPollCommand { get; }
    public RelayCommand SendFlipbookCommand { get; }
    public RelayCommand SendStoryCommand { get; }
    public RelayCommand SendAppCommand { get; }
    public RelayCommand CloseMediaSendCommand { get; }

    public RelayCommand SendCommand { get; }
    public RelayCommand JoinCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand<Message> SelectMessageCommand { get; }
    public RelayCommand DeselectCommand { get; }
    public RelayCommand<Message> ToggleStarCommand { get; }
    public RelayCommand<Message> ToNotesCommand { get; }
    public RelayCommand<Message> CopyCommand { get; }
    public RelayCommand<Message> StartReplyCommand { get; }
    public RelayCommand CancelReplyCommand { get; }
    public RelayCommand<string> ReactCommand { get; }
    public RelayCommand<Message> ToggleImportantCommand { get; }
    public RelayCommand<Message> UnsendMessageCommand { get; }
    public RelayCommand<Message> ReportCommand { get; }
    public RelayCommand<Message> ReportNameCommand { get; }
    public RelayCommand SubmitReportCommand { get; }
    public RelayCommand CancelReportCommand { get; }
    public RelayCommand<Message> DmUserCommand { get; }

    public MessagesViewModel(KindleHubCore core, ILogger<MessagesViewModel> logger)
    {
        _core = core;
        _logger = logger;
        _core.MessageReceived += OnMessageReceived;

        SendCommand = new RelayCommand(async () => await SendMessageAsync(), () => !IsSending && SelectedGroup != null && !string.IsNullOrWhiteSpace(NewMessageText));
        JoinCommand = new RelayCommand(async () => await JoinByCodeAsync(), () => !string.IsNullOrWhiteSpace(JoinCode));
        ReloadCommand = new RelayCommand(async () => await ReloadRoomAsync(), () => SelectedGroup != null);

        SelectMessageCommand = new RelayCommand<Message>(m => ActiveMessage = (m != null && ActiveMessage?.Id == m.Id) ? null : m);
        DeselectCommand = new RelayCommand(ClearActiveMessage);
        ToggleStarCommand = new RelayCommand<Message>(async m => await ToggleStarAsync(m));
        ToNotesCommand = new RelayCommand<Message>(async m => await StarToNotesAsync(m), _ => ActiveMessage != null);
        CopyCommand = new RelayCommand<Message>(CopyText);
        StartReplyCommand = new RelayCommand<Message>(StartReply);
        CancelReplyCommand = new RelayCommand(CancelReply, () => ReplyCompose);
        ReactCommand = new RelayCommand<string>(async key => await ReactAsync(ActiveMessage!, key ?? ""), _ => ActiveMessage != null);
        ToggleImportantCommand = new RelayCommand<Message>(async m => await ToggleImportantAsync(m),
            m => m != null && m.IsMine && !string.IsNullOrEmpty(m.OwnerSecret));
        UnsendMessageCommand = new RelayCommand<Message>(async m => await UnsendMessageInternalAsync(m),
            m => m != null && m.IsMine && !string.IsNullOrEmpty(m.OwnerSecret));
        ReportCommand = new RelayCommand<Message>(m => { if (m != null) OpenReport(m, nameReport: false); });
        ReportNameCommand = new RelayCommand<Message>(m => { if (m != null) OpenReport(m, nameReport: true); });
        SubmitReportCommand = new RelayCommand(async () => await SubmitReportAsync(), () => ReportOpen && !string.IsNullOrEmpty(ReportReason));
        CancelReportCommand = new RelayCommand(CloseReport, () => ReportOpen);
        DmUserCommand = new RelayCommand<Message>(async m => await DmAsync(m), m => m != null);

        ToggleStickerPanelCommand = new RelayCommand(() => SetMediaMode(_mediaSendMode == MediaSendMode.Sticker ? MediaSendMode.None : MediaSendMode.Sticker));
        TogglePollPanelCommand = new RelayCommand(() => SetMediaMode(_mediaSendMode == MediaSendMode.Poll ? MediaSendMode.None : MediaSendMode.Poll));
        ToggleFlipbookPanelCommand = new RelayCommand(() => SetMediaMode(_mediaSendMode == MediaSendMode.Flipbook ? MediaSendMode.None : MediaSendMode.Flipbook));
        ToggleStoryPanelCommand = new RelayCommand(() => SetMediaMode(_mediaSendMode == MediaSendMode.Story ? MediaSendMode.None : MediaSendMode.Story));
        ToggleAppPanelCommand = new RelayCommand(() => SetMediaMode(_mediaSendMode == MediaSendMode.App ? MediaSendMode.None : MediaSendMode.App));
        SendStickerCommand = new RelayCommand<string>(async id => await SendStickerAsync(id));
        VoteCommand = new RelayCommand<string>(async optionText => await VotePollAsync(optionText));
        SendPollCommand = new RelayCommand(async () => await SendPollAsync());
        SendFlipbookCommand = new RelayCommand(async () => await SendFlipbookAsync());
        SendStoryCommand = new RelayCommand(async () => await SendStoryAsync());
        SendAppCommand = new RelayCommand(async () => await SendAppAsync());
        CloseMediaSendCommand = new RelayCommand(() => SetMediaMode(MediaSendMode.None));

        LoadStickers();

        Groups.Add(new Group { Code = KindleHubCore.GlobalGroupCode, Name = "Global Chat", Creator = "KindleHub" });
        Groups.Add(new Group { Code = KindleHubCore.CrossChatGroupCode, Name = "Crosschat", Creator = "KindleHub" });
        foreach (var g in RoomRegistry.Rooms)
        {
            if (!string.IsNullOrEmpty(g?.Code) && !Groups.Any(x => string.Equals(x?.Code, g!.Code, StringComparison.Ordinal)))
                Groups.Add(g);
        }

        RoomRegistry.ActiveRoomChanged += OnActiveRoomChanged;
        // Must exist before SelectedGroup is assigned: the setter synchronously
        // starts ReloadRoomAsync, which touches the timer.
        _settleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _settleTimer.Tick += SettleTimer_Tick;
        SelectedGroup = Groups[0];
        _pollTimer = new Timer(async _ => await PollTickAsync(), null, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8));
    }

    private void OnActiveRoomChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => _ = SelectedFromRegistryAsync());

private async Task SelectedFromRegistryAsync()
        {
            await Task.Yield();
            var active = RoomRegistry.ActiveRoom;
        if (active == null) return;
        var existing = Groups.FirstOrDefault(g => g != null && string.Equals(g.Code, active.Code, StringComparison.Ordinal));
        if (existing == null) { Groups.Add(active); existing = active; }
        SelectedGroup = existing;
    }

    private void ClearActiveMessage()
    {
        ActiveMessage = null;
        ReplyCompose = false;
        CloseReport();
    }
    public void AttachHostControl(Control host) => _host = host;

    public async Task ReloadRoomAsync()
    {
        var room = SelectedGroup;
        if (room == null) return;
        IsLoading = true;
        try
        {
            // A room switch invalidates any batch queued for the previous room.
            // Kept inside the try so a failure here can never strand IsLoading.
            _pendingHydrate.Clear();
            _settleTimer.Stop();

            var pageSize = RecentWindowSizeFor(room);
            var list = await _core.FetchMessagesAsync(room.Code, pageSize, 0, CancellationToken.None);
            ApplyStars(list);
            await _core.HydrateReplyPreviewsAsync(list, room.Code, CancellationToken.None);
            _messages.Clear();
            _pollVotes.Clear();
            // The API returns ts.desc (newest first), but the transcript is
            // rendered oldest-first and appended to as messages arrive, so it has
            // to be sorted ascending here. Without this the newest message sits at
            // the top and "scroll to bottom" lands on the oldest one.
            foreach (var m in list.OrderBy(x => x.Timestamp))
            {
                if (IsRelay(m.Text, room) || IsVoteMessage(m.Text)) { _pollVotes.Add(m); continue; }
                _messages.Add(m);
            }
            TrimMessagesToRecent(room);
            StatusText = $"{_messages.Count} messages · #{room.Code}";
            UpdateActivePolls();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Load chat failed");
            StatusText = "Couldn't load this room yet.";
        }
        finally
        {
            IsLoading = false;
            ForceScroll(); // opening a room always lands on the newest row
        }
    }

    private static void ApplyStars(List<Message> list)
    {
        foreach (var m in list) m.IsStarred = ChatPrefsStore.Current.IsStarred(m.GroupCode + "|" + m.Id);
    }

    public async Task SendMessageAsync()
    {
        var room = SelectedGroup;
        var text = NewMessageText?.Trim();
        if (room == null || string.IsNullOrEmpty(text)) return;
        if (!_core.IsAuthenticated) { StatusText = "Sign in to send messages."; return; }

        var important = text.StartsWith('!');
        var replyToId = ReplyCompose ? ActiveMessage?.Id : null;
        NewMessageText = "";
        IsSending = true;
        try
        {
            var msg = await _core.SendMessageAsync(room.Code, text, important, replyToId, CancellationToken.None);
            _messages.Add(msg);
            TrimMessagesToRecent(room);
            RoomRegistry.AddRoom(room);
            if (ReplyCompose) { ReplyCompose = false; ReplyTargetLabel = ""; OnPropertyChanged(nameof(ReplyTargetLabel)); }
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            _logger.LogInformation(ex, "Send failed");
            NewMessageText = text;
        }
        finally { IsSending = false; ForceScroll(); UpdateActivePolls(); }
    }

private void StartReply(Message? m)
    {
        if (m == null || SelectedGroup == null) return;
        ActiveMessage = m;
        ReplyCompose = true;
        ReplyTargetLabel = "Replying to " + (m.DisplayName ?? "someone");
        OnPropertyChanged(nameof(ReplyTargetLabel));
    }

    private void CancelReply()
    {
        ReplyCompose = false;
        ReplyTargetLabel = "";
        OnPropertyChanged(nameof(ReplyTargetLabel));
    }

    public async Task ToggleStarAsync(Message? m)
    {
        if (m == null) return;
        m.IsStarred = !m.IsStarred;
        ChatPrefsStore.Current.ToggleStar(m.GroupCode + "|" + m.Id);
        await Task.CompletedTask;
    }

    public async Task StarToNotesAsync(Message? m)
    {
        if (m == null) return;
        if (!_core.IsAuthenticated) { StatusText = "Sign in first."; return; }
        var snapshot = m.IsImage ? ("[photo]") : (m.Text ?? "");
        var note = snapshot + "  — " + (m.DisplayName ?? "") + ", " + (SelectedGroup?.Name ?? SelectedGroup?.Code ?? "");
        var ok = await _core.StarToNotesAsync(note, CancellationToken.None);
        StatusText = ok ? "Saved to your notes (synced)." : "Couldn't save to notes.";
    }

    private void CopyText(Message? m)
    {
        if (m == null) return;
        var clip = _host != null ? TopLevel.GetTopLevel(_host)?.Clipboard : null;
        if (clip != null) _ = clip.SetTextAsync(m.Text ?? "");
        StatusText = m.IsImage ? "Images can't be copied as text." : "Copied.";
    }

    public async Task ReactAsync(Message m, string key)
    {
        if (m == null || string.IsNullOrEmpty(key)) return;
        if (!_core.IsAuthenticated) { StatusText = "Sign in to react."; return; }
        var room = SelectedGroup ?? new Group { Code = m.GroupCode };
        var ok = await _core.ToggleReactionAsync(m.Id, key, CancellationToken.None);
        if (!ok) return;
        var list = await _core.FetchMessagesAsync(room.Code, RecentWindowSizeFor(room), 0, CancellationToken.None);
        ApplyStars(list);
        await _core.HydrateReplyPreviewsAsync(list, room.Code, CancellationToken.None);
_messages.Clear();
            _pollVotes.Clear();
            foreach (var row in list.OrderBy(r => r.Timestamp))
            {
                if (IsRelay(row.Text, room) || IsVoteMessage(row.Text)) { _pollVotes.Add(row); continue; }
                _messages.Add(row);
            }
            ActiveMessage = list.FirstOrDefault(x => x.Id == m.Id);
            RequestScroll();
    }

    public async Task ToggleImportantAsync(Message? m)
    {
        if (m == null || !m.IsMine || string.IsNullOrEmpty(m.OwnerSecret)) return;
        m.Important = !m.Important;
        var ok = await _core.ToggleImportantAsync(m.Id, m.OwnerSecret, m.Important, CancellationToken.None);
        if (!ok) m.Important = !m.Important;
    }

    private async Task UnsendMessageInternalAsync(Message? m)
    {
        if (m == null || !m.IsMine || string.IsNullOrEmpty(m.OwnerSecret)) return;
        if (await _core.UnsendMessageAsync(m.Id, m.OwnerSecret, CancellationToken.None))
        {
            _messages.Remove(m);
            ClearActiveMessage();
        }
    }

    public async Task DmAsync(Message? m)
    {
        if (m == null) return;
        if (!_core.IsAuthenticated) { StatusText = "Sign in to send messages."; return; }
        if (string.IsNullOrEmpty(m.UserId)) { StatusText = "That account can't be DM'd."; return; }

        try
        {
            var known = ChatPrefsStore.Current.DmRoomFor(m.UserId);
            var group = await _core.OpenDmAsync(m.UserId, m.DisplayName ?? "friend", known ?? "", CancellationToken.None);
            ChatPrefsStore.Current.RememberDmRoom(m.UserId, group.Code);
            if (!Groups.Any(g => g?.Code == group.Code)) Groups.Add(group);
            RoomRegistry.Open(group);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Open DM failed");
            StatusText = "Couldn't open that chat.";
        }
    }

    private void OpenReport(Message m, bool nameReport)
    {
        if (m == null) return;
        if (!_core.IsAuthenticated) { StatusText = "Sign in first."; return; }
        ActiveMessage = m;
        IsNameReport = nameReport;
        ReportReason = ChatPrefsStore.Current.ReportedReason(m.GroupCode + "|" + m.Id) ?? "";
        ReportNote = "";
        ReportOpen = true;
        OnPropertyChanged(nameof(ReportReasons));
    }

    private void CloseReport()
    {
        ReportOpen = false;
        IsNameReport = false;
        ReportReason = "";
        ReportNote = "";
    }

    private async Task SubmitReportAsync()
    {
        var m = ActiveMessage;
        if (m == null || string.IsNullOrEmpty(ReportReason)) return;
        var roomName = SelectedGroup?.Name ?? SelectedGroup?.Code ?? "?";
        bool ok;
        try
        {
            // The official client adds [SECURITY] only on username reports whose reason is this.
            var security = IsNameReport && ReportReason == "Possible hacking / account-security issue";
            ok = IsNameReport
                ? await _core.ReportUsernameAsync(ReportReason, ReportNote, m.DisplayName ?? "", m.UserId ?? "",
                      security, SelectedGroup!, m.Id, CancellationToken.None)
                : await _core.ReportMessageAsync(ReportReason, ReportNote, m, roomName, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Report failed"); ok = false;
        }
        StatusText = ok
            ? (IsNameReport ? "Username reported." : "Reported to moderators.")
            : "Couldn't file that report — try again shortly.";
        CloseReport();
    }

    private void SetMediaMode(MediaSendMode mode)
    {
        if (_mediaSendMode == mode) mode = MediaSendMode.None;
        _mediaSendMode = mode;
        OnPropertyChanged(nameof(MediaSendOpen));
        OnPropertyChanged(nameof(MediaSendTitle));
        OnPropertyChanged(nameof(IsStickerMode));
        OnPropertyChanged(nameof(IsPollMode));
        OnPropertyChanged(nameof(IsFlipbookMode));
        OnPropertyChanged(nameof(IsStoryMode));
        OnPropertyChanged(nameof(IsAppMode));
        if (mode != MediaSendMode.None) ClearActiveMessage();
    }

    public void ToggleMediaSendPanel() => SetMediaMode(_mediaSendMode);

    private void LoadStickers()
    {
        ChatMedia.EnsureStickersLoaded();
        foreach (var kv in ChatMedia.Stickers)
            StickerEntries.Add(new StickerItem { Id = kv.Key, Label = kv.Value.Label, Svg = kv.Value.Svg });
    }

    private async Task SendStickerAsync(string? stickerId)
    {
        if (string.IsNullOrEmpty(stickerId) || SelectedGroup == null) return;
        if (!_core.IsAuthenticated) { StatusText = "Sign in to send stickers."; return; }
        var wire = ChatMedia.EncodeSticker(stickerId);
        if (string.IsNullOrEmpty(wire)) { StatusText = "Invalid sticker."; return; }
        try
        {
            var msg = await _core.SendMessageAsync(SelectedGroup.Code, wire, false, null, CancellationToken.None);
            _messages.Add(msg);
            RoomRegistry.AddRoom(SelectedGroup);
        }
        catch (Exception ex) { StatusText = ex.Message; }
        finally { ForceScroll(); UpdateActivePolls(); }
    }

    public async Task VotePollAsync(string? optionText)
    {
        if (ActiveMessage == null || SelectedGroup == null || string.IsNullOrEmpty(optionText)) return;
        if (!_core.IsAuthenticated) { StatusText = "Sign in to vote."; return; }
        var poll = ChatMedia.TryParsePoll(ActiveMessage.Text);
        if (poll == null) { StatusText = "Not a poll."; return; }
        var idx = Array.IndexOf(poll.Options, optionText);
        if (idx < 0) { StatusText = "Invalid option."; return; }
        var wire = ChatMedia.EncodeVote(poll.Id, idx);
        try
        {
            var msg = await _core.SendMessageAsync(SelectedGroup.Code, wire, false, null, CancellationToken.None);
            _pollVotes.Add(msg);
            RoomRegistry.AddRoom(SelectedGroup);
        }
        catch (Exception ex) { StatusText = ex.Message; }
        finally { ForceScroll(); UpdateActivePolls(); }
    }

    private async Task SendPollAsync()
    {
        if (SelectedGroup == null) return;
        if (!_core.IsAuthenticated) { StatusText = "Sign in to send polls."; return; }
        var question = PollQuestion?.Trim();
        if (string.IsNullOrEmpty(question)) { StatusText = "Enter a question."; return; }
        var opts = new List<string>();
        foreach (var o in new[] { PollOption1, PollOption2, PollOption3, PollOption4 })
        {
            var t = o?.Trim();
            if (!string.IsNullOrEmpty(t)) opts.Add(t);
        }
        if (opts.Count < 2) { StatusText = "Add at least two options."; return; }
        var poll = new ChatMedia.Poll { Question = question, Options = opts.ToArray(), Open = PollIsOpen };
        var wire = ChatMedia.EncodePoll(poll);
        if (string.IsNullOrEmpty(wire)) { StatusText = "Couldn't build poll."; return; }
        try
        {
            var msg = await _core.SendMessageAsync(SelectedGroup.Code, wire, false, null, CancellationToken.None);
            _messages.Add(msg);
            RoomRegistry.AddRoom(SelectedGroup);
            PollQuestion = ""; PollOption1 = ""; PollOption2 = ""; PollOption3 = ""; PollOption4 = "";
            PollIsOpen = true;
            SetMediaMode(MediaSendMode.None);
        }
        catch (Exception ex) { StatusText = ex.Message; }
        finally { ForceScroll(); }
    }

    private async Task SendFlipbookAsync()
    {
        if (SelectedGroup == null) return;
        if (!_core.IsAuthenticated) { StatusText = "Sign in to send flipbooks."; return; }
        if (FlipbookFrames.Count == 0) { StatusText = "Draw a flipbook first."; return; }

        // A drawing handed over by the editor window is already a valid wire —
        // send it verbatim rather than re-encoding it.
        var wire = _flipbookWire;
        if (string.IsNullOrEmpty(wire))
        {
            var fb = new ChatMedia.Flipbook
            {
                Name = string.IsNullOrEmpty(FlipbookName) ? "Flip" : FlipbookName,
                Width = 8,
                Height = 8,
                Fps = 6,
                Frames = FlipbookFrames.ToArray()
            };
            wire = ChatMedia.EncodeFlipbook(fb);
        }
        if (string.IsNullOrEmpty(wire)) { StatusText = "Couldn't build flipbook."; return; }
        try
        {
            var msg = await _core.SendMessageAsync(SelectedGroup.Code, wire, false, null, CancellationToken.None);
            _messages.Add(msg);
            RoomRegistry.AddRoom(SelectedGroup);
            FlipbookName = "";
            FlipbookFrames.Clear();
            _flipbookWire = null;
            FlipbookPreview = null;
            OnPropertyChanged(nameof(FlipbookFrameCountText));
            SetMediaMode(MediaSendMode.None);
        }
        catch (Exception ex) { StatusText = ex.Message; }
        finally { ForceScroll(); UpdateActivePolls(); }
    }

    /// <summary>Receive a finished KHFLIP1 wire from the standalone drawing
    /// window. The packed frames are kept verbatim so sending is a straight
    /// pass-through and the drawing is never re-quantised.</summary>
    public void AdoptFlipbookWire(string wire)
    {
        var parsed = ChatMedia.TryParseFlipbook(wire);
        if (parsed == null) { StatusText = "Couldn't read that flipbook."; return; }
        _flipbookWire = wire;
        FlipbookName = parsed.Name;
        FlipbookFrames.Clear();
        foreach (var f in parsed.Frames) FlipbookFrames.Add(f);
        RefreshFlipbookPreview(parsed);
        SetMediaMode(MediaSendMode.Flipbook);
        StatusText = $"Flipbook ready — {parsed.Frames.Length} frames. Hit Send.";
        OnPropertyChanged(nameof(FlipbookFrameCountText));
    }

    /// <summary>Rasterise the first frame of a parsed flipbook for the composer
    /// preview. Uses the same binary-cell grid the official client paints.</summary>
    private void RefreshFlipbookPreview(ChatMedia.Flipbook? parsed)
    {
        FlipbookPreview = null;
        var w = parsed?.Width ?? 0;
        var h = parsed?.Height ?? 0;
        var frame = parsed?.Frames?.FirstOrDefault();
        if (parsed == null || w <= 0 || h <= 0 || string.IsNullOrEmpty(frame)) return;

        const int side = 128;
        var cellX = Math.Max(1, side / w);
        var cellY = Math.Max(1, side / h);
        var buf = new byte[side * side * 4];
        for (var i = 0; i < side * side; i++)
        {
            buf[i * 4 + 0] = 0xFF; buf[i * 4 + 1] = 0xFF;
            buf[i * 4 + 2] = 0xFF; buf[i * 4 + 3] = 0xFF;
        }

        var cells = ChatMedia.FlipUnpack(frame, w * h);
        for (var row = 0; row < h; row++)
        {
            for (var col = 0; col < w; col++)
            {
                if (cells[row * w + col] == 0) continue;
                for (var dy = 0; dy < cellY; dy++)
                {
                    var y = row * cellY + dy;
                    if (y >= side) break;
                    for (var dx = 0; dx < cellX; dx++)
                    {
                        var x = col * cellX + dx;
                        if (x >= side) break;
                        var o = (y * side + x) * 4;
                        buf[o + 0] = 0x11; buf[o + 1] = 0x11;
                        buf[o + 2] = 0x11; buf[o + 3] = 0xFF;
                    }
                }
            }
        }

        try
        {
            var bmp = new Avalonia.Media.Imaging.WriteableBitmap(
                new Avalonia.PixelSize(side, side), new Avalonia.Vector(96, 96));
            using (var locked = bmp.Lock())
            {
                var rowBytes = locked.RowBytes;
                for (var y = 0; y < side; y++)
                    System.Runtime.InteropServices.Marshal.Copy(
                        buf, y * side * 4, locked.Address + (nint)(y * rowBytes), side * 4);
            }
            FlipbookPreview = bmp;
        }
        catch
        {
            FlipbookPreview = null; // preview is cosmetic; sending still works
        }
    }

    private async Task SendStoryAsync()
    {
        if (SelectedGroup == null) return;
        if (!_core.IsAuthenticated) { StatusText = "Sign in to send stories."; return; }
        var setting = StorySetting?.Trim() ?? "A story";
        var theme = StoryTheme?.Trim() ?? "";
        if (StoryLog.Count == 0) { StatusText = "Add at least one story line."; return; }
        var story = new ChatMedia.Story
        {
            Setting = string.IsNullOrEmpty(setting) ? "A story" : setting,
            Theme = theme,
            Log = StoryLog.Select(t => new ChatMedia.StoryLogEntry { Role = "ai", Text = t }).ToArray()
        };
        var wire = ChatMedia.EncodeStory(story);
        if (string.IsNullOrEmpty(wire)) { StatusText = "Story too long."; return; }
        try
        {
            var msg = await _core.SendMessageAsync(SelectedGroup.Code, wire, false, null, CancellationToken.None);
            _messages.Add(msg);
            RoomRegistry.AddRoom(SelectedGroup);
            StorySetting = ""; StoryTheme = ""; StoryLog.Clear(); StoryLogText = "";
            SetMediaMode(MediaSendMode.None);
        }
        catch (Exception ex) { StatusText = ex.Message; }
        finally { ForceScroll(); UpdateActivePolls(); }
    }

    public void AddStoryLine()
    {
        var t = StoryLogText?.Trim();
        if (string.IsNullOrEmpty(t)) return;
        StoryLog.Add(t);
        StoryLogText = "";
        OnPropertyChanged(nameof(StoryLog));
    }

    /// <summary>Set the composer HTML from pasted text, enforcing the same cap the
    /// file loader does so the two paths behave identically.</summary>
    public void SetAppHtmlText(string html)
    {
        if (html.Length > ChatMedia.MaxAppHtmlLen)
        {
            StatusText = $"That's {html.Length / 1024.0:0.#} KB — the share cap is 512 KB.";
            return;
        }
        AppHtml = html;
        StatusText = $"Loaded {AppHtmlSizeLabel}. Hit Send app to share it.";
    }

    /// <summary>Fill the app-share composer from a file on disk. The name defaults
    /// to the filename so the author does not have to retype it, but stays
    /// editable.</summary>
    public async Task LoadAppHtmlFileAsync(string path, byte[] bytes)
    {
        try
        {
            var html = System.Text.Encoding.UTF8.GetString(bytes);
            // Gate on the decoded character count, not the byte count: that is what
            // ChatMedia measures, and a file with multi-byte characters would
            // otherwise be rejected even though it fits the wire cap.
            if (html.Length > ChatMedia.MaxAppHtmlLen)
            {
                StatusText = $"That file is {html.Length / 1024.0:0.#} KB — the share cap is 512 KB.";
                return;
            }
            AppHtml = html;
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(name)) AppLabel = name;
            StatusText = $"Loaded {AppHtmlSizeLabel}. Hit Send app to share it.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "App share file load failed");
            StatusText = "Couldn't read that HTML file.";
        }
        await Task.CompletedTask;
    }

    private async Task SendAppAsync()
    {
        if (SelectedGroup == null) return;
        if (!_core.IsAuthenticated) { StatusText = "Sign in to share apps."; return; }
        var label = AppLabel?.Trim() ?? "App";
        var html = AppHtml ?? "";
        if (html.Length > 524288) { StatusText = "HTML too long (max 524288)."; return; }
        var app = new ChatMedia.AppShare { Label = label, Html = html };
        var wire = ChatMedia.EncodeAppShare(app);
        if (string.IsNullOrEmpty(wire)) { StatusText = "Couldn't build app share."; return; }
        try
        {
            var msg = await _core.SendMessageAsync(SelectedGroup.Code, wire, false, null, CancellationToken.None);
            _messages.Add(msg);
            RoomRegistry.AddRoom(SelectedGroup);
            AppLabel = ""; AppHtml = "";
            SetMediaMode(MediaSendMode.None);
        }
        catch (Exception ex) { StatusText = ex.Message; }
        finally { ForceScroll(); UpdateActivePolls(); }
    }

    public async Task SendImageDataAsync(string fileName, byte[] bytes)
    {
        var room = SelectedGroup;
        if (room == null) { StatusText = "Pick a room first."; return; }
        if (!_core.IsAuthenticated) { StatusText = "Sign in to send messages."; return; }
        if (bytes == null || bytes.Length == 0) { StatusText = "Empty image."; return; }

        var mime = GuessMime(fileName);
        if (mime == null) { StatusText = "Only jpg / png / gif / webp images can be sent."; return; }

        var dataUri = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        var wire = ChatMedia.EncodeImage(dataUri);
        if (wire == null) { StatusText = "Image too large — the web client caps it at ~110 KB of base64."; return; }

        IsSending = true;
        try
        {
            var msg = await _core.SendMessageAsync(room.Code, wire, false, null, CancellationToken.None);
            _messages.Add(msg);
            RoomRegistry.AddRoom(room);
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            _logger.LogInformation(ex, "Send image failed");
        }
        finally { IsSending = false; ForceScroll(); }
    }

    private static string? GuessMime(string file)
    {
        var ext = System.IO.Path.GetExtension(file ?? "").ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => null
        };
    }

    public async Task JoinByCodeAsync()
    {
        var digits = new string((JoinCode ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length < 12) { StatusText = "Room codes are 12 digits."; return; }
        digits = digits[^12..];
        JoinCode = "";
        try
        {
            var group = await _core.JoinGroupByCodeAsync(digits, CancellationToken.None);
            if (!Groups.Any(x => string.Equals(x?.Code, group.Code, StringComparison.Ordinal))) Groups.Add(group);
            RoomRegistry.AddRoom(group);
            SelectedGroup = group;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Join failed");
            StatusText = "Couldn't join that room.";
        }
    }

    // ── flipbook playback ─────────────────────────────────────────────────────

    /// <summary>Export a flipbook message as an animated GIF and hand it to the
    /// desktop's default viewer. The wire format is a binary cell grid with no
    /// colour information, so the file is written at a fixed on-screen scale —
    /// big enough to watch comfortably on a laptop.</summary>
    public void OpenFlipbook(Message? message)
    {
        if (message == null) return;
        var fb = ChatMedia.TryParseFlipbook(message.Text);
        if (fb == null || fb.Frames == null || fb.Frames.Length == 0)
        {
            StatusText = "That flipbook couldn't be read.";
            return;
        }

        try
        {
            var w = Math.Clamp(fb.Width, 1, 64);
            var h = Math.Clamp(fb.Height, 1, 64);
            var cell = Math.Max(2, 480 / Math.Max(w, h));
            var frameW = w * cell;
            var frameH = h * cell;
            var delay = 1000 / Math.Clamp(fb.Fps <= 0 ? 6 : fb.Fps, 1, 15);

            var frames = new List<byte[]>(fb.Frames.Length);
            foreach (var packed in fb.Frames)
            {
                var cells = ChatMedia.FlipUnpack(packed ?? "", w * h);
                var pixels = new byte[frameW * frameH];
                for (var row = 0; row < h; row++)
                {
                    for (var col = 0; col < w; col++)
                    {
                        if (cells[row * w + col] == 0) continue;
                        for (var dy = 0; dy < cell; dy++)
                        {
                            var y = row * cell + dy;
                            if (y >= frameH) break;
                            for (var dx = 0; dx < cell; dx++)
                            {
                                var x = col * cell + dx;
                                if (x >= frameW) break;
                                pixels[y * frameW + x] = 1;
                            }
                        }
                    }
                }
                frames.Add(pixels);
            }

            var gif = GifWriter.Build(frameW, frameH, frames, delay);
            var safeName = MakeSafeFileName(string.IsNullOrWhiteSpace(fb.Name) ? "flipbook" : fb.Name!);
            var path = Path.Combine(Path.GetTempPath(), $"{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.gif");
            File.WriteAllBytes(path, gif);

            LaunchViewer(path);
            StatusText = $"Opened {frames.Count} frames in your GIF viewer.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Flipbook export failed");
            StatusText = "Couldn't open that flipbook: " + ex.Message;
        }
    }

    private static void LaunchViewer(string path)
    {
        var info = new ProcessStartInfo(path) { UseShellExecute = true };
        Process.Start(info);
    }

    private static string MakeSafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        if (string.IsNullOrEmpty(cleaned)) cleaned = "flipbook";
        return cleaned.Length > 40 ? cleaned[..40] : cleaned;
    }

    // ── opening a shared app ─────────────────────────────────────────────────

    /// <summary>Write an app share's inline HTML to disk and open it in the default
    /// browser. Nothing is downloaded: a KHAPP1 message carries its HTML in the
    /// message body, so this works even fully offline.</summary>
    public async Task OpenAppShareAsync(Message? message)
    {
        if (message == null) return;
        var app = ChatMedia.TryParseAppShare(message.Text);
        if (app == null) { StatusText = "That app share couldn't be read."; return; }
        var html = app.Html ?? "";
        if (string.IsNullOrWhiteSpace(html)) { StatusText = "That app share is empty."; return; }

        var label = string.IsNullOrWhiteSpace(app.Label) ? "Shared app" : app.Label!.Trim();
        try
        {
            var safe = new string(label.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
            if (safe.Length == 0) safe = "app";
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KindleHubPro", "chat-apps");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, safe + ".html");
            await File.WriteAllTextAsync(path, html, CancellationToken.None);
            OpenInDefaultBrowser(path, label);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Opening a shared app failed");
            StatusText = "Couldn't open that app: " + ex.Message;
        }
    }

    private void OpenInDefaultBrowser(string path, string label)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo($"@\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", $"@\"{path}\"");
            else
                Process.Start("xdg-open", $"@\"{path}\"");
            StatusText = $"Opened \"{label}\" in your browser.";
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "No browser available for shared app");
            // Headless session or no browser: the file is still on disk.
            StatusText = $"No browser available. The HTML was saved to: {path}";
        }
    }

    // ── story reading ─────────────────────────────────────────────────────────

    /// <summary>Parsed story behind a chat row, for the story viewer window.</summary>
    public ChatMedia.Story? GetStory(Message? message)
        => message == null ? null : ChatMedia.TryParseStory(message.Text);

    private static bool IsRelay(string? text, Group? room)
    {
        if (room?.Code is not { Length: > 0 } code) return false;
        if (!(code.StartsWith("900000", StringComparison.Ordinal) || code.StartsWith("80000077", StringComparison.Ordinal)))
            return false;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        return s.Length > 2 && s[0] == '{' && s[^1] == '}' && s.Contains("\"type\"", StringComparison.Ordinal);
    }

    /// <summary>Inserts a fetched message into the list.
    ///
    /// FetchMessagesAsync raises MessageReceived once for <em>every</em> row in the
    /// page it returns, so this runs for the whole backlog on every poll — not just
    /// for genuinely new traffic. It therefore has to be cheap and, above all, must
    /// not touch rows it has already seen: the previous version removed and re-added
    /// each existing message, which rebuilt every row container and made the list
    /// visibly thrash on each tick.
    ///
    /// Only genuinely new ids are inserted; everything else is ignored. The
    /// follow-up work (reply previews, poll tallies, scrolling) is coalesced into a
    /// single debounced pass by <see cref="ScheduleSettle"/>.
    /// </summary>
    private void OnMessageReceived(object? sender, Message e)
    {
        var room = SelectedGroup;
        if (room == null || !string.Equals(e.GroupCode, room.Code, StringComparison.Ordinal)) return;
        if (string.IsNullOrEmpty(e.Id)) return;
        if (IsRelay(e.Text, room)) return;

        Dispatcher.UIThread.Post(() =>
        {
            // The room may have changed while this was queued.
            var current = SelectedGroup;
            if (current == null || !string.Equals(e.GroupCode, current.Code, StringComparison.Ordinal)) return;

            if (IsVoteMessage(e.Text))
            {
                // A vote's payload is fixed for its id, so a repeat fetch of the
                // same vote cannot change any tally — only settle for new ones,
                // otherwise every tick would wake the timer for the whole backlog.
                var existing = _pollVotes.FindIndex(v => string.Equals(v.Id, e.Id, StringComparison.Ordinal));
                if (existing >= 0) { _pollVotes[existing] = e; return; }
                _pollVotes.Add(e);
                ScheduleSettle();
                return;
            }

            if (_messages.Any(m => string.Equals(m.Id, e.Id, StringComparison.Ordinal)))
            {
                // Already displayed. Editing in place would be nice, but re-adding
                // is what caused the jumping, so leave the row completely alone.
                return;
            }

            e.IsStarred = ChatPrefsStore.Current.IsStarred(e.GroupCode + "|" + e.Id);
            _messages.Add(e);
            TrimMessagesToRecent(current);
            _pendingHydrate.Add(e);
            ScheduleSettle();
        });
    }

    private void TrimMessagesToRecent(Group? room)
    {
        var maxMessages = RecentWindowSizeFor(room);
        while (_messages.Count > maxMessages)
            _messages.RemoveAt(0);
    }

    /// <summary>Coalesce the burst of rows from one fetch into a single pass, so a
    /// poll performs one hydrate, one poll-tally refresh and at most one scroll.</summary>
    private void ScheduleSettle()
    {
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    private async void SettleTimer_Tick(object? sender, EventArgs e)
    {
        _settleTimer.Stop();

        // A vote adds no visible row to the transcript — it only changes the poll
        // tallies. Refresh those but do not scroll and do not count it as an
        // unread message, or the "new" tally drifts upwards on every tick.
        if (_pendingHydrate.Count == 0) { UpdateActivePolls(); return; }

        var batch = _pendingHydrate.ToList();
        _pendingHydrate.Clear();
        var room = SelectedGroup;
        if (room == null) return;

        try
        {
            // Only the newly-arrived rows need their reply previews resolved.
            await _core.HydrateReplyPreviewsAsync(batch, room.Code, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Reply preview hydration failed");
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (SelectedGroup == null) return;
            StatusText = $"{_messages.Count} messages · #{SelectedGroup.Code}";
            UpdateActivePolls();
            RequestScroll();
        });
    }

    private static bool IsVoteMessage(string? text)
    {
        return !string.IsNullOrEmpty(text) && text.StartsWith("KHVOTE1:", StringComparison.Ordinal);
    }

    private void UpdateActivePolls()
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Nothing to show and nothing to tear down — skip the Clear/Add cycle
            // so an ordinary chat with no polls does no observable work per tick.
            if (_activePolls.Count == 0 && !_messages.Any(m => ChatMedia.IsPollMessage(m.Text)))
                return;

            _activePolls.Clear();
            UpdatePollTallies();
            foreach (var m in _messages.Where(m => ChatMedia.IsPollMessage(m.Text)))
                _activePolls.Add(m);
        });
    }

    private void UpdatePollTallies()
    {
        var all = _messages.Concat(_pollVotes).ToList();
        foreach (var pollMsg in all.Where(m => ChatMedia.IsPollMessage(m.Text)).ToList())
        {
            var poll = ChatMedia.TryParsePoll(pollMsg.Text);
            if (poll == null) continue;
            var tally = ChatMedia.TallyVotes(all, poll);
            pollMsg.PollVoteCounts = tally?.OptionCounts;
        }
    }

    private async Task PollTickAsync()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            await PollInboxAsync();
            var room = SelectedGroup;
            if (room == null) return;

            // Global chat should not keep polling a newer 60-message window while the
            // user is reading older history. A follow state is explicit in the view,
            // so the background refresh must stop as soon as the user scrolls up.
            if (!IsFollowing)
            {
                _polling = 0;
                return;
            }

            var list = await _core.FetchMessagesAsync(room.Code, RecentWindowSizeFor(room), 0, CancellationToken.None);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var known = new HashSet<string>(_messages.Select(m => m.Id), StringComparer.Ordinal);
                var fresh = new List<Message>();
                foreach (var m in list.OrderBy(x => x.Timestamp))
                {
                    if (IsRelay(m.Text, room) || !known.Add(m.Id)) continue;
                    m.IsStarred = ChatPrefsStore.Current.IsStarred(m.GroupCode + "|" + m.Id);
                    if (IsVoteMessage(m.Text))
                    {
                        var existing = _pollVotes.FindIndex(v => string.Equals(v.Id, m.Id, StringComparison.Ordinal));
                        if (existing >= 0) { _pollVotes[existing] = m; continue; }
                        _pollVotes.Add(m);
                    }
                    else
                    {
                        _messages.Add(m);
                        fresh.Add(m);
                    }
                }
                if (fresh.Count > 0)
                {
                    // Resolve previews for the rows that just arrived, not the whole backlog.
                    await _core.HydrateReplyPreviewsAsync(fresh, room.Code, CancellationToken.None);
                    StatusText = $"{_messages.Count} messages · #{room.Code}";
                    RequestScroll();
                    UpdateActivePolls();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Chat poll failed");
        }
        finally { _polling = 0; }
    }

    private async Task PollInboxAsync()
    {
        if (!_core.IsAuthenticated) return;
        try
        {
            var invited = await _core.PollInboxForInvitesAsync(_inboxSince, CancellationToken.None);
            foreach (var g in invited)
            {
                if (!_seenInvites.Add(g.Code)) continue;
                _inboxSince = DateTimeOffset.UtcNow;
                if (!Groups.Any(x => string.Equals(x?.Code, g.Code, StringComparison.Ordinal)))
                {
                    Groups.Add(g);
                    StatusText = (g.Creator ?? "Someone") + " started a DM — now in the Rooms list.";
                }
                RoomRegistry.AddRoom(g);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Inbox poll failed");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer.Dispose();
        _core.MessageReceived -= OnMessageReceived;
        RoomRegistry.ActiveRoomChanged -= OnActiveRoomChanged;
        GC.SuppressFinalize(this);
    }
}
