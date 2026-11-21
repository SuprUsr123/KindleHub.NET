using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KindleHub.Core;
using Microsoft.Extensions.Logging;

namespace KindleHub.Client.ViewModels;

/// <summary>Which mailbox folder is on screen. The server returns received and sent
/// mail in one read, so the split is entirely a client-side view.</summary>
public enum MailFolder
{
    Inbox,
    Sent,
}

/// <summary>One row in the mail list, with the decrypted body ready to show.</summary>
public sealed class MailRow
    {
        public MailItem Item { get; }
        public string Id => Item.Id;
        public string Subject => string.IsNullOrEmpty(Item.Subject) ? "(no subject)" : Item.Subject;
        public string Who => IsOutbound ? $"To {Item.ToUser}" : $"From {Item.FromUser}";
        public string Preview => PreviewOf(Item.Body);
        public string Body => Item.Body;
        public string Timestamp => Item.TimestampFormatted;
        public bool IsSelected { get; set; }
        public bool IsOutbound { get; }
        public bool CanUnsend { get; }
        /// <summary>Mail you sent to your own address. The website lists those in
        /// both folders, so a note-to-self is never hidden away in Sent.</summary>
        public bool IsSelfSent { get; }

        public MailRow(MailItem item, string myUserId, string myUser)
        {
            Item = item;
            IsOutbound = !string.IsNullOrEmpty(item.FromId)
                         && string.Equals(item.FromId, myUserId, StringComparison.OrdinalIgnoreCase);
            CanUnsend = IsOutbound;
            var mine = KindleHubApiClient.NormalizeMailUser(myUser);
            var to = KindleHubApiClient.NormalizeMailUser(item.ToUser);
            IsSelfSent = IsOutbound && mine.Length > 0 && string.Equals(to, mine, StringComparison.Ordinal);
        }

    /// <summary>First line of the body, for the collapsed row.</summary>
    private static string PreviewOf(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var line = body.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        return line.Length <= 90 ? line : line[..89] + "…";
    }
}

/// <summary>
/// Mail: the KindleHub mailbox, backed by the same kh_mail table the website uses.
/// Bodies are E2E-encrypted per message exactly as the web client does, so mail
/// written here reads there and vice versa. Subject is plaintext by design, which
/// is what lets a list render with no keys.
/// </summary>
public class MailViewModel : ViewModelBase
{
    private readonly KindleHubCore _core;
    private readonly ILogger<MailViewModel> _logger;

    private ObservableCollection<MailRow> _messages = new();
    private MailRow? _selected;
    private MailFolder _folder = MailFolder.Inbox;
    private string _composeTo = "";
    private string _composeSubject = "";
    private string _composeBody = "";
    private string _composeReplyTo = "";
    private bool _composing;
    private bool _isLoading;
    private bool _isSending;
    private string _status = "";

    public ObservableCollection<MailRow> Messages { get => _messages; set => SetProperty(ref _messages, value); }
    public MailRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value)) OnPropertyChanged(nameof(HasSelection));
        }
    }

    /// <summary>Drives the reading pane's empty state from the list selection.</summary>
    public bool HasSelection => _selected != null;

    /// <summary>Folder switcher. The parameter is the folder name, so it binds
    /// straight to the two buttons without a converter.</summary>
    public RelayCommand<string> FolderCommand { get; }

    public MailFolder Folder
    {
        get => _folder;
        set
        {
            if (SetProperty(ref _folder, value)) Refilter();
        }
    }

    public bool IsComposing { get => _composing; set => SetProperty(ref _composing, value); }
    public bool NotComposing => !_composing;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
    public bool IsSending { get => _isSending; set => SetProperty(ref _isSending, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    public bool IsEmpty => _messages.Count == 0;
    public bool HasMail => _messages.Count > 0;
    public int InboxUnreadCount { get; private set; }

    public string ComposeTo { get => _composeTo; set { if (SetProperty(ref _composeTo, value)) SendCommand.RaiseCanExecuteChanged(); } }
    public string ComposeSubject { get => _composeSubject; set { if (SetProperty(ref _composeSubject, value)) SendCommand.RaiseCanExecuteChanged(); } }
    public string ComposeBody { get => _composeBody; set { if (SetProperty(ref _composeBody, value)) SendCommand.RaiseCanExecuteChanged(); } }
    public string ComposeReplyTo { get => _composeReplyTo; set => SetProperty(ref _composeReplyTo, value); }

    public bool CanSend => !_isSending
                           && !string.IsNullOrWhiteSpace(_composeTo)
                           && !string.IsNullOrWhiteSpace(_composeBody);

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ComposeCommand { get; }
    public RelayCommand CancelComposeCommand { get; }
    public RelayCommand SendCommand { get; }
    public RelayCommand<MailRow> ReplyCommand { get; }
    public RelayCommand<MailRow> UnsendCommand { get; }

    private List<MailRow> _all = new();

    public MailViewModel(KindleHubCore core, ILogger<MailViewModel> logger)
    {
        _core = core;
        _logger = logger;

        RefreshCommand = new RelayCommand(async () => await RefreshAsync());
        FolderCommand = new RelayCommand<string>(name =>
        {
            if (Enum.TryParse<MailFolder>(name, out var f)) Folder = f;
        });
        ComposeCommand = new RelayCommand(StartCompose);
        CancelComposeCommand = new RelayCommand(CancelCompose);
        SendCommand = new RelayCommand(async () => await SendAsync(), () => CanSend);
        ReplyCommand = new RelayCommand<MailRow>(StartReply);
        UnsendCommand = new RelayCommand<MailRow>(row => { if (row != null) _ = UnsendAsync(row); });

        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (!_core.IsAuthenticated)
        {
            Status = "Sign in to read your mail.";
            Refilter();
            return;
        }
        IsLoading = true;
        try
        {
            var items = await _core.FetchMailAsync(CancellationToken.None);
            var myId = _core.CurrentProfile?.UserId ?? "";
            var myUser = _core.CurrentProfile?.Email ?? "";
            _all = items.Select(i => new MailRow(i, myId, myUser)).ToList();
            Refilter();
            Status = _all.Count == 0
                ? "No mail yet. It always delivers between KindleHub accounts."
                : $"{_all.Count(r => !r.IsOutbound)} received, {_all.Count(r => r.IsOutbound)} sent.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mail refresh failed");
            Status = "Couldn't reach your mailbox.";
        }
        finally { IsLoading = false; }
    }

    /// <summary>Inbox shows what came in; Sent shows what you wrote. A note to
    /// yourself is in both, like the website, so it is never lost.</summary>
    private void Refilter()
    {
        Messages.Clear();
        foreach (var r in _all.Where(r => _folder == MailFolder.Inbox
                                            ? (!r.IsOutbound || r.IsSelfSent)
                                            : r.IsOutbound))
            Messages.Add(r);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasMail));
        OnPropertyChanged(nameof(InboxUnreadCount));
    }

    private void StartCompose()
    {
        ComposeTo = ""; ComposeSubject = ""; ComposeBody = ""; ComposeReplyTo = "";
        IsComposing = true;
        OnPropertyChanged(nameof(NotComposing));
        SendCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Reply pre-fills the original fields and keeps the thread id, which is
    /// how the website links a reply back to its parent.</summary>
    private void StartReply(MailRow? row)
    {
        if (row is null) return;
        ComposeTo = row.IsOutbound ? row.Item.ToUser : row.Item.FromUser;
        ComposeSubject = row.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            ? row.Subject
            : "Re: " + row.Subject;
        ComposeBody = row.IsOutbound ? "" : "\n\n--- on " + row.Timestamp + ", " + row.Who + " wrote ---\n" + row.Body;
        ComposeReplyTo = row.Item.ReplyTo;
        IsComposing = true;
        OnPropertyChanged(nameof(NotComposing));
        SendCommand.RaiseCanExecuteChanged();
    }

    private void CancelCompose()
    {
        IsComposing = false;
        OnPropertyChanged(nameof(NotComposing));
    }

    public void NotifyComposeChanged() => SendCommand.RaiseCanExecuteChanged();

    private async Task SendAsync()
    {
        if (!CanSend) return;
        IsSending = true;
        SendCommand.RaiseCanExecuteChanged();
        try
        {
            var sent = await _core.SendMailAsync(ComposeTo.Trim(), ComposeSubject.Trim(), ComposeBody ?? "", ComposeReplyTo, CancellationToken.None);
            var row = new MailRow(sent, _core.CurrentProfile?.UserId ?? "", _core.CurrentProfile?.Email ?? "");
            _all.Insert(0, row);
            CancelCompose();
            ComposeTo = ""; ComposeSubject = ""; ComposeBody = ""; ComposeReplyTo = "";
            /* Stay where the letter is: a note to yourself belongs in the Inbox,
               and the website never yanks you into Sent after a send. */
            if (!row.IsSelfSent) Folder = MailFolder.Sent;
            Refilter();
            Status = $"Sent to {sent.ToUser}.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mail send failed");
            Status = "Couldn't send that mail.";
        }
        finally
        {
            IsSending = false;
            SendCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task UnsendAsync(MailRow row)
    {
        try
        {
            if (!await _core.UnsendMailAsync(row.Id, CancellationToken.None))
            {
                Status = "Only the device that sent that mail can unsend it.";
                return;
            }
            _all.RemoveAll(r => r.Id == row.Id);
            Refilter();
            Status = "Mail unsent.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unsend failed");
            Status = "Couldn't unsend that mail.";
        }
    }
}
