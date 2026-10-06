using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace KindleHub.Client.Android;

[Activity(
    Label = "KindleHub Pro",
    Theme = "@style/KindleHubTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
