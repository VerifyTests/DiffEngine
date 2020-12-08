using System;
using System.IO;

static class AssemblyLocation
{
    static AssemblyLocation()
    {
        var assembly = typeof(AssemblyLocation).Assembly;
#pragma warning disable 618
        UriBuilder uri = new(assembly.CodeBase!);
#pragma warning restore 618
        var path = Uri.UnescapeDataString(uri.Path);

        CurrentDirectory = Path.GetDirectoryName(path)!;
    }

    public static string CurrentDirectory;
}