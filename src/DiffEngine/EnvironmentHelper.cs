static class EnvironmentHelper
{
    /// <summary>
    /// Both scopes, so a setting chosen in the tray outlives the process that chose it.
    /// <para>
    /// Swapped for <see cref="SetProcessOnly" /> by the test projects. A test would otherwise
    /// write the user environment of the machine running it, and cannot put it back reliably: the
    /// test projects run as parallel processes over the one registry key, so a capture in one and
    /// a restore in the other race, and the value that loses is gone.
    /// </para>
    /// </summary>
    internal static Action<string, string?> Set = SetUserAndProcess;

    static void SetUserAndProcess(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
    }

    /// <summary>
    /// Nothing outside the process. For tests.
    /// </summary>
    internal static void SetProcessOnly(string name, string? value) =>
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
}
