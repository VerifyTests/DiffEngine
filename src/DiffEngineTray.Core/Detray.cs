/// <summary>
/// The macOS tray's native library, native/include/detray.h, which the Mac head carries under
/// runtimes/{rid}/native. In the core, as ProcessEx's kernel32 and libproc calls are: it compiles
/// anywhere and is called only on macOS, and the core's tests can then run it there.
/// </summary>
static class Detray
{
    public const string Library = "diffengine_tray";

    /// <summary>
    /// Must match DETRAY_VERSION in native/include/detray.h, so a stale library is reported rather
    /// than read as garbage.
    /// </summary>
    public const int ExpectedVersion = 1;

    public const int Enabled = 1;
    public const int Separator = 2;
    public const int Checkable = 4;
    public const int Checked = 8;

    /// <summary>
    /// Has PlatformHeads' resolver find <see cref="Library" /> under runtimes/{rid}/native for these
    /// imports. Once a process, however many threads ask.
    /// </summary>
    public static void Resolve() =>
        NativeResolver.Register(typeof(Detray).Assembly, Library);

    [DllImport(Library, EntryPoint = "detray_version")]
    public static extern int Version();

    [DllImport(Library, EntryPoint = "detray_init")]
    public static extern int Init();

    [DllImport(Library, EntryPoint = "detray_set_icon")]
    public static extern void SetIcon(int icon, byte[] png, int length);

    [DllImport(Library, EntryPoint = "detray_set_menu_icon")]
    public static extern void SetMenuIcon([MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte[] png, int length);

    [DllImport(Library, EntryPoint = "detray_apply")]
    public static extern void Apply(int icon, [MarshalAs(UnmanagedType.LPUTF8Str)] string tooltip, DetrayItem[] items, int count);

    [DllImport(Library, EntryPoint = "detray_pump")]
    public static extern void Pump();

    [DllImport(Library, EntryPoint = "detray_poll")]
    public static extern int Poll(byte[] buffer, int capacity);

    [DllImport(Library, EntryPoint = "detray_quitting")]
    public static extern int Quitting();

    [DllImport(Library, EntryPoint = "detray_describe")]
    public static extern int Describe(byte[] buffer, int capacity);

    [DllImport(Library, EntryPoint = "detray_shutdown")]
    public static extern void Shutdown();
}
