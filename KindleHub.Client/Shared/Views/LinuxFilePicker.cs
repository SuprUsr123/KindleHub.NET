using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace KindleHub.Client.Views;

/// <summary>Uses an installed desktop picker on Linux without opening Avalonia's D-Bus connection.</summary>
internal static class LinuxFilePicker
{
    public static bool IsAvailable => FindExecutable("kdialog") is not null
        || FindExecutable("zenity") is not null
        || FindExecutable("yad") is not null;

    public static async Task<string?> OpenAsync(string title, string filterName, params string[] patterns)
    {
        var kdialog = FindExecutable("kdialog");
        if (kdialog is not null)
        {
            string filter = $"{filterName} ({string.Join(' ', patterns)})|{string.Join(' ', patterns)}";
            return await RunAsync(kdialog, "--getopenfilename", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), filter, "--title", title);
        }

        var zenity = FindExecutable("zenity");
        if (zenity is not null)
        {
            var args = new[] { "--file-selection", $"--title={title}", $"--file-filter={filterName} | {string.Join(' ', patterns)}", "--file-filter=All files | *" };
            return await RunAsync(zenity, args);
        }

        var yad = FindExecutable("yad");
        if (yad is not null)
        {
            var args = new[] { "--file", $"--title={title}", $"--file-filter={filterName} | {string.Join(' ', patterns)}", "--file-filter=All files | *" };
            return await RunAsync(yad, args);
        }

        return null;
    }

    private static async Task<string?> RunAsync(string executable, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);

            using var process = Process.Start(start);
            if (process is null) return null;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();
            if (process.ExitCode != 0 && error.Length > 0)
                Console.WriteLine($"[KindleHub Debug] Desktop picker exited {process.ExitCode}: {error}");
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KindleHub Debug] Desktop picker failed: {ex.Message}");
            return null;
        }
    }

    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
