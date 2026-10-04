using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Threading.Tasks;
using KindleHub.Client.ViewModels;

namespace KindleHub.Client;

public partial class MainWindow : Window
{
    private static readonly Key[] _keyHistory =
    {
        Key.Up, Key.Up, Key.Down, Key.Down, Key.Left, Key.Right, Key.Left, Key.Right, Key.B, Key.A
    };
    private int _konamiPosition;

    public MainWindow()
    {
        InitializeComponent();
        FontSizeScaler.Attach(this);
        AddHandler(KeyDownEvent, HandleKonamiCode, RoutingStrategies.Tunnel);
    }

    private async void HandleKonamiCode(object? sender, KeyEventArgs e)
    {
        if (e.Key == _keyHistory[_konamiPosition])
            _konamiPosition++;
        else
            _konamiPosition = e.Key == _keyHistory[0] ? 1 : 0;

        if (_konamiPosition != _keyHistory.Length) return;
        _konamiPosition = 0;
        await ShowOneOfThoseMessagesAsync(string.Concat("Woah! You just discovered ", "a new cheat!"));
        await ShowOneOfThoseMessagesAsync(string.Concat("what the hell does it even do ", "here anyways?"));
        SettingsViewModel.NoteFound("TheOldCode");
    }

    private static Task ShowOneOfThoseMessagesAsync(string message)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            var body = new StackPanel { Margin = new Thickness(20), Spacing = 18, VerticalAlignment = VerticalAlignment.Center };
            body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            var ok = new Button { Content = "OK", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right };
            body.Children.Add(ok);
            var window = new Window { Title = "KindleHub Pro", Width = 420, Height = 170, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = body };
            ok.Click += (_, _) => window.Close();
            window.Closed += (_, _) => done.TrySetResult();
            window.Show();
        });
        return done.Task;
    }
}
