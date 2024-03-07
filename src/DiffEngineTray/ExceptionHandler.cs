static class ExceptionHandler
{
    public static void Handle(string message, Exception exception)
    {
        Log.Error(exception, message);
        IssueLauncher.LaunchForException(message, exception);
    }

    public static void Handle(string message)
    {
        Log.Error(message);
        IssueLauncher.LaunchForException(message);
    }
}