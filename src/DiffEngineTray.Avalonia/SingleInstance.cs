namespace DiffEngineTray;

static class SingleInstance
{
    // Returns the held mutex when this is the first instance, or null when another instance owns it.
    public static Mutex? TryAcquire()
    {
        try
        {
            var mutex = new Mutex(true, "DiffEngine", out var createdNew);
            if (createdNew)
            {
                return mutex;
            }

            mutex.Dispose();
            return null;
        }
        catch (Exception exception)
        {
            // Named mutexes are not guaranteed on every platform; fail open rather than refuse to start.
            Log.Warning(exception, "Failed to create single-instance mutex; continuing without the guard.");
            return new(false);
        }
    }
}
