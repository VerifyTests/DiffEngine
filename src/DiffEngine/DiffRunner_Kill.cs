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

        // TryFindForInputFilePath rather than by extension, so this resolves the same tool the
        // launch did. By extension alone a file matched by a text file convention resolved to
        // nothing here, and Kill logged "Extension not found" for a pair LaunchAsync had opened -
        // leaving the tool on screen for a test that now passes
        if (!DiffTools.TryFindForInputFilePath(tempFile, out var diffTool))
        {
            Logging.Write($"No diff tool for. {tempFile}");
            return;
        }

        if (diffTool.IsMdi)
        {
            Logging.Write($"DiffTool is Mdi so not killing. diffTool: {diffTool.ExePath}");
            return;
        }

        var command = diffTool.BuildCommand(tempFile, targetFile);
        ProcessCleanup.Kill(command);
    }
}