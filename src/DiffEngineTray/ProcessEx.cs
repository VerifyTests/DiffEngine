static class ProcessEx
{
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    static extern SafeProcessHandle OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    const int processQueryInfo = 0x0400;

    public static bool TryGet(int id, [NotNullWhen(true)] out Process? process)
    {
        using (var handle = OpenProcess(processQueryInfo, false, id))
        {
            if (handle.IsInvalid)
            {
                process = null;
                return false;
            }
        }

        Process? opened = null;
        try
        {
            opened = Process.GetProcessById(id);
            process = opened;
            // Forces the OS handle open, and keeps it. GetProcessById holds none of its own, so
            // Kill, HasExited and MainWindowHandle each re-open the id at the moment they are
            // called - and a tracked move outlives its diff tool by design, since HandleScanMove
            // keeps it while the temp file is still there. Hours later that id may belong to
            // something else, and "Accept all" or "Open diff tool" would kill whatever it is.
            // An open handle also stops Windows handing the id out again while this move is
            // tracked, so there is nothing to confuse it with
            _ = process.Handle;
            return true;
        }
        catch (ArgumentException)
        {
            // Handle Race condition if process doesnt exists
            process = null;
            return false;
        }
        catch (Exception exception)
            when (exception is Win32Exception or InvalidOperationException)
        {
            // The handle could not be held - it exited between the probe above and here, or this
            // account cannot open it. Without one there is no way to tell the process apart from a
            // later holder of the same id, so it is better tracked as no process at all: the tool
            // is then not killed, rather than something else being killed in its place
            opened?.Dispose();
            process = null;
            return false;
        }
    }

    public static void KillAndDispose(this Process process)
    {
        // Capture identity up front. Once the process has exited, Id/MainModule can throw,
        // so reading them in the error handlers below could mask the real failure.
        var description = Describe(process);
        try
        {
            process.Kill();
            var exited = process.WaitForExit(500);
            if (!exited)
            {
                ExceptionHandler.Handle($"Failed to kill process. {description}");
            }
        }
        catch (InvalidOperationException)
        {
            // Race condition can cause "No process is associated with this object"
        }
        catch (Win32Exception)
        {
            // no permission or already closed
            // https://github.com/VerifyTests/DiffEngine/issues/542
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle($"Failed to kill process. {description}", exception);
        }
        finally
        {
            process.Dispose();
        }
    }

    internal static string Describe(Process process)
    {
        try
        {
            return $"Id:{process.Id} Name: {process.MainModule?.FileName}";
        }
        catch (Exception)
        {
            // Id/MainModule can throw for an exited, disposed or inaccessible process.
            return "Id: unknown";
        }
    }
}