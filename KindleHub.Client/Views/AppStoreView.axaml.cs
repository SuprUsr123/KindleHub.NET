using System;
using System.IO;
using Avalonia.Controls;
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
        if (sender is Button { DataContext: AppCatalog app } && DataContext is AppStoreViewModel vm)
        {
            vm.SelectedApp = app;
            await vm.DownloadAndOpenAsync();
        }
    }

    private async void ChooseHtml_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AppStoreViewModel vm) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the app's HTML file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("HTML file") { Patterns = new[] { "*.html", "*.htm" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } }
            }
        });
        if (files.Count == 0) return;
        var path = files[0].Path.LocalPath;
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
