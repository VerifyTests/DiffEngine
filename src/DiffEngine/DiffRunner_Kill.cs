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

        // The viewer holds this pair as a row in a queue some other process owns, not as a window
        // of its own, so there is no process here to kill - and killing the one it is drawn in
        // would take every other pending pair with it. Settling drops the row instead, which is
        // what killing the window meant for a tool that had one per pair.
        if (PendingFiles.IsViewer(diffTool))
        {
            ViewerClient.TrySend(new(ViewerVerb.Settle, TrackedKeys.ForMove(tempFile)));
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