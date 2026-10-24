using Microsoft.Win32;

namespace GitHubReleaseWatcher.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "GitHubReleaseWatcher";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(AppName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
            key.SetValue(AppName, $"\"{Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName}\"");
        else
            key.DeleteValue(AppName, false);
    }
}
