static class ProcessEx
{
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    static extern SafeProcessHandle OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    const int processQueryInfo = 0x0400;

    public static bool TryGet(int id, [NotNullWhen(true)] out Process? process)
    {
        using (var handle = OpenProcess(processQueryInfo, false, id))
        {
            if (handle.IsInvalid)
            {
                process = null;
                return false;
            }
        }

        Process? opened = null;
        try
        {
            opened = Process.GetProcessById(id);
            process = opened;
            // Forces the OS handle open, and keeps it. GetProcessById holds none of its own, so
            // Kill, HasExited and MainWindowHandle each re-open the id at the moment they are
            // called - and a tracked move outlives its diff tool by design, since HandleScanMove
            // keeps it while the temp file is still there. Hours later that id may belong to
            // something else, and "Accept all" or "Open diff tool" would kill whatever it is.
            // An open handle also stops Windows handing the id out again while this move is
            // tracked, so there is nothing to confuse it with
            _ = process.Handle;
            return true;
        }
        catch (ArgumentException)
        {
            // Handle Race condition if process doesnt exists
            process = null;
            return false;
        }
        catch (Exception exception)
            when (exception is Win32Exception or InvalidOperationException)
        {
            // The handle could not be held - it exited between the probe above and here, or this
            // account cannot open it. Without one there is no way to tell the process apart from a
            // later holder of the same id, so it is better tracked as no process at all: the tool
            // is then not killed, rather than something else being killed in its place
            opened?.Dispose();
            process = null;
            return false;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, [Out] char[] name, ref int size);

    /// <summary>
    /// The process a move names, when it is running the tool the move names.
    /// <para>
    /// The id arrives from another process and is a claim. A library from before
    /// <c>ProcessCleanup.StillRunning</c> lists the diff tools once, as its test run begins, and
    /// sends the id of one closed since then, which Windows may have handed to anything. Held and
    /// tracked on the id alone, that stranger was what an accept ended.
    /// </para>
    /// <para>
    /// A process that is not known to be the tool is tracked as no process, so it is not ended
    /// and the pair does not count as open. That is every case the comparison cannot settle, each
    /// on purpose:
    /// </para>
    /// <list type="bullet">
    /// <item>A move naming no tool. There is nothing to compare with, and no library sends an id
    /// without one.</item>
    /// <item>A tool started through a script (<c>code.cmd</c>, <c>rider.cmd</c>). The id is the
    /// command interpreter's. Ending that closes no diff window, the script having handed over to
    /// the real program, and taking any cmd.exe for the tool is how somebody's shell would be
    /// ended.</item>
    /// <item>An image that cannot be read. One this account cannot open was never held by
    /// <see cref="TryGet"/> either, and one that has exited has nothing left to end.</item>
    /// <item>Another copy of the tool, which is running the right image for another pair or for
    /// somebody's own use: see <see cref="WasStartedFor"/>.</item>
    /// </list>
    /// <para>
    /// A launcher that is itself an executable - a shim that starts the real tool and waits - is
    /// the image the sender started and the id it sent, so it matches as it always did.
    /// </para>
    /// </summary>
    /// <param name="temp">
    /// The received file of the move, which the tool showing the pair was started with: see
    /// <see cref="WasStartedFor"/>.
    /// </param>
    public static bool TryGetTool(int id, string? exe, string temp, [NotNullWhen(true)] out Process? process)
    {
        if (!TryGet(id, out process))
        {
            return false;
        }

        var image = ImagePath(process);
        if (!IsSameExecutable(image, exe))
        {
            Log.Warning(
                "Process {Id} is not tracked as the diff tool: it is running `{Image}` and the move names `{Exe}`",
                id,
                image ?? "an image that could not be read",
                exe ?? "no tool");
            process.Dispose();
            process = null;
            return false;
        }

        if (WasStartedFor(CommandLine(process), temp))
        {
            return true;
        }

        Log.Warning(
            "Process {Id} is not tracked as the diff tool: it is running `{Image}` and was not started with `{Temp}`",
            id,
            image,
            temp);
        process.Dispose();
        process = null;
        return false;
    }

    /// <summary>
    /// Whether a process running the tool is the one showing this pair, by whether it was started
    /// with the received file on its command line.
    /// <para>
    /// The image says which program a process is and not which window. A stale id handed to
    /// another copy of the same tool - the one open on the next snapshot along, or one somebody
    /// started by hand for a merge - passed on the image, was tracked as this pair's, and was
    /// ended when the pair was accepted. Every tool DiffEngine starts is given the two paths as
    /// arguments, the received file among them exactly as the move carries it, and
    /// <c>ProcessCleanup.StillRunning</c> asks the same of the command line before a newer library
    /// sends an id at all. This is that question asked again here, for a library that does not.
    /// </para>
    /// <para>
    /// A process's start time was the other candidate and settles nothing: the id is checked as
    /// the move arrives, when every process there is to find was started before it, the one that
    /// took over a closed tool's id included.
    /// </para>
    /// <para>
    /// A command line that cannot be read is not known to be the tool's, and is treated as every
    /// other such case is.
    /// </para>
    /// </summary>
    internal static bool WasStartedFor(string? commandLine, string temp)
    {
        if (commandLine is null ||
            temp.Length == 0)
        {
            return false;
        }

        var from = 0;
        while (true)
        {
            var found = commandLine.IndexOf(temp, from, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return false;
            }

            // The whole of an argument's path, and not the start of a longer one: the tool open
            // on `Sample.received.txt.bak` was not started for `Sample.received.txt`
            var end = found + temp.Length;
            if (end == commandLine.Length ||
                commandLine[end] == '"' ||
                char.IsWhiteSpace(commandLine[end]))
            {
                return true;
            }

            from = found + 1;
        }
    }

    [DllImport("ntdll.dll", ExactSpelling = true)]
    static extern int NtQueryInformationProcess(SafeProcessHandle process, int informationClass, IntPtr information, int length, out int returned);

    // ProcessCommandLineInformation. Answers with a UNICODE_STRING and the characters after it, in
    // one buffer, and needs no more than the right to query the process: nothing is read out of
    // the other process's memory, so it is the same call whatever the bitness of either side
    const int processCommandLine = 60;

    /// <summary>
    /// The command line a process was started with, asked of the handle that is held, or null
    /// when it will not say.
    /// </summary>
    internal static string? CommandLine(Process process)
    {
        try
        {
            var handle = process.SafeHandle;
            // Fails for want of room, and says how much it wants
            NtQueryInformationProcess(handle, processCommandLine, IntPtr.Zero, 0, out var length);
            if (length <= 0)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (NtQueryInformationProcess(handle, processCommandLine, buffer, length, out _) != 0)
                {
                    return null;
                }

                // Length in bytes, then MaximumLength, then the pointer at its natural alignment
                var bytes = (ushort) Marshal.ReadInt16(buffer);
                var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                if (text == IntPtr.Zero)
                {
                    return null;
                }

                return Marshal.PtrToStringUni(text, bytes / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception exception)
            when (exception is Win32Exception or InvalidOperationException or EntryPointNotFoundException)
        {
            // Exited, or no longer this account's to ask
            return null;
        }
    }

    /// <summary>
    /// By file name and not by path. One executable has several paths - through a junction, a
    /// substituted drive, a short name, another case - and the one the sender resolved need not be
    /// the one the system reports, so comparing whole paths would stop closing tools that should
    /// be closed. What that gives up is telling one copy of a tool from another copy of it.
    /// </summary>
    internal static bool IsSameExecutable(string? image, string? exe) =>
        image is not null &&
        exe is not null &&
        string.Equals(Path.GetFileName(image), Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Asked of the handle that is held, so it is the process that would be ended that answers,
    /// and through the call that needs the least access to it.
    /// </summary>
    static string? ImagePath(Process process)
    {
        try
        {
            // The longest a path can be
            var name = new char[32768];
            var size = name.Length;
            if (QueryFullProcessImageName(process.SafeHandle, 0, name, ref size))
            {
                return new(name, 0, size);
            }
        }
        catch (Exception exception)
            when (exception is Win32Exception or InvalidOperationException)
        {
            // Exited, or no longer this account's to ask
        }

        return null;
    }

    public static void KillAndDispose(this Process process)
    {
        // Capture identity up front. Once the process has exited, Id/MainModule can throw,
        // so reading them in the error handlers below could mask the real failure.
        var description = Describe(process);
        try
        {
            process.Kill();
            var exited = process.WaitForExit(500);
            if (!exited)
            {
                ExceptionHandler.Handle($"Failed to kill process. {description}");
            }
        }
        catch (InvalidOperationException)
        {
            // Race condition can cause "No process is associated with this object"
        }
        catch (Win32Exception)
        {
            // no permission or already closed
            // https://github.com/VerifyTests/DiffEngine/issues/542
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle($"Failed to kill process. {description}", exception);
        }
        finally
        {
            process.Dispose();
        }
    }

    internal static string Describe(Process process)
    {
        try
        {
            return $"Id:{process.Id} Name: {process.MainModule?.FileName}";
        }
        catch (Exception)
        {
            // Id/MainModule can throw for an exited, disposed or inaccessible process.
            return "Id: unknown";
        }
    }
}