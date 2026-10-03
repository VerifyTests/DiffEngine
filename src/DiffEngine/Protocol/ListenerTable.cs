using System.Net.NetworkInformation;

namespace DiffEngine;

/// <summary>
/// The operating system's table of listening TCP sockets, asked whether a port is in it.
/// <para>
/// For a caller about to connect to a local port that may have nobody on it. Such a connect is not
/// refused at once everywhere: where the reset a closed port answers with is dropped, as Windows
/// Firewall's stealth mode drops it, the connect runs for two seconds. Reading the table takes a
/// third of a millisecond on a machine with a few dozen connections open. It is filtered out of
/// every connection the machine has, so that grows with them, and a caller with a cheaper way of
/// knowing somebody is there should use it first.
/// </para>
/// <para>
/// By port alone, whichever address the listener is bound to. The table is only ever believed
/// when it says nobody is there, so a listener on some address a loopback connect does not reach -
/// the other family's loopback, one interface - costs the caller the connect it would have made
/// anyway. A rule about addresses would have to be right for every family and socket option, and
/// where it was wrong it would cost a listener that was there: one bound to every address in dual
/// mode is listed on Windows under both families, and nothing says another platform lists it so.
/// </para>
/// </summary>
static class ListenerTable
{
    /// <summary>
    /// False only when the table was read and has no listener on <paramref name="port"/>. A table
    /// that cannot be read says true, whatever stopped it being read, which leaves the connect to
    /// decide as it did before anything asked here.
    /// </summary>
    public static bool IsHeld(int port)
    {
        try
        {
            return Lookup(port);
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// What <see cref="IsHeld"/> asks. Replaced by the tests, for the two things about the table
    /// that cannot be arranged or seen from outside: that it could not be read, and that it was
    /// not asked at all. Given the port, so a test can answer for its own and pass every other
    /// through: the tests beside it are asking about theirs at the same time.
    /// </summary>
    internal static Func<int, bool> Lookup { get; set; } = Listed;

    static bool Listed(int port)
    {
        foreach (var listener in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
        {
            if (listener.Port == port)
            {
                return true;
            }
        }

        return false;
    }
}
