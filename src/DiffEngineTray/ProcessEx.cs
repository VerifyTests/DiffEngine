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