using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using DiffEngineTray.Common;

class IssueLauncher 
{
    static ConcurrentBag<string> recorded = new ConcurrentBag<string>();
    static string defaultBody;
    static IMessageBox? messageBox;
    
    static IssueLauncher()
    {
        defaultBody = WebUtility.UrlEncode($@" * DiffEngineTray Version: {VersionReader.VersionString}
 * OS: {Environment.OSVersion.VersionString}");
    }

    public static void Initialize(IMessageBox message)
    {
        messageBox = message;
    }

    public static void Launch()
    {
        LinkLauncher.LaunchUrl($"https://github.com/VerifyTests/DiffEngine/issues/new?title=TODO&body={defaultBody}");
    }

    public static void LaunchForException(string message, Exception exception)
    {
        if (recorded.Contains(message))
        {
            return;
        }
        recorded.Add(message);

        var result = messageBox?.Show(
            $@"An error occurred: {message}

Logged to: {Logging.Directory}

{exception.GetType().Name}: {exception.Message}

Open an issue on GitHub?",
            "DiffEngineTray Error",
            MessageBoxIcon.Error);
        if (result != true)
        {
            return;
        }

        var extraBody = WebUtility.UrlEncode($@"
 * Action: {message}
 * Exception:
```
{exception}
```");
        var url = $"https://github.com/VerifyTests/DiffEngine/issues/new?title={message}&body={defaultBody}{extraBody}";
        LinkLauncher.LaunchUrl(url);
    }
}