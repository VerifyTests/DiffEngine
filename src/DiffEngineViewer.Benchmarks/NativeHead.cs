#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

/// <summary>
/// The Linux head's window, opened by a benchmark and turned the way <c>ViewerProgram</c>'s loop
/// turns it: a screen presented through <see cref="NativeViewerWindow"/>, then input polled.
/// <para>
/// A frame's time on the clock says little about what it cost. The shim holds the loop to sixty
/// frames a second, so a frame that took one millisecond to draw and one that took ten both come
/// back after 16.7. What is counted around every turn instead is the processor time the process
/// spent, on every thread, since a software rasteriser spreads a frame over several, and what was
/// drawn, which is asked of OpenGL itself: the primitives generated between the start of a turn
/// and its end are the triangles the shim submitted, whichever rasteriser then filled them, and a
/// turn that generated none put nothing on the screen.
/// </para>
/// <para>
/// Every call into the shim has to come from the thread that opened the window, and so does every
/// call into OpenGL, since the GL context belongs to that thread. BenchmarkDotNet's in process
/// runner gives each benchmark case a thread of its own and runs its setup, its iterations and its
/// cleanup there, so a window opened in a case's setup and closed in its cleanup keeps to that.
/// </para>
/// </summary>
sealed unsafe class NativeHead : IDisposable
{
    const string shim = "diffengine_viewer";

    /// <summary>
    /// What every turn since the process started has added up to, for
    /// <see cref="NativeHeadDiagnoser"/> to take the difference of across a run.
    /// </summary>
    public static NativeHeadTotals Totals;

    static string? shimPath;
    static bool resolved;
    static int? server;
    static bool serverSought;

    readonly IViewerWindow window;
    readonly uint query;

    NativeHead(IViewerWindow window, uint query)
    {
        this.window = window;
        this.query = query;
    }

    /// <summary>
    /// Whether there is a shim to load and a display to open its window on. False on Windows,
    /// whose head draws with WinForms, and on macOS, where a window may only be made on the main
    /// thread, so the benchmarks that need this are left out of a run there rather than failing it.
    /// </summary>
    public static bool Available =>
        OperatingSystem.IsLinux() &&
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) &&
        TryFindShim(out _);

    /// <summary>
    /// The window's size in character cells, as the shim measured it from the font it loaded.
    /// </summary>
    public int Columns { get; private set; }

    public int Rows { get; private set; }

    /// <summary>
    /// Opens the window on screen rather than hidden: a frame put into a window nobody can see
    /// costs the rasteriser the same and the window system nothing, and the second is part of
    /// what a frame costs.
    /// </summary>
    public static NativeHead Open(int width, int height)
    {
        if (!resolved)
        {
            // In place of NativeResolver.Register, which only looks beside the assembly. A
            // resolver can be set once for an assembly, and nothing else in this process sets one.
            resolved = true;
            NativeLibrary.SetDllImportResolver(typeof(NativeViewerWindow).Assembly, Resolve);
        }

        var window = NativeViewerWindow.Open("DiffEngineViewer", width, height, false, null, out var error);
        if (window is null)
        {
            throw new InvalidOperationException(error);
        }

        uint query;
        Gl.GenQueries(1, &query);
        var head = new NativeHead(window, query);

        // The first frame is drawn before the window's size in cells is known, as the loop's is.
        head.Turn(ScreenBuilder.Build(SessionState.Start(ViewerMode.File)), out var input);
        head.Columns = input.Columns;
        head.Rows = input.Rows;
        return head;
    }

    /// <summary>
    /// One turn of the loop.
    /// </summary>
    public bool Turn(Screen screen) =>
        Turn(screen, out _);

    bool Turn(Screen screen, out ViewerInput input)
    {
        var before = ProcessorTime();
        Gl.BeginQuery(Gl.PrimitivesGenerated, query);
        var open = window.Present(screen);
        Gl.EndQuery(Gl.PrimitivesGenerated);
        uint triangles;
        Gl.GetQueryObject(query, Gl.QueryResult, &triangles);
        input = window.Poll();

        Totals.ProcessorTime += ProcessorTime() - before;
        Totals.Triangles += triangles;
        Totals.Turns++;
        if (triangles > 0)
        {
            Totals.Drawn++;
        }

        return open;
    }

    /// <summary>
    /// Turns the loop for as long as it takes a screen to come to rest: its pictures decoded on
    /// the shim's own thread and handed over, and whatever the shim does in the frames after a
    /// change done with.
    /// </summary>
    public void Settle(Screen screen, int turns = 180)
    {
        for (var turn = 0; turn < turns; turn++)
        {
            Turn(screen);
        }
    }

    public void Dispose() =>
        window.Dispose();

    static nint Resolve(string library, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (library == shim &&
            TryFindShim(out var path))
        {
            return NativeLibrary.Load(path);
        }

        return nint.Zero;
    }

    /// <summary>
    /// The shim the Linux head in this checkout ships: beside this assembly if a build put it
    /// there, and otherwise in that head's own runtimes folder, which is where the CI job copies
    /// the one it has just built from source.
    /// </summary>
    static bool TryFindShim([NotNullWhen(true)] out string? path)
    {
        if (shimPath is not null)
        {
            path = shimPath;
            return true;
        }

        if (NativeResolver.TryFind(out path))
        {
            shimPath = path;
            return true;
        }

        var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "DiffEngineViewer.Linux",
                "runtimes",
                $"linux-{architecture}",
                "native",
                $"lib{shim}.so");
            if (File.Exists(candidate))
            {
                path = shimPath = candidate;
                return true;
            }
        }

        path = null;
        return false;
    }

    /// <summary>
    /// Processor time of this process in nanoseconds, every thread included. From the clock
    /// rather than <see cref="Process.TotalProcessorTime"/>, which counts in hundredths of a second.
    /// </summary>
    static long ProcessorTime()
    {
        Timespec time;
        // CLOCK_PROCESS_CPUTIME_ID
        ClockGetTime(2, &time);
        return time.Seconds * 1_000_000_000 + time.Nanoseconds;
    }

    /// <summary>
    /// Processor time of the X server this process draws to, in nanoseconds, or -1 when it cannot
    /// be found. Putting a frame on the screen is work for the server too: under Xvfb with a
    /// software rasteriser it copies every pixel of the window, and none of that is counted
    /// against this process.
    /// </summary>
    public static long ServerProcessorTime()
    {
        try
        {
            if (ServerProcess() is not { } process)
            {
                return -1;
            }

            // One line a thread: the nanoseconds it has run, the nanoseconds it has waited to,
            // and how many times it has been scheduled.
            long total = 0;
            foreach (var task in Directory.EnumerateDirectories($"/proc/{process}/task"))
            {
                var fields = File.ReadAllText(Path.Combine(task, "schedstat")).Split(' ');
                total += long.Parse(fields[0], CultureInfo.InvariantCulture);
            }

            return total;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            return -1;
        }
    }

    /// <summary>
    /// The server whose command line names the display this process was given.
    /// </summary>
    static int? ServerProcess()
    {
        if (serverSought)
        {
            return server;
        }

        serverSought = true;
        var display = Environment.GetEnvironmentVariable("DISPLAY") ?? "";
        var colon = display.LastIndexOf(':');
        if (colon < 0)
        {
            return null;
        }

        // ":99" or "host:99.0"
        var number = ":" + display[(colon + 1)..].Split('.')[0];
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var process))
            {
                continue;
            }

            string[] arguments;
            try
            {
                arguments = File.ReadAllText(Path.Combine(directory, "cmdline")).Split('\0');
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (Path.GetFileName(arguments[0]) is "Xvfb" or "Xorg" or "X" or "Xwayland" &&
                arguments.Contains(number))
            {
                server = process;
                break;
            }
        }

        return server;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Timespec
    {
        public long Seconds;
        public long Nanoseconds;
    }

    [DllImport("libc", EntryPoint = "clock_gettime")]
    static extern int ClockGetTime(int clock, Timespec* time);

    /// <summary>
    /// The four OpenGL calls a query takes, from the library the shim's own context came from.
    /// </summary>
    static class Gl
    {
        const string library = "libGL.so.1";

        public const uint PrimitivesGenerated = 0x8C87;
        public const uint QueryResult = 0x8866;

        [DllImport(library, EntryPoint = "glGenQueries")]
        public static extern void GenQueries(int count, uint* ids);

        [DllImport(library, EntryPoint = "glBeginQuery")]
        public static extern void BeginQuery(uint target, uint id);

        [DllImport(library, EntryPoint = "glEndQuery")]
        public static extern void EndQuery(uint target);

        [DllImport(library, EntryPoint = "glGetQueryObjectuiv")]
        public static extern void GetQueryObject(uint id, uint name, uint* value);
    }
}

/// <summary>
/// What turns of the loop have added up to.
/// </summary>
struct NativeHeadTotals
{
    /// <summary>
    /// Runs of a benchmark method, which is what a figure is reported per: one turn for a
    /// benchmark of a frame, sixty for a benchmark of a second.
    /// </summary>
    public long Operations;

    public long Turns;

    /// <summary>
    /// Turns that submitted anything to draw.
    /// </summary>
    public long Drawn;

    public long Triangles;

    /// <summary>
    /// Nanoseconds of processor time this process spent inside turns.
    /// </summary>
    public long ProcessorTime;
}
