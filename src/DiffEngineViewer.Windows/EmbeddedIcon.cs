/// <summary>
/// The window icon, carried in this assembly rather than read from the exe beside it. The same
/// image is compiled into the apphost by ApplicationIcon, but that one is a native resource a Form
/// cannot reach, and a viewer launched through a shim or a shadow copy has no exe to point at.
/// </summary>
static class EmbeddedIcon
{
    const string name = "DiffEngineViewer.viewer.ico";

    /// <summary>
    /// Null when the resource is missing, which leaves the Form on the WinForms default rather
    /// than failing to open a window over an icon.
    /// </summary>
    public static Icon? Load()
    {
        using var stream = typeof(EmbeddedIcon).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        return new(stream);
    }
}
