namespace DiffEngine;

/// <summary>
/// Whether a DiffEngineTray is running, asked live rather than cached, so a tray started after the
/// asking process still counts.
/// <para>
/// On Windows by the tray's named mutex, which is local to the login session, as the tray is. Off
/// Windows by the listener table, as a listener on the tray's piper port: .NET keeps a named mutex
/// there in a file under a directory per session, which is the process's session id, so a tray
/// started at login and a test run started from a terminal or an IDE never saw each other's. Nor
/// do two runtimes: given a global name, net8 and net10 each found only a holder on their own
/// runtime, and the tray is net10 while a test process can be any of them. A port is seen across
/// sessions and runtimes alike.
/// </para>
/// </summary>
static class TrayDetector
{
    /// <summary>
    /// The port the tray's piper listens on and <c>PiperClient</c> sends to. Here as well as
    /// there because the viewer links this file and not the client.
    /// </summary>
    public const int PiperPort = 3492;

    public static bool IsRunning() =>
        IsRunning(PiperPort);

    /// <param name="piperPort">Where the tray's piper is asked for off Windows.</param>
    public static bool IsRunning(int piperPort)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Listed rather than held: a table that cannot be read is no tray, since what a
            // caller does on a tray being there - a viewer hiding its window on close, say - is
            // what nothing undoes when there was none
            return ListenerTable.IsListed(piperPort);
        }

        try
        {
            if (Mutex.TryOpenExisting("DiffEngine", out var mutex))
            {
                mutex.Dispose();
                return true;
            }
        }
        // Documented for a mutex that exists but cannot be opened by this caller: the tray running
        // under one account and the tests under another in the same session. "Not accessible" is
        // not "not running", but from here there is nothing to tell them apart with, and the
        // answer that keeps a diff tool launching is the one to take
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }
}
