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

        return false;
    }
}
