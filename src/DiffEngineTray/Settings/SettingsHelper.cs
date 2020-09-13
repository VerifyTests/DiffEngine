using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

class SettingsHelper
{
    public static string FilePath;

    static SettingsHelper()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string directory = Path.Combine(appData, "DiffEngine");
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "settings.json");
    }

    public static async Task<Settings> Read()
    {
        if (File.Exists(FilePath))
        {
            await using var stream = File.OpenRead(FilePath);
            return await JsonSerializer.DeserializeAsync<Settings>(stream);
        }

        await File.WriteAllTextAsync(FilePath, "{}");
        return new Settings();
    }

    public static async Task Write(Settings settings)
    {
        File.Delete(FilePath);
        await using var stream = File.OpenWrite(FilePath);
        await JsonSerializer.SerializeAsync(stream, settings);
    }
}