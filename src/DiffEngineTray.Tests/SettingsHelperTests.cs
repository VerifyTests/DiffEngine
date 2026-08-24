public class SettingsHelperTests
{
    const string maxInstances = "DiffEngine_MaxInstances";

    /// <summary>
    /// Saving used to leave DiffEngine_MaxInstances in the user environment on a machine that had
    /// never chosen a limit. Write is handed whatever the options form was populated with, which
    /// is the value already in effect, and unrelated saves come through here too - the "always
    /// kill locking processes" prompt persists itself this way and has no options form at all.
    /// </summary>
    [Test]
    public async Task Write_leaves_an_unchanged_max_instances_unpersisted()
    {
        var originalPath = SettingsHelper.FilePath;
        var originalProcess = Environment.GetEnvironmentVariable(maxInstances);
        var tempFile = Path.Combine(Path.GetTempPath(), $"SettingsHelperTests_{Guid.NewGuid()}.json");
        try
        {
            SettingsHelper.FilePath = tempFile;
            // The "never chose a limit" machine, whatever the one running the test has set
            Environment.SetEnvironmentVariable(maxInstances, null);
            MaxInstance.ResetAppDomainValue();

            await SettingsHelper.Write(
                new()
                {
                    // As the options form populates it, and as an unrelated save leaves it
                    MaxInstancesToLaunch = MaxInstance.MaxInstancesToLaunch
                });

            await Assert.That(Environment.GetEnvironmentVariable(maxInstances)).IsNull();
        }
        finally
        {
            SettingsHelper.FilePath = originalPath;
            File.Delete(tempFile);
            Environment.SetEnvironmentVariable(maxInstances, originalProcess);
            MaxInstance.ResetAppDomainValue();
        }
    }

    [Test]
    public async Task ReadWrite()
    {
        // SettingsHelper.Write persists MaxInstances/TargetOnLeft. The module initializer keeps
        // that inside this process, so these restore the process scope it writes.
        var originalPath = SettingsHelper.FilePath;
        var originalMaxInstances = Environment.GetEnvironmentVariable("DiffEngine_MaxInstances");
        var originalTargetOnLeft = Environment.GetEnvironmentVariable("DiffEngine_TargetOnLeft");
        var tempFile = Path.Combine(Path.GetTempPath(), $"SettingsHelperTests_{Guid.NewGuid()}.json");
        try
        {
            SettingsHelper.FilePath = tempFile;
            await SettingsHelper.Write(
                new()
                {
                    AcceptAllHotKey = new()
                    {
                        Key = "T"
                    },
                    MaxInstancesToLaunch = 5,
                    TargetOnLeft = false,
                    AlwaysKillLockingProcesses = true
                });

            var result = await SettingsHelper.Read();

            await Verify(result);
        }
        finally
        {
            SettingsHelper.FilePath = originalPath;
            File.Delete(tempFile);
            EnvironmentHelper.Set("DiffEngine_MaxInstances", originalMaxInstances);
            EnvironmentHelper.Set("DiffEngine_TargetOnLeft", originalTargetOnLeft);
        }
    }
}
