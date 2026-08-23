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
        var looks = 0;
        var unreadable = new ConcurrentBag<string>();
        // No token on Task.Run. It cancels the scheduling rather than the delegate, so a pool
        // that had not yet picked this up when the cancel lands leaves the task Canceled and
        // `await reader` throwing. The loop already exits on the token, which is the only
        // cancellation this ever wanted
        // ReSharper disable once MethodSupportsCancellation
        var reader = Task.Run(
            () =>
            {
                reading.SetResult(true);
                while (!cancellation.IsCancellationRequested)
                {
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
                        var text = streamReader.ReadToEnd();
                        if (!CanBeRead(text))
                        {
                            unreadable.Add(text);
                        }
                    }
                    catch (FileNotFoundException)
                    {
                        Interlocked.Increment(ref missing);
                    }
                    catch (Exception exception)
                        when (exception is IOException or UnauthorizedAccessException)
                    {
                        // The OS asking to come back, rather than the file being damaged
                    }

                    Interlocked.Increment(ref looks);

                    // Sampling rather than spinning. A reader that reopens the file the instant it
                    // closes it holds it for most of the time it runs, which on a two core CI
                    // machine starves the swap of every attempt it has - and that is the test
                    // being the adversary, not the file being fragile
                    Thread.Sleep(1);
                }
            });

        await reading.Task;

        // Until the reader has looked enough times for the window to have shown itself, rather
        // than a count of writes, which on a fast machine all pass between two of its looks
        for (var index = 0; Volatile.Read(ref looks) < 100 && index < 5000; index++)
        {
            await SettingsHelper.WriteFile(
                new()
                {
                    AlwaysKillLockingProcesses = index % 2 == 0
                });
        }

        await cancellation.CancelAsync();
        await reader;

        // That the reader looked at all. Everything below is a statement about what it saw
        await Assert.That(looks).IsGreaterThanOrEqualTo(100);
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
