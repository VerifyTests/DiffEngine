// DiffEngineTray is the obsolete public shim, but its IsRunning is still where the tray check
// lives, and tests still set it.
#pragma warning disable CS0618 // Type or member is obsolete

namespace DiffEngine;

/// <summary>
/// Where a pending move or delete goes.
/// <para>
/// The tray when one is running, over the piper port it has always used. Otherwise the process
/// that owns the inline queue, which is normally a viewer — so a pending file has a surface with
/// no tray installed. Before this it had none: the send was skipped outright, and a received file
/// or a stale verified file was pending in nothing at all.
/// </para>
/// <para>
/// A delete starts a viewer when nothing owns the queue. A move does not, and the asymmetry is the
/// point: <see cref="DiffRunner"/> has already opened a diff tool for that file pair, so a move
/// has a window, and a second one competing with it is not an improvement. A delete has no second
/// file to compare against and so no tool to open.
/// </para>
/// <para>
/// The tray check is <see cref="DiffEngineTray.IsRunning"/>, read once when that type initialises.
/// A tray started after the test process therefore never sees the piper port for the rest of that
/// process's life, and its moves and deletes arrive here instead — which a tray that owns the
/// queue answers, so they end up tracked either way.
/// </para>
/// <para>
/// The mirror of that case is a tray that exits while a long lived host keeps running, and it is
/// why the piper send is asked whether it connected rather than told to get on with it. The cached
/// answer still says a tray is there, so every later move and delete went to a port nobody was
/// listening on and was swallowed into a trace line: pending in nothing, with no fallback and no
/// LaunchDelete. A refused piper send now falls through to the same branch as no tray at all.
/// </para>
/// </summary>
static class PendingFiles
{
    public static void AddDelete(string file)
    {
        if (DiffEngineTray.IsRunning &&
            PiperClient.SendDelete(file))
        {
            return;
        }

        if (ViewerClient.TrySend(new(ViewerVerb.Delete, file)))
        {
            return;
        }

        ViewerLauncher.LaunchDelete(file);
    }

    public static async Task AddDeleteAsync(string file, Cancel cancel)
    {
        if (DiffEngineTray.IsRunning &&
            await PiperClient.SendDeleteAsync(file, cancel))
        {
            return;
        }

        if (await ViewerClient.TrySendAsync(new(ViewerVerb.Delete, file), cancel))
        {
            return;
        }

        ViewerLauncher.LaunchDelete(file);
    }

    public static void AddMove(
        string tempFile,
        string targetFile,
        string? exe,
        string? arguments,
        bool canKill,
        int? processId)
    {
        if (DiffEngineTray.IsRunning &&
            PiperClient.SendMove(tempFile, targetFile, exe, arguments, canKill, processId))
        {
            return;
        }

        ViewerClient.TrySend(new(ViewerVerb.Move, tempFile, targetFile));
    }

    public static async Task AddMoveAsync(
        string tempFile,
        string targetFile,
        string? exe,
        string? arguments,
        bool canKill,
        int? processId,
        Cancel cancel)
    {
        if (DiffEngineTray.IsRunning &&
            await PiperClient.SendMoveAsync(tempFile, targetFile, exe, arguments, canKill, processId, cancel))
        {
            return;
        }

        await ViewerClient.TrySendAsync(new(ViewerVerb.Move, tempFile, targetFile), cancel);
    }
}
