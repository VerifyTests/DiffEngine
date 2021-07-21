using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

static class ProcessEx
{
    public static bool TryGet(int id, [NotNullWhen(true)] out Process? process)
    {
        try
        {
            process = Process.GetProcessById(id);
            return true;
        }
        catch (ArgumentException)
        {
            //If process doesnt exists
            process = null;
            return false;
        }
    }

    public static void KillAndDispose(this Process process)
    {
        try
        {
            process.Kill();
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