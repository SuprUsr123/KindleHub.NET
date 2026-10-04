using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using KindleHub.Client.ViewModels;
using Avalonia.Platform.Storage;

namespace KindleHub.Client.Views;

public partial class CommunityView : UserControl
{
    private CommunityViewModel? _attachedViewModel;

    public CommunityView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachEditorCommand();
        Loaded += (_, _) => AttachEditorCommand();
    }

    private void AttachEditorCommand()
    {
        if (_attachedViewModel != null) _attachedViewModel.OpenFlipbookEditorRequested -= OpenFlipbookEditor;
        _attachedViewModel = DataContext as CommunityViewModel;
        if (_attachedViewModel != null) _attachedViewModel.OpenFlipbookEditorRequested += OpenFlipbookEditor;
    }

    private void OpenFlipbookEditor()
    {
        if (DataContext is not CommunityViewModel vm) return;
        var editorVm = new FlipbookEditorViewModel
        {
            SaveToCommunity = wire => _ = vm.SaveFlipbookAsync(wire)
        };
        var view = new FlipbookEditorView { DataContext = editorVm };
        var window = new Window
        {
            Title = "Create flipbook",
            Width = 540,
            Height = 820,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = view
        };
        editorVm.CloseWindow = window.Close;
        window.Show();
    }

    private async void Topic_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is CommunityViewModel vm && vm.SelectedTopic != null)
        {
            await vm.OpenSelectedAsync();
        }
    }

    private void SaveMeme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not CommunityViewModel vm) return;
        try
        {
            var width = Math.Max(1, (int)Math.Ceiling(MemePreview.Bounds.Width));
            var height = Math.Max(1, (int)Math.Ceiling(MemePreview.Bounds.Height));
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "KindleHub", "Memes");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"meme-{DateTime.Now:yyyyMMdd-HHmmss}.png");

            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
            bitmap.Render(MemePreview);
            bitmap.Save(path, PngBitmapEncoderOptions.Default);
            vm.StatusText = $"Meme saved to {path}";
        }
        catch (Exception)
        {
            vm.StatusText = "Couldn't save the meme. Check that your Pictures folder is writable.";
        }
    }

    private async void UseMemePicture_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not CommunityViewModel vm) return;
        try
        {
            if (OperatingSystem.IsLinux() && LinuxFilePicker.IsAvailable)
            {
                var path = await LinuxFilePicker.OpenAsync("Choose a meme picture", "Images", "*.jpg", "*.jpeg", "*.png", "*.gif", "*.webp", "*.bmp");
                if (path is null) return;
                using var stream = File.OpenRead(path);
                vm.UseMemePicture(new Bitmap(stream));
                return;
            }

            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null) return;
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a meme picture",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Images") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.gif", "*.webp", "*.bmp" } }
                }
            });
            if (files.Count == 0) return;
            await using var imageStream = await files[0].OpenReadAsync();
            vm.UseMemePicture(new Bitmap(imageStream));
        }
        catch (Exception)
        {
            vm.StatusText = "Couldn't open that picture. Choose a PNG, JPEG, GIF, WebP, or BMP image.";
        }
    }

    private void ClearMemePicture_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is CommunityViewModel vm) vm.ClearMemePicture();
    }
}
