/// <summary>
/// Linked into every test project, and called first from each module initializer, before anything
/// can touch DiffTools, which reads the environment once and caches what it found.
/// </summary>
static class MachineSettings
{
    /// <summary>
    /// Every <c>DiffEngine_*</c> variable is a preference of the machine running the tests, and the
    /// tests assert on the defaults. A developer with <c>DiffEngine_ToolOrder</c> naming a tool the
    /// test process cannot resolve failed every test touching DiffTools, from its type initializer,
    /// while CI, which sets none of them, passed. Tests about a variable set it themselves.
    /// </summary>
    public static void Ignore()
    {
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>())
        {
            if (name.StartsWith("DiffEngine_", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals(PixelTests, StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }
    }

    /// <summary>
    /// What switches the native pixel snapshots on. A setting of the tests rather than of
    /// DiffEngine, which reads nothing by this name, but it starts with the same letters, and the
    /// prefix is matched without regard to case because Windows matches names that way. Cleared
    /// with the rest, it was gone before the attribute that reads it ran: the pixel snapshots were
    /// skipped on the CI job that sets it, on both platforms, and nothing said so.
    /// </summary>
    public const string PixelTests = "DIFFENGINE_VIEWER_PIXEL_TESTS";
}
