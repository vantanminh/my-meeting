using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace MeetingAssistant.Services;

public enum WorkspaceResetChoice
{
    ReinstallApp,
    RemoveData,
    EraseEverything
}

public static class WorkspaceReset
{
    public static string Describe(WorkspaceResetChoice choice) => choice switch
    {
        WorkspaceResetChoice.ReinstallApp => "The installer will remove the app and leave your meetings and settings in place.",
        WorkspaceResetChoice.RemoveData => "Meetings, recordings, and the local workspace are deleted. Settings stay so you can sign nothing back in.",
        _ => "The app, meetings, recordings, settings, and startup entry are removed, as if it was never installed."
    };

    public static void Apply(WorkspaceResetChoice choice)
    {
        if (choice is WorkspaceResetChoice.RemoveData or WorkspaceResetChoice.EraseEverything)
            DeleteDirectory(AppPaths.DataDirectory);

        if (choice == WorkspaceResetChoice.EraseEverything)
        {
            TryDisableStartup();
            DeleteDirectory(AppPaths.RootDirectory);
        }

        if (choice is WorkspaceResetChoice.ReinstallApp or WorkspaceResetChoice.EraseEverything)
            StartUninstaller();
    }

    private static void TryDisableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue("MeetingAssistant", throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }

    private static void StartUninstaller()
    {
        var command = FindUninstallCommand();
        if (string.IsNullOrWhiteSpace(command)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command} /SILENT",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static string? FindUninstallCommand()
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (key is null) continue;
            foreach (var name in key.GetSubKeyNames())
            {
                using var app = key.OpenSubKey(name);
                var display = app?.GetValue("DisplayName") as string;
                var uninstall = app?.GetValue("UninstallString") as string;
                if (display?.Contains("Meeting Assistant", StringComparison.OrdinalIgnoreCase) == true &&
                    !string.IsNullOrWhiteSpace(uninstall))
                    return uninstall;
            }
        }

        return null;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
