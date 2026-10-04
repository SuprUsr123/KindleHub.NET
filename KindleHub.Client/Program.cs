using Avalonia;
using Avalonia.X11;
using Avalonia.Wayland;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
                Console.Error.WriteLine("Unhandled exception. System.InvalidOperationException: Unknown Command: INTERLOPE");
                Console.Error.WriteLine("   at KindleHub.Client.Program.Main(String[] args)");
                Environment.ExitCode = 1;
                return;
            }
            if (phrase.Equals(string.Concat("-get s.interlope.", "pull:27015"), StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(marker))
                {
                    try { File.Delete(marker); } catch { }
                    RunInterloperTerminalSequence();
                    SettingsViewModel.NoteFound("INTERLOPER");
                    try { Process.Start(new ProcessStartInfo(string.Concat("https://www.youtube.com/watch?v=", "Imew", "MLwyjdE")) { UseShellExecute = true }); } catch { }
                }
                return;
            }

            if (!phrase.Equals(string.Concat("-worldmachine", "edition"), StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("There was some text here, but I kinda sorta forgot. Probably not important.");
                return;
            }
            Console.WriteLine("There was some text here, but I kinda sorta forgot. Probably not important.");
        }

        App.LaunchArguments = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void RunInterloperTerminalSequence()
    {
        Console.WriteLine("getting socket for s.interlope.pull:27015...");
        Thread.Sleep(TimeSpan.FromSeconds(2));
        Console.WriteLine("extracting data from terminal");
        Thread.Sleep(TimeSpan.FromSeconds(7));
        Console.WriteLine("submitting envelope");
        Thread.Sleep(TimeSpan.Zero);
        Console.WriteLine("received");
        Thread.Sleep(TimeSpan.FromSeconds(1));
        Console.WriteLine("request from archive submitted");
        Thread.Sleep(TimeSpan.FromSeconds(5));
        Console.WriteLine("message from server administrator: CONGRATULATIONS AND WELCOME PLEASE ENTER WITH CAUTION YOU ARE NOT WELCOME HERE");
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
