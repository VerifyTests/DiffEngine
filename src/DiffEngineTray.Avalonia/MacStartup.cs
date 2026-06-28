namespace DiffEngineTray;

// Run-at-login for the Avalonia head. macOS uses a LaunchAgent plist (the equivalent of the
// Windows "Run" registry key); other platforms are currently no-ops.
static class MacStartup
{
    const string label = "com.verifytests.diffenginetray";

    static string PlistPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "LaunchAgents",
            $"{label}.plist");

    // The global tool shim installed by `dotnet tool install --global DiffEngineTray`.
    static string ToolPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotnet",
            "tools",
            "DiffEngineTray");

    public static bool Exists() =>
        OperatingSystem.IsMacOS() &&
        File.Exists(PlistPath);

    public static void Add()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
        File.WriteAllText(PlistPath, PlistContent());
    }

    public static void Remove()
    {
        if (OperatingSystem.IsMacOS() &&
            File.Exists(PlistPath))
        {
            File.Delete(PlistPath);
        }
    }

    static string PlistContent() =>
        $"""
         <?xml version="1.0" encoding="UTF-8"?>
         <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
         <plist version="1.0">
         <dict>
             <key>Label</key>
             <string>{label}</string>
             <key>ProgramArguments</key>
             <array>
                 <string>{ToolPath}</string>
             </array>
             <key>RunAtLoad</key>
             <true/>
             <key>ProcessType</key>
             <string>Interactive</string>
         </dict>
         </plist>
         """;
}
