using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KindleHub.Client.ViewModels;

namespace KindleHub.Client.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>Enter in the display-name box commits immediately (cancels the keystroke-debounce).</summary>
    private void ProfileName_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not SettingsViewModel vm) return;
        e.Handled = true;
        vm.ApplyProfileNameCommand.Execute(null);
    }

    private void FontPick_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string label } && DataContext is SettingsViewModel vm)
            vm.FontSizeName = label;
    }

    private void ThemePick_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string label } && DataContext is SettingsViewModel vm)
            vm.ThemeName = label;
    }
}
