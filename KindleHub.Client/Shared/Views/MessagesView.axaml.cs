using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KindleHub.Client.ViewModels;
using KindleHub.Core;

namespace KindleHub.Client.Views;

public partial class MessagesView : UserControl
{
    /// <summary>How close to the bottom still counts as "at the present".
    /// Deliberately tight: mainstream clients (Discord included) only stay stuck
    /// while you are essentially at the bottom, so a single flick upward breaks
    /// the anchor. A loose threshold is what makes a chat feel like it is
    /// fighting you.</summary>
    private const double StickThreshold = 16;

    /// <summary>True while the reader is at the bottom, so new messages should scroll in.</summary>
    private bool _following = true;

    /// <summary>Messages that arrived while the reader was scrolled away.</summary>
    private int _missed;

    /// <summary>Set while we move the ScrollViewer ourselves, so our own layout
    /// passes don't get mistaken for the reader scrolling.</summary>
    private bool _selfScrolling;
    private bool _mobileLayout;
    private bool _mobileShowChannels = true;
    private readonly DispatcherTimer _messageHoldTimer;
    private Message? _pressedMessage;
    private Control? _pressedMessageBubble;
    private Point _messagePressPoint;
    private long? _messagePressPointerId;

    public MessagesView()
    {
        InitializeComponent();
        _messageHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        _messageHoldTimer.Tick += MessageHoldTimer_Tick;
        ChatScroll.AddHandler(InputElement.PointerPressedEvent, ChatScroll_PointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        ChatScroll.AddHandler(InputElement.PointerMovedEvent, ChatScroll_PointerMoved,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        ChatScroll.AddHandler(InputElement.PointerReleasedEvent, ChatScroll_PointerReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        ChatScroll.AddHandler(InputElement.ContextRequestedEvent, ChatScroll_ContextRequested,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        SizeChanged += (_, args) => ConfigureLayout(args.NewSize.Width);
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            if (DataContext is MessagesViewModel vm)
            {
                vm.ScrollRequested -= OnScrollRequested;
                vm.ForceScrollRequested -= OnForceScrollRequested;
                vm.PropertyChanged -= ViewModel_PropertyChanged;
            }
            ChatScroll.ScrollChanged -= ChatScroll_ScrollChanged;
            ChatScroll.PointerWheelChanged -= ChatScroll_PointerWheelChanged;
            CancelMessageHold();
        };
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MessagesViewModel vm) return;
        vm.AttachHostControl(this);
        vm.PropertyChanged -= ViewModel_PropertyChanged;
        vm.PropertyChanged += ViewModel_PropertyChanged;
        if (Bounds.Width < 760 && vm.SelectedGroup is not null)
            _mobileShowChannels = false;
        ConfigureLayout(Bounds.Width);
        vm.ScrollRequested -= OnScrollRequested;
        vm.ScrollRequested += OnScrollRequested;
        vm.ForceScrollRequested -= OnForceScrollRequested;
        vm.ForceScrollRequested += OnForceScrollRequested;
        ChatScroll.ScrollChanged += ChatScroll_ScrollChanged;
        // Latch autoscroll off on any upward scroll intent, straight from the
        // input event. This does not depend on the ScrollViewer's reported
        // Extent/Viewport, so it holds even while a scroll is in flight.
        ChatScroll.PointerWheelChanged += ChatScroll_PointerWheelChanged;
        _following = true;
        vm.SetFollowState(true);
        _missed = 0;
        UpdateJumpButton();
        JumpToBottom(force: true);
        UpdateMessageActionPresentation(vm);
    }

    private void ConfigureLayout(double width)
    {
        _mobileLayout = width < 760;
        MessagesRoot.Margin = _mobileLayout ? new Thickness(4) : new Thickness(12);
        MessagesTitle.IsVisible = !_mobileLayout;
        MessagesLayout.ColumnDefinitions.Clear();
        MessagesLayout.RowDefinitions.Clear();

        if (_mobileLayout)
        {
            ChatHeaderLayout.ColumnDefinitions.Clear();
            ChatHeaderLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            ChatHeaderLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            ChatHeaderLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            ChatHeaderLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            ChatHeaderLayout.RowDefinitions.Clear();
            ChatHeaderLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            ChatHeaderLayout.ColumnSpacing = 4;
            Grid.SetColumn(ShowChannelsButton, 0);
            Grid.SetColumn(ChatHeaderInfo, 1);
            Grid.SetColumn(HeaderProgress, 2);
            Grid.SetColumn(RefreshMessagesButton, 3);
            ChannelIcon.IsVisible = false;
            RefreshMessagesButton.Content = "↻";
            RefreshMessagesButton.MinWidth = 44;
            RefreshMessagesButton.Padding = new Thickness(4, 0);

            MessagesLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            MessagesLayout.RowDefinitions.Add(new RowDefinition(_mobileShowChannels ? GridLength.Star : new GridLength(0)));
            MessagesLayout.RowDefinitions.Add(new RowDefinition(_mobileShowChannels ? new GridLength(0) : GridLength.Star));
            MessagesLayout.ColumnSpacing = 0;
            Grid.SetColumn(ChannelPanel, 0);
            Grid.SetRow(ChannelPanel, 0);
            Grid.SetColumn(ChatPanel, 0);
            Grid.SetRow(ChatPanel, 1);
            ShowChannelsButton.IsVisible = true;
            ApplyMobilePanelVisibility();
        }
        else
        {
            ChatHeaderLayout.ColumnDefinitions.Clear();
            ChatHeaderLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            ChatHeaderLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            ChatHeaderLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            ChatHeaderLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            ChatHeaderLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            ChatHeaderLayout.RowDefinitions.Clear();
            ChatHeaderLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            ChatHeaderLayout.ColumnSpacing = 8;
            Grid.SetColumn(ShowChannelsButton, 0);
            Grid.SetColumn(ChannelIcon, 1);
            Grid.SetColumn(ChatHeaderInfo, 2);
            Grid.SetColumn(HeaderProgress, 3);
            Grid.SetColumn(RefreshMessagesButton, 4);
            ChannelIcon.IsVisible = true;
            RefreshMessagesButton.Content = "Refresh";
            RefreshMessagesButton.ClearValue(TemplatedControl.MinWidthProperty);
            RefreshMessagesButton.ClearValue(TemplatedControl.PaddingProperty);

            MessagesLayout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(250)));
            MessagesLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            MessagesLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            MessagesLayout.ColumnSpacing = 12;
            Grid.SetColumn(ChannelPanel, 0);
            Grid.SetRow(ChannelPanel, 0);
            Grid.SetColumn(ChatPanel, 1);
            Grid.SetRow(ChatPanel, 0);
            ChannelPanel.IsVisible = true;
            ChatPanel.IsVisible = true;
            ShowChannelsButton.IsVisible = false;
        }

        if (DataContext is MessagesViewModel vm)
            UpdateMessageActionPresentation(vm);
    }

    private void ApplyMobilePanelVisibility()
    {
        ChannelPanel.IsVisible = _mobileShowChannels;
        ChatPanel.IsVisible = !_mobileShowChannels;
        if (_mobileLayout && MessagesLayout.RowDefinitions.Count == 2)
        {
            MessagesLayout.RowDefinitions[0].Height = _mobileShowChannels ? GridLength.Star : new GridLength(0);
            MessagesLayout.RowDefinitions[1].Height = _mobileShowChannels ? new GridLength(0) : GridLength.Star;
        }
    }

    private void ShowChannels_Click(object? sender, RoutedEventArgs e)
    {
        _mobileShowChannels = true;
        ApplyMobilePanelVisibility();
    }

    private void OpenGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Group group } || DataContext is not MessagesViewModel vm)
            return;

        vm.SelectedGroup = group;
        if (_mobileLayout)
        {
            _mobileShowChannels = false;
            ApplyMobilePanelVisibility();
        }
    }

    /// <summary>New content arrived. This follows the official client’s bottom-pin
    /// rule: if the reader is still at the bottom, the new message snaps the view
    /// to the end. Otherwise, leave the viewport alone and count it as missed.
    /// </summary>
    private void OnScrollRequested(object? sender, EventArgs e)
    {
        if (_following)
        {
            _following = true;
            _missed = 0;
            Dispatcher.UIThread.Post(() =>
            {
                if (ChatScroll is null) return;

                ChatScroll.ScrollToEnd();
                var max = Math.Max(0, ChatScroll.Extent.Height - ChatScroll.Viewport.Height);
                ChatScroll.Offset = new Avalonia.Vector(ChatScroll.Offset.X, max);

                _following = true;
                _missed = 0;
                UpdateJumpButton();
            }, DispatcherPriority.Render);
        }
        else
        {
            _missed++;
            UpdateJumpButton();
        }
    }

    /// <summary>The reader did something that should reveal the newest row.</summary>
    private void OnForceScrollRequested(object? sender, EventArgs e)
    {
        _following = true;
        if (DataContext is MessagesViewModel vm)
            vm.SetFollowState(true);
        _missed = 0;
        JumpToBottom(force: true);
    }

    /// <summary>Any upward wheel/trackpad gesture stops autoscroll immediately.
    /// Scrolling back down to the very bottom re-enables it.</summary>
    private void ChatScroll_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y >= 0) return;          // downward / horizontal: leave state alone
        _following = false;
        if (DataContext is MessagesViewModel vm)
            vm.SetFollowState(false);
        UpdateJumpButton();
    }

    private void ChatScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var viewModel = DataContext as MessagesViewModel;

        // Content can grow after an append (reply previews/images are hydrated
        // asynchronously). Preserve the bottom anchor across that layout change.
        if (!_selfScrolling && _following && e.ExtentDelta.Y > 0)
        {
            _missed = 0;
            JumpToBottom(force: true);
            return;
        }

        if (_selfScrolling)
        {
            if (IsAtBottom)
            {
                _following = true;
                viewModel?.SetFollowState(true);
                _missed = 0;
            }
            UpdateJumpButton();
            return;
        }

        var atBottom = IsAtBottom;
        _following = atBottom;
        viewModel?.SetFollowState(atBottom);
        if (atBottom)
            _missed = 0;
        UpdateJumpButton();
    }

    private bool IsAtBottom =>
        ChatScroll is not null &&
        ChatScroll.Extent.Height - (ChatScroll.Offset.Y + ChatScroll.Viewport.Height) <= StickThreshold;

    /// <summary>Request a jump to the newest message.
    /// This mirrors the official client’s behavior: if the reader is still
    /// following, snap the viewport to the end of the transcript.
    /// </summary>
    /// <param name="force">True for reader-initiated jumps (opening a room, sending,
    /// pressing "jump to present"), which re-anchor to the newest row regardless of
    /// where the viewport currently is.</param>
    private void JumpToBottom(bool force = false)
    {
        if (ChatScroll is null) return;

        if (force)
        {
            _following = true;
            _missed = 0;
        }

        var chase = force || IsAtBottom;
        if (!chase)
        {
            UpdateJumpButton();
            return;
        }

        _selfScrolling = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (ChatScroll is null)
            {
                _selfScrolling = false;
                return;
            }

            ChatScroll.ScrollToEnd();
            var max = Math.Max(0, ChatScroll.Extent.Height - ChatScroll.Viewport.Height);
            ChatScroll.Offset = new Avalonia.Vector(ChatScroll.Offset.X, max);

            _selfScrolling = false;
            _following = true;
            _missed = 0;
            UpdateJumpButton();
        }, DispatcherPriority.Render);
    }

    private string _lastJumpLabel = "";

    private void UpdateJumpButton()
    {
        if (JumpLatest is null) return;
        JumpLatest.IsVisible = !_following;

        // ScrollChanged fires on every pixel of a drag, so only touch the label
        // when the text actually differs — otherwise every frame re-measures and
        // re-renders the pill, which shows up as flicker while scrolling.
        var label = _missed > 0
            ? $"{_missed} new message{(_missed == 1 ? "" : "s")} — jump to present"
            : "Jump to present";
        if (label == _lastJumpLabel) return;
        _lastJumpLabel = label;
        JumpLatestLabel.Text = label;
    }

    private void JumpLatest_Click(object? sender, RoutedEventArgs e)
    {
        _following = true;
        if (DataContext is MessagesViewModel vm)
            vm.SetFollowState(true);
        _missed = 0;
        JumpToBottom(force: true);
    }

    /// <summary>Open an app shared into the chat. The HTML is inline in the
    /// message, so this writes it out and launches the default browser.</summary>
    private void AppShare_Click(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MessagesViewModel vm) return;
        var msg = FindMessage(sender);
        if (msg == null) return;
        e.Handled = true;
        _ = vm.OpenAppShareAsync(msg);
    }

    /// <summary>Start a short stationary-press timer for touch and mouse. Pointer motion
    /// cancels it, leaving vertical swipes to the chat scroller.</summary>
    private void ChatScroll_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MessagesViewModel || FindMessageBubble(e.Source) is not { } message)
            return;

        var point = e.GetCurrentPoint(this);
        if (e.Pointer.Type != PointerType.Touch && !point.Properties.IsLeftButtonPressed)
            return;

        CancelMessageHold();
        _pressedMessage = message;
        _pressedMessageBubble = FindMessageBubbleControl(e.Source);
        _messagePressPoint = point.Position;
        _messagePressPointerId = e.Pointer.Id;
        _messageHoldTimer.Start();
    }

    private void ChatScroll_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedMessage is null || _messagePressPointerId != e.Pointer.Id)
            return;

        var position = e.GetPosition(this);
        var delta = position - _messagePressPoint;
        if (Math.Abs(delta.X) > 14 || Math.Abs(delta.Y) > 14)
            CancelMessageHold();
    }

    private void ChatScroll_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_messagePressPointerId == e.Pointer.Id)
            CancelMessageHold();
    }

    private static Control? FindMessageBubbleControl(object? source)
    {
        for (var control = source as Control; control is not null; control = control.GetVisualParent() as Control)
        {
            if (control is Border border && border.Classes.Contains("chat-message"))
                return control;
        }

        return null;
    }

    private static Message? FindMessageBubble(object? source) =>
        FindMessageBubbleControl(source)?.DataContext as Message;

    private void ChatScroll_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (FindMessageBubbleControl(e.Source) is not { DataContext: Message message } bubble)
            return;

        e.Handled = true;
        OpenMessageActions(message, bubble);
    }

    private void MessageHoldTimer_Tick(object? sender, EventArgs e)
    {
        _messageHoldTimer.Stop();
        if (_pressedMessage is { } message && _pressedMessageBubble is { } bubble)
            OpenMessageActions(message, bubble);
    }

    private void OpenMessageActions(Message message, Control bubble)
    {
        if (DataContext is not MessagesViewModel vm) return;
        vm.SelectMessageCommand.Execute(message);
        UpdateMessageActionPresentation(vm);
        CancelMessageHold();
    }

    private void MessageAction_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MessagesViewModel vm)
            vm.DeselectCommand.Execute(null);
        MobileMessageActionSheet.IsVisible = false;
        SelectedMessageActionBar.IsVisible = false;
    }

    private void DismissMessageActions_Click(object? sender, RoutedEventArgs e) => MessageAction_Click(sender, e);

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MessagesViewModel.ActiveMessage) or nameof(MessagesViewModel.HasActiveMessage)
            && sender is MessagesViewModel vm)
            UpdateMessageActionPresentation(vm);
    }

    private void UpdateMessageActionPresentation(MessagesViewModel vm)
    {
        var show = vm.HasActiveMessage;
        SelectedMessageActionBar.IsVisible = show && !_mobileLayout;
        MobileMessageActionSheet.IsVisible = show && _mobileLayout;
        if (_mobileLayout)
            MobileMessageActionPanel.MaxHeight = Math.Max(320, Bounds.Height * 0.78);
    }

    private void CancelMessageHold()
    {
        _messageHoldTimer.Stop();
        _pressedMessage = null;
        _pressedMessageBubble = null;
        _messagePressPointerId = null;
    }

    private void ReasonChip_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string reason } && DataContext is MessagesViewModel vm)
            vm.ReportReason = reason;
    }

    private void Composer_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            if (DataContext is MessagesViewModel vm) _ = vm.SendMessageAsync();
        }
    }

    private void JoinBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MessagesViewModel vm) return;
        e.Handled = true;
        _ = vm.JoinByCodeAsync();
    }

    private async void Attach_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MessagesViewModel vm) return;
        string? path;
        if (OperatingSystem.IsLinux() && LinuxFilePicker.IsAvailable)
        {
            path = await LinuxFilePicker.OpenAsync("Attach a photo", "Images", "*.jpg", "*.jpeg", "*.png", "*.gif", "*.webp");
            if (path is null) return;
        }
        else
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null) return;
            IReadOnlyList<IStorageFile> files;
            try
            {
                files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Attach a photo",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Images") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.gif", "*.webp" } }
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[KindleHub Debug] Open image picker failed: {ex}");
                vm.StatusText = "Couldn't open the file picker.";
                return;
            }
            if (files.Count == 0) return;
            path = files[0].Path.LocalPath;
        }
        try
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(path);
            await vm.SendImageDataAsync(path, bytes);
        }
        catch { vm.StatusText = "Couldn't read that image."; }
    }

    /// <summary>Pull HTML off the clipboard into the app-share composer, for the
    /// cases where the author has it copied but not saved as a file.</summary>
    private async void PasteAppHtml_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MessagesViewModel vm) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) { vm.StatusText = "Couldn't reach the clipboard."; return; }
        try
        {
            var text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) { vm.StatusText = "The clipboard doesn't contain any text."; return; }
            vm.SetAppHtmlText(text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KindleHub Debug] PasteAppHtml_Click failed: {ex}");
            vm.StatusText = "Couldn't read the clipboard.";
        }
    }

    /// <summary>Load an HTML file into the app-share composer, so an app can be
    /// shared from a file as easily as from pasted text.</summary>
    private async void ShareAppFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MessagesViewModel vm) return;
        string? path;
        if (OperatingSystem.IsLinux() && LinuxFilePicker.IsAvailable)
        {
            path = await LinuxFilePicker.OpenAsync("Choose an app's HTML file", "HTML files", "*.html", "*.htm");
            if (path is null) return;
        }
        else
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null) return;
            IReadOnlyList<IStorageFile> files;
            try
            {
                files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Choose an app's HTML file",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("HTML file") { Patterns = new[] { "*.html", "*.htm" } },
                        new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[KindleHub Debug] Open app-share picker failed: {ex}");
                vm.StatusText = "Couldn't open the file picker.";
                return;
            }
            if (files.Count == 0) return;
            path = files[0].Path.LocalPath;
        }
        try
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(path);
            await vm.LoadAppHtmlFileAsync(path, bytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KindleHub Debug] ShareAppFile_Click failed: {ex}");
            vm.StatusText = "Couldn't read that HTML file.";
        }
    }

    private void More_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MessagesViewModel vm) return;
        vm.ToggleMediaSendPanel();
    }

    /// <summary>Open the standalone flipbook drawing window. "Send to chat" in the
    /// editor hands the KHFLIP1 wire back, which lands in the composer's frame list.</summary>
    private void OpenFlipbookEditor_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MessagesViewModel vm) return;
        var editorVm = new FlipbookEditorViewModel();
        editorVm.SendFlipbook = wire => vm.AdoptFlipbookWire(wire);

        var view = new FlipbookEditorView { DataContext = editorVm };
        var window = new Window
        {
            Title = "Flipbook",
            Width = 520,
            Height = 820,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = view
        };
        editorVm.CloseWindow = window.Close;
        window.Show();
    }

    private void AddStoryLine_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MessagesViewModel vm) vm.AddStoryLine();
    }

    /// <summary>Chat rows that wrap media share a common ancestor, so the message
    /// is dug out by walking up until a DataContext of type Message appears.</summary>
    private static Message? FindMessage(object? sender)
    {
        if (sender is not Avalonia.Visual v) return null;
        for (var node = v; node != null; node = node.GetVisualParent())
            if (node is Control { DataContext: Message m }) return m;
        return null;
    }

    /// <summary>"Click here to view the flipbook" — renders the drawing to an
    /// animated GIF and opens it in the desktop's default viewer.</summary>
    private void ViewFlipbook_Click(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MessagesViewModel vm) return;
        var msg = FindMessage(sender);
        if (msg == null) return;
        e.Handled = true;
        vm.OpenFlipbook(msg);
    }

    /// <summary>Opens the story log in its own window.</summary>
    private void ViewStory_Click(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MessagesViewModel vm) return;
        var msg = FindMessage(sender);
        if (msg == null) return;
        e.Handled = true;
        var story = vm.GetStory(msg);
        if (story == null) { vm.StatusText = "That story couldn't be read."; return; }
        var window = new StoryViewerWindow();
        window.LoadStory(story);
        window.Show();
    }

    private void PollOption_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Border { DataContext: string optionText }) return;
        if (DataContext is not MessagesViewModel vm) return;
        _ = vm.VotePollAsync(optionText);
    }
}
