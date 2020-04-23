using System;
using DiffEngine;

static partial class Implementation
{
    public static Definition TortoiseMerge()
    {
        string BuildArguments(string tempFile, string targetFile) =>
            $"\"{tempFile}\" \"{targetFile}\"";

        return new Definition(
            name: DiffTool.TortoiseMerge,
            url: "https://tortoisesvn.net/TortoiseMerge.html",
            autoRefresh: false,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            windows: new OsSettings( BuildArguments,new[]
            {
                @"%ProgramFiles%\TortoiseSVN\bin\TortoiseMerge.exe"
            }),
            linux: null,
            osx: null,
            binaryExtensions: Array.Empty<string>());
    }
}