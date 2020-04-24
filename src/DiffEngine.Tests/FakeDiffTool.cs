using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using DiffEngine;

public class FakeDiffTool
{
    public static string Exe;

    static FakeDiffTool()
    {
        var path = Path.Combine(AssemblyLocation.CurrentDirectory, "../../../../FakeDiffTool/bin/FakeDiffTool");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            path += ".exe";
        }
        else
        {
            path += ".dll";
        }

        Exe = Path.GetFullPath(path);
    }
}