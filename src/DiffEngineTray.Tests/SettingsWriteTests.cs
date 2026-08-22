using System.Text.Json;

/// <summary>
/// What the settings file looks like from outside while it is being written. The tray reads it at
/// startup and fails to start when it cannot, so a save that leaves it absent or half written for
/// any length of time is a save that can end with a "Cannot start" dialog at every launch.
/// <para>
/// <see cref="SettingsHelper.WriteFile" /> rather than <see cref="SettingsHelper.Write" />, which
/// also persists two User scope environment variables - a registry write and a broadcast each,
/// which this many iterations of would take far longer than the thing under test.
/// </para>
/// </summary>
public class SettingsWriteTests :
    IDisposable
{
    [Test]
    public async Task The_file_is_never_observed_missing()
    {
        await SettingsHelper.WriteFile(new());

        using var cancellation = new CancelSource();
        var reading = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var missing = 0;
        var unreadable = new ConcurrentBag<string>();
        var reader = Task.Run(
            () =>
            {
                reading.SetResult(true);
                while (!cancellation.IsCancellationRequested)
                {
                    string text;
                    try
                    {
                        // Sharing everything, including delete, so that reading the file cannot
                        // itself be what makes a write fail
                        using var stream = new FileStream(
                            file,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                        using var streamReader = new StreamReader(stream);
                        text = streamReader.ReadToEnd();
                    }
                    catch (FileNotFoundException)
                    {
                        Interlocked.Increment(ref missing);
                        continue;
                    }
                    catch (Exception exception)
                        when (exception is IOException or UnauthorizedAccessException)
                    {
                        // The OS asking to come back, rather than the file being damaged
                        continue;
                    }

                    if (!CanBeRead(text))
                    {
                        unreadable.Add(text);
                    }
                }
            });

        await reading.Task;

        for (var index = 0; index < 200; index++)
        {
            await SettingsHelper.WriteFile(
                new()
                {
                    AlwaysKillLockingProcesses = index % 2 == 0
                });
        }

        await cancellation.CancelAsync();
        await reader;

        await Assert.That(missing).IsEqualTo(0);
        await Assert.That(unreadable).IsEmpty();
    }

    [Test]
    public async Task Leaves_nothing_beside_the_file()
    {
        await SettingsHelper.WriteFile(new());

        var beside = Directory.GetFiles(directory);

        await Assert.That(beside).HasSingleItem();
        await Assert.That(beside[0]).IsEqualTo(file);
    }

    static bool CanBeRead(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        try
        {
            return JsonSerializer.Deserialize<Settings>(text) != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public SettingsWriteTests()
    {
        originalPath = SettingsHelper.FilePath;
        directory = Path.Combine(Path.GetTempPath(), $"SettingsWriteTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(directory);
        file = Path.Combine(directory, "settings.json");
        SettingsHelper.FilePath = file;
    }

    public void Dispose()
    {
        SettingsHelper.FilePath = originalPath;
        Directory.Delete(directory, true);
    }

    string originalPath;
    string directory;
    string file;
}
