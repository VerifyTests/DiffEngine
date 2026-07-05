public class SettingsHelperTests
{
    [Test]
    public async Task ReadWrite()
    {
        // SettingsHelper.Write persists MaxInstances/TargetOnLeft to the User-scope environment
        // (registry on Windows), so capture and restore them to leave the machine untouched.
        var originalPath = SettingsHelper.FilePath;
        var originalMaxInstances = Environment.GetEnvironmentVariable("DiffEngine_MaxInstances", EnvironmentVariableTarget.User);
        var originalTargetOnLeft = Environment.GetEnvironmentVariable("DiffEngine_TargetOnLeft", EnvironmentVariableTarget.User);
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
