/// <summary>
/// JetBrains Mono, carried in this assembly rather than looked up on the machine. Every renderer
/// wants the same glyphs: the shim uploads these bytes to ImGui, and the WinForms head registers
/// them with GDI+. Shipping the font is also what keeps the pixel baselines independent of what
/// happens to be installed on the runner.
/// </summary>
static class EmbeddedFont
{
    const string name = "DiffEngineViewer.JetBrainsMono-Regular.ttf";

    /// <summary>
    /// Empty when the resource is missing, which each renderer treats as "fall back to a built in
    /// font" rather than as a failure.
    /// </summary>
    public static byte[] Bytes()
    {
        using var stream = typeof(EmbeddedFont).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return [];
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
