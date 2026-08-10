namespace DiffEngine;

/// <summary>
/// The wire format shared by requests and responses. Deliberately not JSON, for the same reason
/// <see cref="InlinePatchFile"/> is not: every value is base64, so snapshot text containing
/// quotes, braces or newlines needs no escaping and the `inline` body can carry an
/// <see cref="InlinePatchFile"/> payload verbatim rather than nested inside a JSON string.
/// <code>
/// version: 1
/// verb: inline
/// key: {base64}
/// body: {base64}
/// </code>
/// </summary>
static class ViewerPayload
{
    public const int Version = 1;

    public static void Append(StringBuilder builder, string name, string? value)
    {
        if (value is null)
        {
            return;
        }

        builder.Append(name);
        builder.Append(": ");
        builder.Append(Encode(value));
        builder.Append('\n');
    }

    public static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    public static bool TryDecode(string value, [NotNullWhen(true)] out string? decoded)
    {
        decoded = null;
        if (value.Length == 0)
        {
            decoded = "";
            return true;
        }

        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Splits into name/value pairs, preserving order and duplicates so repeated `item` lines
    /// survive.
    /// </summary>
    public static bool TryReadLines(string text, out List<(string Name, string Value)> lines)
    {
        lines = [];
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator < 1)
            {
                return false;
            }

            lines.Add((line.Substring(0, separator), line.Substring(separator + 1).Trim()));
        }

        return lines.Count > 0;
    }

    public static bool HasVersion(IReadOnlyList<(string Name, string Value)> lines) =>
        lines.Count > 0 &&
        lines[0].Name == "version" &&
        lines[0].Value == Version.ToString();
}
