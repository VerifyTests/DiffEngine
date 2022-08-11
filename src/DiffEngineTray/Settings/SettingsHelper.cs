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
        File.Delete(FilePath);
        await using var stream = File.OpenWrite(FilePath);
        await JsonSerializer.SerializeAsync(stream, settings);
    }
}