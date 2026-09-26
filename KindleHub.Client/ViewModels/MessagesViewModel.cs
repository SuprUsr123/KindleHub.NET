using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
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
    public static readonly string[] QuickReactions = { "+1", "-1", "lol", "?", "!", "<3", "♥", "★", "✓", "☺" };
    /// <summary>Instance mirror for XAML ItemsSource binding.</summary>
    public List<string> QuickReactionsList => QuickReactions.ToList();
    public static readonly string[] MsgReportReasons = { "Spam", "Harassment", "Hate speech", "Sexual content", "Threats / violence", "Misinformation", "Other" };
    public static readonly string[] NameReportReasons = { "Offensive / slur", "Sexual", "Harassment", "Impersonation", "Spam / ad", "Possible hacking / account-security issue", "Other" };

    private readonly KindleHubCore _core;
    private readonly ILogger<MessagesViewModel> _logger;
    private readonly Timer _pollTimer;
    private readonly HashSet<string> _seenInvites = new(StringComparer.Ordinal);
    private DateTimeOffset _inboxSince = DateTimeOffset.UtcNow;
    private Control? _host;
    private int _polling;
    private bool _disposed;

    private ObservableCollection<Group> _groups = new();
    private Group? _selectedGroup;
    private ObservableCollection<Message> _messages = new();
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
                IsGlobalChat = value?.Code == KindleHubCore.GlobalGroupCode;
                OnPropertyChanged(nameof(IsGlobalChat));
                _ = ReloadRoomAsync();
            }
        }
    }

    public bool IsGlobalChat { get; private set; }
    public bool IsChatting => _selectedGroup != null;

    /// <summary>Raised after an update; the view scrolls the message list to the newest row.</summary>
    public event EventHandler? ScrollRequested;
    public void RequestScroll() => ScrollRequested?.Invoke(this, EventArgs.Empty);

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

        Groups.Add(new Group { Code = KindleHubCore.GlobalGroupCode, Name = "Global Chat", Creator = "KindleHub" });
        foreach (var g in RoomRegistry.Rooms)
        {
            if (!string.IsNullOrEmpty(g?.Code) && !Groups.Any(x => string.Equals(x?.Code, g!.Code, StringComparison.Ordinal)))
                Groups.Add(g);
        }

        RoomRegistry.ActiveRoomChanged += OnActiveRoomChanged;
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
            var list = await _core.FetchMessagesAsync(room.Code, 120, 0, CancellationToken.None);
            ApplyStars(list);
            await _core.HydrateReplyPreviewsAsync(list, room.Code, CancellationToken.None);
            _messages.Clear();
            foreach (var m in list.Where(m => !IsRelay(m.Text, room)).OrderBy(m => m.Timestamp)) _messages.Add(m);
            StatusText = $"{_messages.Count} messages · #{room.Code}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Load chat failed");
            StatusText = "Couldn't load this room yet.";
        }
        finally
        {
            IsLoading = false;
            RequestScroll(); // scroll to the newest message at the bottom
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
            RoomRegistry.AddRoom(room);
            if (ReplyCompose) { ReplyCompose = false; ReplyTargetLabel = ""; OnPropertyChanged(nameof(ReplyTargetLabel)); }
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            _logger.LogInformation(ex, "Send failed");
            NewMessageText = text;
        }
        finally { IsSending = false; RequestScroll(); }
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
        var list = await _core.FetchMessagesAsync(room.Code, 120, 0, CancellationToken.None);
        ApplyStars(list);
        await _core.HydrateReplyPreviewsAsync(list, room.Code, CancellationToken.None);
        _messages.Clear();
        foreach (var row in list.Where(r => !IsRelay(r.Text, room)).OrderBy(r => r.Timestamp)) _messages.Add(row);
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
        finally { IsSending = false; RequestScroll(); }
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

    private static bool IsRelay(string? text, Group? room)
    {
        if (room?.Code is not { Length: > 0 } code) return false;
        if (!(code.StartsWith("900000", StringComparison.Ordinal) || code.StartsWith("80000077", StringComparison.Ordinal)))
            return false;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        return s.Length > 2 && s[0] == '{' && s[^1] == '}' && s.Contains("\"type\"", StringComparison.Ordinal);
    }

    private async Task PollTickAsync()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            await PollInboxAsync();
            var room = SelectedGroup;
            if (room == null) return;
            var list = await _core.FetchMessagesAsync(room.Code, 40, 0, CancellationToken.None);
            var known = new HashSet<string>(_messages.Select(m => m.Id), StringComparer.Ordinal);
            var added = false;
            foreach (var m in list.OrderBy(x => x.Timestamp))
            {
                if (IsRelay(m.Text, room) || !known.Add(m.Id)) continue;
                m.IsStarred = ChatPrefsStore.Current.IsStarred(m.GroupCode + "|" + m.Id);
                _messages.Add(m);
                added = true;
            }
            if (added)
            {
                await _core.HydrateReplyPreviewsAsync(_messages.ToList(), room.Code, CancellationToken.None);
                StatusText = $"{_messages.Count} messages · #{room.Code}";
                RequestScroll();
            }
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
        RoomRegistry.ActiveRoomChanged -= OnActiveRoomChanged;
        GC.SuppressFinalize(this);
    }
}
