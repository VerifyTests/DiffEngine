using System.Diagnostics;

// The launcher is the entry point of the cross-platform `DiffEngineTray` dotnet tool.
// It detects the OS and starts the matching head, which lives in a sibling folder:
//   tools/net10.0/any/DiffEngineTray.Launcher.dll  <- this
//   tools/net10.0/any/windows/DiffEngineTray.dll    <- Windows Forms head (Windows builds only)
//   tools/net10.0/any/avalonia/DiffEngineTray.Avalonia.dll  <- Avalonia head (macOS/Linux)

var toolRoot = AppContext.BaseDirectory;

var (subDirectory, assemblyName) = OperatingSystem.IsWindows()
    ? ("windows", "DiffEngineTray.dll")
    : ("avalonia", "DiffEngineTray.Avalonia.dll");

var headDll = Path.Combine(toolRoot, subDirectory, assemblyName);

if (!File.Exists(headDll))
{
    // A package produced on a non-Windows host has no Windows head; fall back to the Avalonia head.
    var fallback = Path.Combine(toolRoot, "avalonia", "DiffEngineTray.Avalonia.dll");
    if (File.Exists(fallback))
    {
        headDll = fallback;
    }
    else
    {
        await Console.Error.WriteLineAsync($"DiffEngineTray head not found: {headDll}");
        return 1;
    }
}

var startInfo = new ProcessStartInfo(DotnetHost())
{
    UseShellExecute = false,
    CreateNoWindow = true
};
startInfo.ArgumentList.Add("exec");
startInfo.ArgumentList.Add(headDll);
foreach (var argument in args)
{
    startInfo.ArgumentList.Add(argument);
}

try
{
    // Fire-and-forget: the head runs as an independent tray/menu-bar app for the rest of the session.
    using var process = Process.Start(startInfo);
    return process == null ? 1 : 0;
}
catch (Exception exception)
{
    await Console.Error.WriteLineAsync($"Failed to start DiffEngineTray head: {exception}");
    return 1;
}

// Resolve the dotnet host without relying on PATH. At macOS login launchd starts the tool shim with
// a minimal environment (no DOTNET_HOST_PATH, PATH lacking the dotnet install dir), so a bare "dotnet"
// would fail and the tray would silently never start.
static string DotnetHost()
{
    var fileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

    // Set by the SDK/MSBuild when invoked through the muxer.
    var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
    if (IsFile(hostPath))
    {
        return hostPath!;
    }

    // Set by the apphost (e.g. the tool shim) that started this process.
    foreach (var root in new[]
             {
                 Environment.GetEnvironmentVariable("DOTNET_ROOT"),
                 Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)")
             })
    {
        if (!string.IsNullOrEmpty(root))
        {
            var candidate = Path.Combine(root, fileName);
            if (IsFile(candidate))
            {
                return candidate;
            }
        }
    }

    foreach (var candidate in WellKnownHostPaths(fileName))
    {
        if (IsFile(candidate))
        {
            return candidate;
        }
    }

    // Last resort: rely on PATH.
    return fileName;
}

static bool IsFile(string? path) =>
    !string.IsNullOrEmpty(path) && File.Exists(path);

static IEnumerable<string> WellKnownHostPaths(string fileName)
{
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (OperatingSystem.IsWindows())
    {
        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrEmpty(programFiles))
        {
            yield return Path.Combine(programFiles, "dotnet", fileName);
        }

        yield return Path.Combine(home, ".dotnet", fileName);
    }
    else
    {
        yield return "/usr/local/share/dotnet/dotnet"; // macOS default (and the AppVeyor install dir)
        yield return "/usr/local/share/dotnet/x64/dotnet"; // macOS x64 runtime on Apple Silicon
        yield return "/opt/homebrew/bin/dotnet"; // Homebrew (Apple Silicon)
        yield return "/usr/local/bin/dotnet"; // Homebrew (Intel) / common symlink
        yield return "/usr/share/dotnet/dotnet"; // Linux
        yield return Path.Combine(home, ".dotnet", "dotnet"); // per-user install
    }
}
