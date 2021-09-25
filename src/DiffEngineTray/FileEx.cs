using Serilog;

static class FileEx
{
    public static void SafeDelete(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException exception)
        {
            Log.Error(exception, $"Filed to delete '{path}'.");
            //Swallow this since it is likely that a running test it reading or
            //writing to the files, and the result will re-add the tracked item
        }
    }

    public static void SafeMove(string temp, string target)
    {
        if (!File.Exists(temp))
        {
            return;
        }

        try
        {
            File.Move(temp, target, true);
        }
        catch (IOException exception)
        {
            Log.Error(exception, $"Filed to move '{temp}' to '{target}'.");
            //Swallow this since it is likely that a running test it reading or
            //writing to the files, and the result will re-add the tracked item
        }
    }
}