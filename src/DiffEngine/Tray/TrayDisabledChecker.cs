static class TrayDisabledChecker
{
    public static bool IsDisabled()
    {
        var variable = Environment.GetEnvironmentVariable("DiffEngine_TrayDisabled");
        return string.Equals(variable, "true", StringComparison.OrdinalIgnoreCase);
    }
}
