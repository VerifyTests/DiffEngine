static class EnvironmentHelper
{
    public static void Set(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
    }
}