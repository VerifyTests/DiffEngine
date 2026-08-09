/// <summary>
/// Finds the shim under runtimes/{rid}/native.
/// <para>
/// The default probing rules only consult that layout for natives that arrived through a NuGet
/// package and are recorded in deps.json. This app ships them as plain content, both in the repo
/// and inside the RID agnostic dotnet tool package, so it resolves them itself.
/// </para>
/// </summary>
static class NativeResolver
{
    const string name = "diffengine_viewer";
    static bool registered;

    public static void Register()
    {
        if (registered)
        {
            return;
        }

        registered = true;
        NativeLibrary.SetDllImportResolver(typeof(NativeResolver).Assembly, Resolve);
    }

    static nint Resolve(string library, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (library != name)
        {
            return nint.Zero;
        }

        foreach (var candidate in Candidates())
        {
            if (File.Exists(candidate) &&
                NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        // Zero hands back to the default resolver, so a native sitting beside the executable, or
        // on the system path, still works.
        return nint.Zero;
    }

    /// <summary>
    /// The shipped binary for the current RID, or false when none is shipped for it, such as on
    /// linux-musl.
    /// </summary>
    public static bool TryFind([NotNullWhen(true)] out string? path)
    {
        foreach (var candidate in Candidates())
        {
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        }

        path = null;
        return false;
    }

    static IEnumerable<string> Candidates()
    {
        var root = AppContext.BaseDirectory;
        var file = FileName();
        foreach (var rid in Rids())
        {
            yield return Path.Combine(root, "runtimes", rid, "native", file);
        }

        yield return Path.Combine(root, file);
    }

    static IEnumerable<string> Rids()
    {
        // The exact RID first. On Alpine that is linux-musl-x64, which we do not ship, so the
        // probe simply misses rather than loading a glibc binary and hard crashing.
        yield return RuntimeInformation.RuntimeIdentifier;

        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => null
        };

        if (architecture is null)
        {
            yield break;
        }

        if (OperatingSystem.IsWindows())
        {
            yield return $"win-{architecture}";
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return $"osx-{architecture}";
        }
        else if (OperatingSystem.IsLinux())
        {
            yield return $"linux-{architecture}";
        }
    }

    static string FileName()
    {
        if (OperatingSystem.IsWindows())
        {
            return $"{name}.dll";
        }

        if (OperatingSystem.IsMacOS())
        {
            return $"lib{name}.dylib";
        }

        return $"lib{name}.so";
    }
}
