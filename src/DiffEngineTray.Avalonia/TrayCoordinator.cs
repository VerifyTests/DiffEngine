namespace DiffEngineTray;

// Orchestrates the Avalonia head, mirroring the Windows Forms Program.Inner:
// reads settings, tracks pending files, serves the piper, and drives the tray menu.
class TrayCoordinator(Application application, IClassicDesktopStyleApplicationLifetime lifetime) :
    IDisposable
{
    Tracker tracker = null!;
    TrayIcon trayIcon = null!;
    TrayCommands commands = null!;
    Settings settings = new();
    MacHotKeys? hotKeys;
    readonly CancelSource piperCancel = new();
    Task piperTask = Task.CompletedTask;
    volatile bool disposed;

    public void Start()
    {
        settings = ReadSettings();

        trayIcon = new()
        {
            Icon = AppIcons.Default,
            ToolTipText = "DiffEngineTray",
            IsVisible = true
        };

        commands = new()
        {
            Exit = () => lifetime.Shutdown(),
            Options = OpenOptions,
            OpenLogs = Logging.OpenDirectory,
            Purge = Purger.Run,
            RaiseIssue = IssueLauncher.Launch,
            Refresh = RebuildMenu
        };

        tracker = new(
            active: () => OnUiThread(() =>
            {
                trayIcon.Icon = AppIcons.Active;
                RebuildMenu();
            }),
            inactive: () => OnUiThread(() =>
            {
                trayIcon.Icon = AppIcons.Default;
                RebuildMenu();
            }));

        RebuildMenu();
        TrayIcon.SetIcons(application, new() { trayIcon });

        piperTask = StartServer();
        hotKeys = MacHotKeys.TryCreate(settings, tracker);
    }

    Task StartServer() =>
        PiperServer.Start(
            payload => tracker.AddMove(
                payload.Temp,
                payload.Target,
                payload.Exe,
                payload.Arguments,
                payload.CanKill,
                payload.ProcessId),
            payload => tracker.AddDelete(payload.File),
            piperCancel.Token);

    static Settings ReadSettings()
    {
        try
        {
            return SettingsHelper.Read().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Failed to read settings: {FilePath}", SettingsHelper.FilePath);
            IssueLauncher.LaunchForException($"Cannot read settings: {SettingsHelper.FilePath}", exception);
            return new();
        }
    }

    void OpenOptions() =>
        OptionsWindow.Launch(settings, SaveSettings);

    async Task<IReadOnlyCollection<string>> SaveSettings(Settings newSettings)
    {
        if (!newSettings.IsValidate(out var validationErrors))
        {
            return validationErrors;
        }

        hotKeys?.Rebind(newSettings, tracker);

        if (newSettings.RunAtStartup)
        {
            MacStartup.Add();
        }
        else
        {
            MacStartup.Remove();
        }

        await SettingsHelper.Write(newSettings);
        settings = newSettings;
        return [];
    }

    void RebuildMenu() =>
        OnUiThread(() => trayIcon.Menu = TrayMenuBuilder.Build(commands, tracker));

    void OnUiThread(Action action)
    {
        if (disposed)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!disposed)
                {
                    action();
                }
            });
        }
    }

    public void Dispose()
    {
        // Stops queued tracker callbacks from touching the tray icon as it is torn down.
        disposed = true;

        try
        {
            hotKeys?.Dispose();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to dispose hotkeys");
        }

        // Cancel background work but do NOT block the UI thread waiting for it. A confirm dialog that a
        // background error path posted to the UI thread would otherwise deadlock shutdown. DisposeAsync
        // runs Clear() (killing tracked diff tools) synchronously; the timer teardown finishes in the
        // background as the process exits.
        piperCancel.Cancel();
        _ = tracker.DisposeAsync().AsTask();
        trayIcon.Dispose();
    }
}
