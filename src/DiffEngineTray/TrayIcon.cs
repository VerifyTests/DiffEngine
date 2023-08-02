public static class TrayIcon
{
    public static void Promoted()
    {
        //C:\Users\SimonCropp\.dotnet\tools\DiffEngineTray.exe
        var profileDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var exePath = Path.Combine(profileDirectory, @"dotnet\tools\DiffEngineTray.exe");
        using var iconSettingsKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
        if (iconSettingsKey == null)
        {
            DiffEngine.Logging.Write(@"NotifyIconSettings reg path returned null. Path: Control Panel\NotifyIconSettings");
            return;
        }

        foreach (var subKeyName in iconSettingsKey.GetSubKeyNames())
        {
            using var iconKey = iconSettingsKey.OpenSubKey(subKeyName, true);
            if (iconKey == null)
            {
                continue;
            }

            var valueNames = iconKey.GetValueNames();
            if (!valueNames.Contains("ExecutablePath") ||
                !string.Equals((string?) iconKey.GetValue("ExecutablePath"), exePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            iconKey.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
            return;
        }

        DiffEngine.Logging.Write(@"DiffEngineTray.exe not found in NotifyIconSettings. Path: Control Panel\NotifyIconSettings");
    }
}