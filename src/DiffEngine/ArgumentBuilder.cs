using System;
using System.Runtime.InteropServices;
using DiffEngine;

static class ArgumentBuilder
{
    public static BuildArguments Build(
        BuildArguments? windowsArguments,
        BuildArguments? linuxArguments,
        BuildArguments? osxArguments)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return windowsArguments!;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return linuxArguments!;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return osxArguments!;
        }

        throw new Exception($"OS not supported: {RuntimeInformation.OSDescription}");
    }
}