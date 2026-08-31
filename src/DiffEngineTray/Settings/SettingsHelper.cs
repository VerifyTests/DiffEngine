static class SettingsHelper
{
    public static string FilePath;

    static SettingsHelper()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(appData, "DiffEngine");
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "settings.json");
    }

    public static async Task<Settings> Read()
    {
        Settings settings;
        if (File.Exists(FilePath))
        {
            // Read whole and parsed in one go rather than deserialised off an async FileStream.
            // The file is under 200 bytes, so the async machinery cost more than the read did:
            // 27ms for the call against 21ms without it. What is left is System.Text.Json being
            // loaded and jitted for the first time, which nothing here can avoid - see
            // <see cref="SettingsContext"/>.
            var json = File.ReadAllBytes(FilePath);
            settings = JsonSerializer.Deserialize(json, SettingsContext.Default.Settings)!;
        }
        else
        {
            await File.WriteAllTextAsync(FilePath, "{}");
            settings = new();
        }

        settings.TargetOnLeft = TargetPosition.TargetOnLeft;
        settings.MaxInstancesToLaunch = MaxInstance.MaxInstancesToLaunch;
        return settings;
    }

    public static Task Write(Settings settings)
    {
        TargetPosition.SetTargetOnLeft(settings.TargetOnLeft);
        MaxInstance.SetForUser(settings.MaxInstancesToLaunch);
        return WriteFile(settings);
    }

    /// <summary>
    /// The file half of <see cref="Write" />, which is the half that has to survive being
    /// interrupted.
    /// <para>
    /// Serialised beside the settings file and moved over it, rather than written in place. The
    /// file used to be deleted and then rewritten, so a kill in the window between the two - or
    /// part way through serialising - left no file at all, or a truncated one, and every launch
    /// after that met "Cannot start. Failed to read settings" until it was deleted by hand.
    /// </para>
    /// </summary>
    internal static async Task WriteFile(Settings settings)
    {
        var temp = $"{FilePath}.tmp";

        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, settings, SettingsContext.Default.Settings);
        }

        await Swap(temp);
    }

    /// <summary>
    /// The swap can lose a race with anything holding the settings file open - the tray reading it
    /// at startup, a backup, an indexer - and losing it throws out of an async void click handler.
    /// Whoever has it will not have it for long, so the save waits rather than failing.
    /// </summary>
    static async Task Swap(string temp)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, FilePath, true);
                return;
            }
            catch (Exception exception)
                when (attempt < 10 &&
                      exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(20);
            }
        }
    }
}