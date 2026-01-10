static class TargetPosition
{
    public static bool TargetOnLeft { get; private set; }

    static TargetPosition() =>
        TargetOnLeft = ReadTargetOnLeft().GetValueOrDefault(false);

    static bool? ReadTargetOnLeft() =>
        ParseTargetOnLeft(Environment.GetEnvironmentVariable("DiffEngine_TargetOnLeft"));

    internal static bool? ParseTargetOnLeft(string? value)
    {
        if (value == null)
        {
            return null;
        }

        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new($"Unable to parse Position from `DiffEngine_TargetOnLeft`. Must be `true` or `false`. Environment variable: {value}");
    }

    public static void SetTargetOnLeft(bool value)
    {
        if (TargetOnLeft == value)
        {
            return;
        }

        TargetOnLeft = value;
        string? envVariable;
        if (value)
        {
            envVariable = "true";
        }
        else
        {
            envVariable = null;
        }

        EnvironmentHelper.Set("DiffEngine_TargetOnLeft", envVariable);
    }
}