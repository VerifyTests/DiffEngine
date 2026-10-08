namespace DiffEngine;

// ReSharper disable PartialTypeWithSinglePart
static partial class WindowsProcess
{
#if NET7_0_OR_GREATER
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(
        IntPtr applicationName,
        IntPtr commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        IntPtr currentDirectory,
        ref STARTUPINFOW startupInfo,
        out PROCESS_INFORMATION processInformation);
#else
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CreateProcessW(
        IntPtr applicationName,
        IntPtr commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        IntPtr currentDirectory,
        ref STARTUPINFOW startupInfo,
        out PROCESS_INFORMATION processInformation);
#endif

    const uint createNoWindow = 0x08000000;

    [StructLayout(LayoutKind.Sequential)]
    struct STARTUPINFOW
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    /// <summary>
    /// Starts a process that takes nothing from this one, and returns its id.
    /// <para>
    /// For the diff tools declared <c>UseShellExecute: false</c>. A test host's output goes to a
    /// pipe that <c>dotnet test</c> reads until every writer has closed it, and
    /// <see cref="Process.Start()" /> without ShellExecute always asks Windows to hand the child
    /// every inheritable handle, the write end of that pipe among them, whatever is or is not
    /// redirected. So a run that opened one of those tools did not return until the process it
    /// had started was gone, and the Word comparer's stays until Word is closed. That is
    /// https://github.com/VerifyTests/Verify/issues/1229 again, for the tools it was not fixed for.
    /// </para>
    /// <para>
    /// Not through ShellExecute, which is how every other tool avoids it. A tool is declared
    /// without it because ShellExecute would give it a console window: it is a console program,
    /// or a script the command interpreter runs. That window can be asked for hidden, but the
    /// request travels to the program as how its first window is to be shown, so a console
    /// program that opens a window of its own - as tools built with Go, Rust or Java commonly
    /// are - never appears. Nothing in the file says which kind of console program it is.
    /// </para>
    /// <para>
    /// So this is the same call <see cref="Process.Start()" /> makes, with the one argument it
    /// does not expose turned off. With no console as well, whatever the tool declared: a console
    /// program that shares this process's console is given its standard handles along with it,
    /// inherited or not, and those are the pipe.
    /// </para>
    /// </summary>
    public static int StartInheritingNothing(string exePath, string arguments)
    {
        var startupInfo = new STARTUPINFOW
        {
            cb = Marshal.SizeOf<STARTUPINFOW>()
        };
        // A buffer rather than a string, because CreateProcess is allowed to write to it
        var commandLine = Marshal.StringToHGlobalUni(CommandLine(exePath, arguments));
        try
        {
            if (!CreateProcessW(
                    IntPtr.Zero,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    createNoWindow,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    ref startupInfo,
                    out var process))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
            return process.dwProcessId;
        }
        finally
        {
            Marshal.FreeHGlobal(commandLine);
        }
    }

    /// <summary>
    /// The quoted path and then the arguments, which is what <see cref="Process.Start()" /> builds
    /// and what ProcessCleanup looks a running tool up by.
    /// </summary>
    internal static string CommandLine(string exePath, string arguments)
    {
        var quoted = exePath.StartsWith('"') && exePath.EndsWith('"') ? exePath : $"\"{exePath}\"";
        if (arguments.Length == 0)
        {
            return quoted;
        }

        return $"{quoted} {arguments}";
    }
}
