using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KindleHub.Client.ViewModels;
using KindleHub.Core;

namespace KindleHub.Client.Views;

public partial class AppStoreView : UserControl
{
    public AppStoreView()
    {
        InitializeComponent();
    }

    private async void Download_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StoreAppItem app } && DataContext is AppStoreViewModel vm)
        {
            vm.SelectedApp = app;
            await vm.OpenOrDownloadAsync(app);
        }
    }

    private async void ChooseHtml_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AppStoreViewModel vm) return;
        string? path;
        if (OperatingSystem.IsLinux() && LinuxFilePicker.IsAvailable)
        {
            path = await LinuxFilePicker.OpenAsync("Choose the app's HTML file", "HTML files", "*.html", "*.htm");
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
                    Title = "Choose the app's HTML file",
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
                Console.WriteLine($"[KindleHub Debug] Open HTML picker failed: {ex}");
                vm.StatusText = "Couldn't open the file picker. Install kdialog, zenity, or yad.";
                return;
            }
            if (files.Count == 0) return;
            path = files[0].Path.LocalPath;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            await vm.SetPublishFileAsync(path, bytes);
        }
        catch (System.Exception ex)
        {
            Console.WriteLine($"[KindleHub Debug] ChooseHtml_Click failed: {ex}");
            vm.StatusText = "Couldn't read that file.";
        }
    }

    /// <summary>Publish straight from the clipboard — the HTML does not need to
    /// exist as a file. Uses the TopLevel clipboard rather than a static one so
    /// it works with whatever window has focus.</summary>
    private async void PasteHtml_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AppStoreViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        var clipboard = top?.Clipboard;
        if (clipboard is null) { vm.StatusText = "Couldn't reach the clipboard."; return; }
        try
        {
            var text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) { vm.StatusText = "The clipboard doesn't contain any text."; return; }
            vm.NotifyHtmlChanged();
            await vm.SetPublishHtmlTextAsync(text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KindleHub Debug] PasteHtml_Click failed: {ex}");
            vm.StatusText = "Couldn't read the clipboard.";
        }
    }

    private void ClearHtml_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AppStoreViewModel vm) vm.ClearPublishHtml();
    }

    private async void RunOwn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OwnAppItem item } && DataContext is AppStoreViewModel vm)
            await vm.RunOwnAppAsync(item);
    }

    private async void RemoveOwn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OwnAppItem item } && DataContext is AppStoreViewModel vm)
            await vm.RemoveOwnAppAsync(item);
    }
}
