using Microsoft.Win32.SafeHandles;

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

        try
        {
            process = Process.GetProcessById(id);
            return true;
        }
        catch (ArgumentException)
        {
            // Handle Race condition if process doesnt exists
            process = null;
            return false;
        }
    }

    public static void KillAndDispose(this Process process)
    {
        try
        {
            process.Kill();
            var exited = process.WaitForExit(500);
            if (!exited)
            {
                ExceptionHandler.Handle($"Failed to kill process. Id:{process.Id} Name: {process.MainModule?.FileName}");
            }
        }
        catch (InvalidOperationException)
        {
            // Race condition can cause "No process is associated with this object"
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle($"Failed to kill process. Id:{process.Id} Name: {process.MainModule?.FileName}", exception);
        }
        finally
        {
            process.Dispose();
        }
    }
}