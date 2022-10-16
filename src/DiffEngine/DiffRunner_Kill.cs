namespace DiffEngine;

public static partial class DiffRunner
{
    /// <summary>
    /// Find and kill a diff tool process.
    /// </summary>
    public static void Kill(string tempFile, string targetFile)
    {
        if (Disabled)
        {
            return;
        }

        var extension = FileExtensions.GetExtension(tempFile);
        if (!DiffTools.TryFindByExtension(extension, out var diffTool))
        {
            Logging.Write($"Extension not found. {extension}");
            return;
        }

        var command = diffTool.BuildCommand(tempFile, targetFile);

        if (diffTool.IsMdi)
        {
            Logging.Write($"DiffTool is Mdi so not killing. diffTool: {diffTool.ExePath}");
            return;
        }

        ProcessCleanup.Kill(command);
    }
}