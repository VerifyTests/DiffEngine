// ReSharper disable RedundantUnsafeContext
/// <summary>
/// The whole native surface. Twelve or so entry points rather than a binding for all of Dear
/// ImGui, because the shim is a renderer for <see cref="Screen"/> and nothing more.
/// </summary>
static unsafe partial class Deview
{
    const string library = "diffengine_viewer";

    /// <summary>
    /// Must match DEVIEW_VERSION in native/include/deview.h. Bumped whenever the structs change,
    /// so a stale native library is reported rather than read as garbage.
    /// </summary>
    public const int ExpectedVersion = 7;

    [LibraryImport(library, EntryPoint = "deview_version")]
    public static partial int Version();

    [LibraryImport(library, EntryPoint = "deview_init", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Init(
        int width,
        int height,
        string title,
        byte* fontTtf,
        int fontLength,
        float fontSize,
        int hidden);

    [LibraryImport(library, EntryPoint = "deview_present")]
    public static partial int Present(DeviewScreen* screen);

    [LibraryImport(library, EntryPoint = "deview_poll_input")]
    public static partial void PollInput(DeviewInput* input);

    [LibraryImport(library, EntryPoint = "deview_capture", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Capture(DeviewScreen* screen, int width, int height, string pngPath);

    [LibraryImport(library, EntryPoint = "deview_set_hidden")]
    public static partial void SetHidden(int hidden);

    [LibraryImport(library, EntryPoint = "deview_focus")]
    public static partial void Focus();

    [LibraryImport(library, EntryPoint = "deview_shutdown")]
    public static partial void Shutdown();
}
