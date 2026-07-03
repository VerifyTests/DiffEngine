static class LockedFilesHandler
{
    public static bool AlwaysKill { get; set; }

    public static LockedFilesResponse Resolve(TrackedMove move, LockedFiles locked)
    {
        if (AlwaysKill)
        {
            return LockedFilesResponse.Kill;
        }

        using var form = new LockedFilesForm(move, locked);
        form.ShowDialog();

        if (form.AlwaysKill)
        {
            AlwaysKill = true;
            Persist();
        }

        return form.Response;
    }

    static void Persist() =>
        Task.Run(async () =>
        {
            try
            {
                var settings = await SettingsHelper.Read();
                settings.AlwaysKillLockingProcesses = true;
                await SettingsHelper.Write(settings);
            }
            catch (Exception exception)
            {
                ExceptionHandler.Handle("Failed to persist AlwaysKillLockingProcesses setting", exception);
            }
        });
}