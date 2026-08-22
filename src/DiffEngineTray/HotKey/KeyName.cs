/// <summary>
/// Reads the key half of a <see cref="HotKey"/>. It is a name in settings.json, so it is whatever
/// a hand edit left there rather than one of the twenty six letters the Options form offers.
/// <para>
/// Numbers and lists are rejected before parsing, because <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>
/// reads both: "1" is <see cref="Keys.LButton" />, silently binding the left mouse button, and
/// "A,B" is a flag combination rather than a key. Aliases are accepted, which is why this is not
/// a round trip through <see cref="Enum.ToString()" /> - <c>Enum.Parse</c> takes "Enter" and
/// prints "Return".
/// </para>
/// </summary>
static class KeyName
{
    public static bool TryParse([NotNullWhen(true)] string? name, out Keys key)
    {
        key = Keys.None;

        if (string.IsNullOrWhiteSpace(name) ||
            name.Contains(',') ||
            long.TryParse(name, out _))
        {
            return false;
        }

        return Enum.TryParse(name, true, out key) &&
               key != Keys.None;
    }
}
