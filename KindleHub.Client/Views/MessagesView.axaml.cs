using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using KindleHub.Client.ViewModels;
using KindleHub.Core;

namespace KindleHub.Client.Views;

public partial class MessagesView : UserControl
{
    public MessagesView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            if (DataContext is MessagesViewModel vm) vm.ScrollRequested -= OnScrollRequested;
        };
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MessagesViewModel vm)
        {
            vm.AttachHostControl(this);
            vm.ScrollRequested -= OnScrollRequested;
            vm.ScrollRequested += OnScrollRequested;
            ScrollToBottom();
        }
    }

    private void OnScrollRequested(object? sender, EventArgs e) => ScrollToBottom();

    private void ScrollToBottom()
    {
        // Defer to after the ItemsControl has measured its new rows.
        Dispatcher.UIThread.Post(() =>
        {
            if (ChatScroll is null) return;
            var max = Math.Max(0, ChatScroll.Extent.Height - ChatScroll.Viewport.Height);
            ChatScroll.Offset = new Avalonia.Vector(ChatScroll.Offset.X, max);
        }, DispatcherPriority.Background);
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
}
