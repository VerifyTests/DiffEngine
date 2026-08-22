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
            await using var stream = File.OpenRead(FilePath);
            settings = (await JsonSerializer.DeserializeAsync<Settings>(stream))!;
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

    public static async Task Write(Settings settings)
    {
        TargetPosition.SetTargetOnLeft(settings.TargetOnLeft);
        MaxInstance.SetForUser(settings.MaxInstancesToLaunch);
        await WriteFile(settings);
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
            await JsonSerializer.SerializeAsync(stream, settings);
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