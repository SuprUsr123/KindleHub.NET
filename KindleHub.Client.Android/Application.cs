using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using KindleHubApp = global::KindleHub.Client.App;

namespace KindleHub.Client.Android;

[global::Android.App.Application]
public class Application : AvaloniaAndroidApplication<KindleHubApp>
{
    protected Application(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}
