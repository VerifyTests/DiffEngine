static partial class WindowsProcess
{
#if NET7_0_OR_GREATER
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        int access,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(
        SafeProcessHandle processHandle,
        int exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(
        SafeProcessHandle handle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION info,
        int size,
        out int returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWow64Process(
        SafeProcessHandle handle,
        [MarshalAs(UnmanagedType.Bool)] out bool isWow64);

    // These methods use complex marshalling not supported by LibraryImport
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadProcessMemory(
        SafeProcessHandle handle,
        IntPtr baseAddress,
        [Out] byte[] buffer,
        IntPtr size,
        out IntPtr bytesRead);
#else
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeProcessHandle OpenProcess(
        int access,
        bool inherit,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateProcess(
        SafeProcessHandle processHandle,
        int exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(
        SafeProcessHandle handle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION info,
        int size,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadProcessMemory(
        SafeProcessHandle handle,
        IntPtr baseAddress,
        [Out] byte[] buffer,
        IntPtr size,
        out IntPtr bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool IsWow64Process(SafeProcessHandle handle, out bool isWow64);
#endif

    const uint TH32CS_SNAPPROCESS = 0x00000002;
    const int PROCESS_QUERY_INFORMATION = 0x0400;
    const int PROCESS_VM_READ = 0x0010;
    const int PROCESS_TERMINATE = 0x0001;
    const int ProcessBasicInformation = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved3;
    }

    public static bool TryTerminateProcess(int processId)
    {
        // First, try graceful shutdown by closing the main window
        try
        {
            using var process = Process.GetProcessById(processId);

            // Try to close the main window gracefully
            if (process.CloseMainWindow())
            {
                // Wait up to 5 seconds for graceful exit
                if (process.WaitForExit(5000))
                {
                    return true;
                }
            }
            // If no main window or still running, fall through to force kill
        }
        catch
        {
            // Process may have already exited or be inaccessible, fall through to force kill
        }

        // Fall back to forceful termination
        using var processHandle = OpenProcess(PROCESS_TERMINATE, false, processId);
        if (processHandle.IsInvalid)
        {
            return false;
        }

        TerminateProcess(processHandle, -1);
        return true;
    }

    public static List<ProcessCommand> FindAll()
    {
        var commands = new List<ProcessCommand>();
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);

        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return commands;
        }

        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };

            if (!Process32FirstW(snapshot, ref entry))
            {
                return commands;
            }

            do
            {
                var processId = (int)entry.th32ProcessID;
                if (processId == 0)
                {
                    continue;
                }

                var commandLine = GetCommandLine(processId);
                if (commandLine != null && MatchesPattern(commandLine))
                {
                    commands.Add(new(commandLine, processId));
                }
            }
            while (Process32NextW(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return commands;
    }

    // Pattern: '% %.%.% %.%.%' - matches command lines with two file paths
    // Each path has at least one space, then path separators (like C:\foo\bar.txt)
    internal static bool MatchesPattern(string commandLine)
    {
        // Looking for pattern with path separators (backslash or forward slash)
        // The WMI pattern '% %.%.% %.%.%' looks for:
        // - anything, space, something.something.something, space, something.something.something
        // This typically matches: "program.exe path\to\file.ext path\to\file2.ext"
        var span = commandLine.AsSpan();
        var firstDotPath = FindDotSeparatedPath(span);
        if (firstDotPath < 0)
        {
            return false;
        }

        var remaining = span[(firstDotPath + 1)..];
        var spaceAfterFirst = remaining.IndexOf(' ');
        if (spaceAfterFirst < 0)
        {
            return false;
        }

        remaining = remaining[(spaceAfterFirst + 1)..];
        return FindDotSeparatedPath(remaining) >= 0;
    }

    static int FindDotSeparatedPath(CharSpan span)
    {
        // Look for pattern like: x.x.x (at least 2 dots with content between)
        var dotCount = 0;
        var lastDot = -1;
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == '.')
            {
                if (lastDot >= 0 && i - lastDot > 1)
                {
                    dotCount++;
                    if (dotCount >= 2)
                    {
                        return i;
                    }
                }
                else if (lastDot < 0)
                {
                    dotCount = 1;
                }

                lastDot = i;
            }
            else if (span[i] == ' ')
            {
                dotCount = 0;
                lastDot = -1;
            }
        }

        return -1;
    }

    static string? GetCommandLine(int processId)
    {
        using var handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, processId);
        if (handle.IsInvalid)
        {
            return null;
        }

        try
        {
            var isTarget32Bit = false;
            if (Environment.Is64BitOperatingSystem)
            {
                if (!IsWow64Process(handle, out isTarget32Bit))
                {
                    return null;
                }
            }

            var pbi = new PROCESS_BASIC_INFORMATION();
            if (NtQueryInformationProcess(handle, ProcessBasicInformation, ref pbi, Marshal.SizeOf(pbi), out _) != 0)
            {
                return null;
            }

            return Environment.Is64BitProcess && !isTarget32Bit
                ? ReadCommandLine64(handle, pbi.PebBaseAddress)
                : ReadCommandLine32(handle, pbi.PebBaseAddress);
        }
        catch
        {
            return null;
        }
    }

    static string? ReadCommandLine64(SafeProcessHandle handle, IntPtr pebAddress)
    {
        // In 64-bit PEB, ProcessParameters is at offset 0x20
        var processParametersOffset = 0x20;
        var commandLineOffset = 0x70; // UNICODE_STRING CommandLine in RTL_USER_PROCESS_PARAMETERS

        var buffer = new byte[8];
        if (!ReadProcessMemory(handle, pebAddress + processParametersOffset, buffer, new(8), out _))
        {
            return null;
        }

        var processParameters = (IntPtr)BitConverter.ToInt64(buffer, 0);
        if (processParameters == IntPtr.Zero)
        {
            return null;
        }

        // Read UNICODE_STRING structure (Length: 2, MaxLength: 2, padding: 4, Buffer: 8)
        buffer = new byte[16];
        if (!ReadProcessMemory(handle, processParameters + commandLineOffset, buffer, new(16), out _))
        {
            return null;
        }

        var length = BitConverter.ToUInt16(buffer, 0);
        var cmdLinePtr = (IntPtr)BitConverter.ToInt64(buffer, 8);

        if (length == 0 || cmdLinePtr == IntPtr.Zero)
        {
            return null;
        }

        var cmdLineBuffer = new byte[length];
        if (!ReadProcessMemory(handle, cmdLinePtr, cmdLineBuffer, new(length), out _))
        {
            return null;
        }

        return Encoding.Unicode.GetString(cmdLineBuffer).TrimEnd('\0');
    }

    static string? ReadCommandLine32(SafeProcessHandle handle, IntPtr pebAddress)
    {
        // In 32-bit PEB, ProcessParameters is at offset 0x10
        var processParametersOffset = 0x10;
        var commandLineOffset = 0x40; // UNICODE_STRING CommandLine in RTL_USER_PROCESS_PARAMETERS

        var buffer = new byte[4];
        if (!ReadProcessMemory(handle, pebAddress + processParametersOffset, buffer, new(4), out _))
        {
            return null;
        }

        var processParameters = (IntPtr)BitConverter.ToInt32(buffer, 0);
        if (processParameters == IntPtr.Zero)
        {
            return null;
        }

        // Read UNICODE_STRING structure (Length: 2, MaxLength: 2, Buffer: 4)
        buffer = new byte[8];
        if (!ReadProcessMemory(handle, processParameters + commandLineOffset, buffer, new(8), out _))
        {
            return null;
        }

        var length = BitConverter.ToUInt16(buffer, 0);
        var cmdLinePtr = (IntPtr)BitConverter.ToInt32(buffer, 4);

        if (length == 0 || cmdLinePtr == IntPtr.Zero)
        {
            return null;
        }

        var cmdLineBuffer = new byte[length];
        if (!ReadProcessMemory(handle, cmdLinePtr, cmdLineBuffer, new(length), out _))
        {
            return null;
        }

        return Encoding.Unicode.GetString(cmdLineBuffer).TrimEnd('\0');
    }
}
