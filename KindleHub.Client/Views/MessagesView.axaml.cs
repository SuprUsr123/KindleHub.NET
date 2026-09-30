using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    public MessagesView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            if (DataContext is not MessagesViewModel vm) return;
            vm.ScrollRequested -= OnScrollRequested;
            vm.ForceScrollRequested -= OnForceScrollRequested;
            ChatScroll.ScrollChanged -= ChatScroll_ScrollChanged;
            ChatScroll.PointerWheelChanged -= ChatScroll_PointerWheelChanged;
        };
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MessagesViewModel vm) return;
        vm.AttachHostControl(this);
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
        else
            _missed++;
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

    /// <summary>Tap a bubble to open the official per-message action bar. Tap again to close it.</summary>
    private void Bubble_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 1) return;
        if (sender is Control { DataContext: Message msg } && DataContext is MessagesViewModel vm)
        {
            vm.SelectMessageCommand.Execute(msg);
            e.Handled = true;
        }
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
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Attach a photo",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.gif", "*.webp" } }
            }
        });
        if (files.Count == 0) return;
        try
        {
            var path = files[0].Path.LocalPath;
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
            var text = await clipboard.GetTextAsync();
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
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an app's HTML file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("HTML file") { Patterns = new[] { "*.html", "*.htm" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } }
            }
        });
        if (files.Count == 0) return;
        try
        {
            var path = files[0].Path.LocalPath;
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
