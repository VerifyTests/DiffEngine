using IOException = System.IO.IOException;

static class FileEx
{
    public static bool IsEmptyDirectory(string directory) =>
        !Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any();

    public static bool SafeDeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException exception)
        {
            Log.Error(exception, $"Failed to delete '{path}'.");
            //Swallow this since it is likely that a running test it reading or
            //writing to the files, and the result will re-add the tracked item
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle($"Failed to delete '{path}'.", exception);
        }
        return false;
    }

    public static bool SafeDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        if (!IsEmptyDirectory(path))
        {
            return false;
        }

        try
        {
            Directory.Delete(path, false);
            return true;
        }
        catch (IOException exception)
        {
            Log.Error(exception, $"Failed to delete '{path}'.");
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle($"Failed to delete '{path}'.", exception);
        }
        return false;
    }

    public static bool SafeMove(string temp, string target)
    {
        //Swallow this since it is likely that a running test it reading or
        //writing to the files, and the result will re-add the tracked item
        void HandleAccessException(Exception exception) =>
            Log.Error(exception, $"Failed to move '{temp}' to '{target}'.");

        if (!File.Exists(temp))
        {
            return false;
        }

        try
        {
            File.Move(temp, target, true);
            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleAccessException(exception);
        }
        catch (IOException exception)
        {
            HandleAccessException(exception);
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle($"Failed to move '{temp}' to '{target}'.", exception);
        }
        return false;
    }
}