using System;
using System.IO;
using System.Linq;

static class Guard
{
    public static void AgainstNegativeAndZero(int value, string argumentName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(argumentName);
        }
    }

    public static void FileExists(string path, string argumentName)
    {
        AgainstEmpty(argumentName, path);
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

    public static void AgainstEmpty(object?[] value, string argumentName)
    {
        if (value == null)
        {
            throw new ArgumentNullException(argumentName);
        }

        if (value.Length == 0)
        {
            throw new ArgumentNullException(argumentName, "Argument cannot be empty.");
        }

        if (value.Any(item => item == null))
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

    public static void AgainstBadExtension(string value, string argumentName)
    {
        AgainstEmpty(value, argumentName);

        if (value.StartsWith("."))
        {
            throw new ArgumentException("Must not start with a period ('.').", argumentName);
        }
    }
}