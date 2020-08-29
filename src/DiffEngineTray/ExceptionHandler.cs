using System;
using Serilog;

static class ExceptionHandler
{
    public static void Handle(string message, Exception exception)
    {
        Log.Error(exception, message);
        IssueLauncher.LaunchForException(message, exception);
    }
}