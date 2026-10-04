using System.Net.NetworkInformation;

namespace DiffEngine;

/// <summary>
/// The operating system's table of listening TCP sockets, asked whether a port is in it.
/// <para>
/// For a caller about to connect to a local port that may have nobody on it. Such a connect is not
/// refused at once everywhere: where the reset a closed port answers with is dropped, as Windows
/// Firewall's stealth mode drops it, the connect runs for two seconds.
/// </para>
/// <para>
/// On Windows the listeners are asked for by themselves (<see cref="Listeners"/>): a tenth of a
/// millisecond, and three tenths with three thousand connections open beside them, since Windows
/// still passes over those to find the listeners. What .NET offers is every connection the
/// machine has, handed across and made into objects, with the listeners then picked out: a third
/// of a millisecond at its quietest and thirteen with those three thousand. That is still what is
/// read wherever the listeners cannot be had alone: off Windows, and on a Windows that will not
/// answer the call. So a caller with a cheaper way of knowing somebody is there should still use
/// it first.
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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            Listeners(port) is { } held)
        {
            return held;
        }

        return Enumerated(port);
    }

    /// <summary>
    /// The answer out of everything .NET lists, which on Windows is every TCP row the machine has
    /// with the listening ones kept. What is asked wherever <see cref="Listeners"/> has no answer.
    /// </summary>
    internal static bool Enumerated(int port)
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

    /// <summary>
    /// The answer out of Windows' own table of listeners, which holds a row for each listening
    /// socket and none for a connection, so what is handed over and looked through does not grow
    /// with what the machine is doing. Null when it could not be had, for whatever reason: the
    /// call is missing, it failed, or what came back is not laid out as expected.
    /// <para>
    /// Both families, since which one a listener is listed under follows how it was bound, and a
    /// loopback connect can reach either. A machine with IPv6 off has no second table to ask for.
    /// </para>
    /// </summary>
    internal static bool? Listeners(int port)
    {
        try
        {
            // MIB_TCPROW_OWNER_PID, whose third field is the local port
            var held = Read(port, interNetwork, 24, 8);
            if (held != false ||
                !Socket.OSSupportsIPv6)
            {
                return held;
            }

            // MIB_TCP6ROW_OWNER_PID, where the port follows the address and its scope
            return Read(port, interNetworkV6, 56, 20);
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return null;
        }
    }

    static bool? Read(int port, int family, int rowSize, int portOffset)
    {
        var size = 4096;
        // A table that outgrew the buffer says how much it needs, and can have grown again by
        // the time it is asked for with that. Not for ever: a call that keeps saying so is one
        // that is not going to answer
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var table = new byte[size];
            var result = GetExtendedTcpTable(table, ref size, 0, family, ownerPidListener, 0);
            if (result == insufficientBuffer)
            {
                size = Math.Max(size, table.Length * 2);
                continue;
            }

            if (result != 0)
            {
                return null;
            }

            // The count, and then the rows
            var count = BitConverter.ToInt32(table, 0);
            if (count < 0 ||
                4 + (long) count * rowSize > table.Length)
            {
                return null;
            }

            for (var index = 0; index < count; index++)
            {
                // Network byte order, in the low half of a field twice the size
                var at = 4 + index * rowSize + portOffset;
                if (((table[at] << 8) | table[at + 1]) == port)
                {
                    return true;
                }
            }

            return false;
        }

        return null;
    }

    const int interNetwork = 2;
    const int interNetworkV6 = 23;

    // TCP_TABLE_OWNER_PID_LISTENER. The basic class has rows that are four bytes shorter, and is
    // not documented for IPv6
    const int ownerPidListener = 3;

    const uint insufficientBuffer = 122;

    [DllImport("iphlpapi.dll")]
    static extern uint GetExtendedTcpTable(
        [Out] byte[] table,
        ref int size,
        int order,
        int family,
        int tableClass,
        int reserved);
}
