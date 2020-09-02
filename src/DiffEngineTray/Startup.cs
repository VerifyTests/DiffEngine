using System;
using System.IO;
using Microsoft.Win32;

public class Startup
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
        key.DeleteValue("DiffEngineTray");
    }

    public static bool Exists()
    {
        using var key = GetRunKey();
        return key.GetValue("DiffEngineTray") != null;
    }

    static RegistryKey GetRunKey()
    {
        return Registry.CurrentUser
            .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)!;
    }
}