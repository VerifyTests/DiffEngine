using System.Diagnostics;
using System.Runtime.InteropServices;

static partial class Program
{
    static dynamic? _word;
    static readonly ConsoleCtrlDelegate _consoleCtrlHandler = ConsoleCtrlHandler;

    static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: diffengine-word <path1> <path2>");
            return 1;
        }

        var path1 = Path.GetFullPath(args[0]);
        var path2 = Path.GetFullPath(args[1]);

        if (!File.Exists(path1))
        {
            Console.Error.WriteLine($"File not found: {path1}");
            return 1;
        }

        if (!File.Exists(path2))
        {
            Console.Error.WriteLine($"File not found: {path2}");
            return 1;
        }

        var wordType = Type.GetTypeFromProgID("Word.Application");
        if (wordType == null)
        {
            Console.Error.WriteLine("Microsoft Word is not installed");
            return 1;
        }

        // Create a job object that kills child processes when this process exits
        var job = CreateJobObject(IntPtr.Zero, null);
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };
        SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, (uint)Marshal.SizeOf(info));

        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        SetConsoleCtrlHandler(_consoleCtrlHandler, true);

        _word = Activator.CreateInstance(wordType)!;
        dynamic word = _word;

        // WdAlertLevel.wdAlertsNone = 0
        word.DisplayAlerts = 0;

        var doc1 = word.Documents.Open(path1, ReadOnly: true, AddToRecentFiles: false);
        var doc2 = word.Documents.Open(path2, ReadOnly: true, AddToRecentFiles: false);

        // WdCompareDestination.wdCompareDestinationNew = 2
        // WdGranularity.wdGranularityWordLevel = 1
        var comparedDoc = word.CompareDocuments(
            doc1, doc2,
            Destination: 2,
            Granularity: 1,
            CompareFormatting: true,
            CompareCaseChanges: true,
            CompareWhitespace: true,
            CompareTables: true,
            CompareHeaders: true,
            CompareFootnotes: true,
            CompareTextboxes: true,
            CompareFields: true,
            CompareComments: true,
            CompareMoves: true,
            RevisedAuthor: "",
            IgnoreAllComparisonWarnings: true);

        doc1.Close(SaveChanges: false);
        doc2.Close(SaveChanges: false);

        // Mark as saved so Word won't prompt to save on close
        comparedDoc.Saved = true;

        // Disable spelling and grammar checks
        comparedDoc.ShowSpellingErrors = false;
        comparedDoc.ShowGrammaticalErrors = false;

        word.Visible = true;

        // Get process from Word's window handle and assign to job
        var hwnd = (IntPtr)word.ActiveWindow.Hwnd;
        GetWindowThreadProcessId(hwnd, out var processId);
        using var process = Process.GetProcessById(processId);
        AssignProcessToJobObject(job, process.Handle);

        process.WaitForExit();

        // Release COM objects
        Marshal.ReleaseComObject(comparedDoc);
        Marshal.ReleaseComObject(word);
        _word = null;
        CloseHandle(job);

        return 0;
    }

    static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) =>
        CloseWord();

    static void OnProcessExit(object? sender, EventArgs e) =>
        CloseWord();

    static bool ConsoleCtrlHandler(int ctrlType)
    {
        CloseWord();
        return false;
    }

    static void CloseWord()
    {
        if (_word == null)
        {
            return;
        }

        try
        {
            _word.Quit(SaveChanges: false);
            Marshal.ReleaseComObject(_word);
        }
        catch
        {
            // Word may already be closed
        }

        _word = null;
    }

    const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    const int JobObjectExtendedLimitInformation = 9;

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, [MarshalAs(UnmanagedType.Bool)] bool add);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, uint cbJobObjectInfoLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    delegate bool ConsoleCtrlDelegate(int ctrlType);
}
