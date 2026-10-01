using Avalonia;
using Avalonia.X11;
using Avalonia.Wayland;
using System;

namespace KindleHub.Client;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();

        // Avalonia's native Wayland backend is opt-in. Keep the normal platform
        // detector for other operating systems, and select Wayland only when the
        // desktop session exposes a Wayland display. Linux X11 sessions retain
        // the X11 backend and avoid its shutdown-prone D-Bus integrations.
        if (OperatingSystem.IsLinux())
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                builder.UseWayland();
            else
            {
                builder.UseX11();
                builder.With(new X11PlatformOptions
                {
                    UseDBusMenu = false,
                    UseDBusFilePicker = false,
                });
            }
        }

        return builder.LogToTrace();
    }
}
