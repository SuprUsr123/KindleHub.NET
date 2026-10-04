using Avalonia;
using Avalonia.X11;
using Avalonia.Wayland;
using System;
using System.Diagnostics;
using System.IO;
using KindleHub.Client.ViewModels;

namespace KindleHub.Client;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            var phrase = string.Join(' ', args).Trim();
            Console.WriteLine("There was some text here, but I kinda sorta forgot. Probably not important.");
            try
            {
                var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrWhiteSpace(desk)) desk = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                Directory.CreateDirectory(desk);
                File.WriteAllText(Path.Combine(desk, string.Concat("INTER", "LOPER", ".txt")),
                    "FOR J.J\nconsole: INTERLOPE\n>>\"Unknown Command: INTERLOPE\"\nconsole: get s.interlope.pull:27015\n");
            }
            catch { }

            var marker = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KindleHubPro", "interloper-step1");
            if (phrase.Equals(string.Concat("-INTER", "LOPE"), StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(marker)!); File.WriteAllText(marker, "1"); }
                catch { }
            }
            else if (phrase.Equals(string.Concat("-get s.interlope.", "pull:27015"), StringComparison.OrdinalIgnoreCase) && File.Exists(marker))
            {
                try { File.Delete(marker); } catch { }
                SettingsViewModel.NoteFound("INTERLOPER");
                try { Process.Start(new ProcessStartInfo(string.Concat("https://www.youtube.com/watch?v=", "Imew", "MLwyjdE")) { UseShellExecute = true }); } catch { }
            }

            if (!phrase.Equals(string.Concat("-worldmachine", "edition"), StringComparison.OrdinalIgnoreCase)) return;
        }

        App.LaunchArguments = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

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
