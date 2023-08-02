﻿public class Startup
{
    public static void Add()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var exePath = Path.Combine(profile, ".dotnet", "tools", "DiffEngineTray.exe");
        using var key = GetRunKey();
        key.SetValue("DiffEngineTray", exePath);
    }

    public static void Remove()
    {
        using var key = GetRunKey();
        key.DeleteValue("DiffEngineTray", false);
    }

    public static bool Exists()
    {
        using var key = GetRunKey();
        return key.GetValue("DiffEngineTray") != null;
    }

    static RegistryKey GetRunKey() =>
        Registry.CurrentUser
            .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)!;
}