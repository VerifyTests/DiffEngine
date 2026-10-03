namespace DiffEngine;

/// <summary>
/// Whether a copy of the viewer is new enough to be started the way <see cref="ViewerLauncher" />
/// starts one.
/// <para>
/// The copy that resolves is not always the one this library was built beside. A globally
/// installed tool and the copy shipped with a tray are both looked for ahead of the bundled one,
/// and either can be older than the library about to launch it. Two things a viewer is launched
/// with arrived together in 20.5.0: the patch in a <c>--payload</c> file, which a viewer from
/// before then exits on as an argument it does not know, and ShellExecute, which gives a viewer
/// from before then - a console executable - a console window to sit beside its own.
/// </para>
/// <para>
/// Resolution passes over a copy known to be older when a newer one is there to be found, and
/// still takes it when it is the only one: for everything but an inline patch an older viewer is
/// better than none, and <see cref="ViewerLaunchGate" /> reports the launch it fails.
/// </para>
/// <para>
/// A copy that does not say what version it is counts as new enough, since passing it over would
/// be acting on nothing. The version read is the one stamped on the executable, or on the assembly
/// beside it where the executable carries none, which is every apphost off Windows.
/// </para>
/// </summary>
static class ViewerContract
{
    /// <summary>
    /// Two parts, so that it is not above a version written with only two.
    /// </summary>
    static readonly Version first = new(20, 5);

    public static bool IsMet(string executable) =>
        IsMetBy(ProductVersion(executable));

    /// <summary>
    /// By the numbers alone. What follows them is a prerelease label or the source revision the
    /// SDK appends, and neither changes which side of 20.5 a copy is on.
    /// </summary>
    internal static bool IsMetBy(string? productVersion)
    {
        if (productVersion is null)
        {
            return true;
        }

        var end = productVersion.IndexOfAny(['+', '-']);
        var numbers = end < 0 ? productVersion : productVersion.Substring(0, end);
        if (!Version.TryParse(numbers, out var version))
        {
            return true;
        }

        return version >= first;
    }

    /// <summary>
    /// Null for a copy that carries no version, and for one that cannot be read. This runs while
    /// DiffTools initialises, where anything thrown is a TypeInitializationException for every
    /// later use of it in the process, so nothing is allowed out.
    /// </summary>
    internal static string? ProductVersion(string executable)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(executable).ProductVersion;
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }

            var assembly = Path.ChangeExtension(executable, ".dll");
            if (!File.Exists(assembly))
            {
                return null;
            }

            version = FileVersionInfo.GetVersionInfo(assembly).ProductVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch
        {
            return null;
        }
    }
}
