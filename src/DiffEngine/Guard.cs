static class Guard
{
    public static void AgainstNegative(int value, string argumentName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(argumentName);
        }
    }

    public static void FileExists(string path, string argumentName)
    {
        // (value, name), not (name, value). Reversed, this validated the literal parameter name -
        // never empty - so an empty path fell through to the message below with no ParamName on it
        AgainstEmpty(path, argumentName);
        if (!File.Exists(path))
        {
            throw new ArgumentException($"File not found. Path: {path}");
        }
    }

    public static void AgainstEmpty(string value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentNullException(argumentName);
        }
    }

    public static void AgainstEmpty<T>(T[] value, string argumentName)
    {
        if (value == null)
        {
            throw new ArgumentNullException(argumentName);
        }

        if (value.Length == 0)
        {
            throw new ArgumentNullException(argumentName, "Argument cannot be empty.");
        }
    }
}