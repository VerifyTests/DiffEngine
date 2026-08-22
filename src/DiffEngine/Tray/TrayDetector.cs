static class TrayDetector
{
    // Checked live (not cached) so a tray started after the test process still counts
    public static bool IsRunning()
    {
        try
        {
            if (Mutex.TryOpenExisting("DiffEngine", out var mutex))
            {
                mutex.Dispose();
                return true;
            }
        }
        //net7 on mac throws an exception if the mutex does not exist
        catch (IOException)
        {
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
