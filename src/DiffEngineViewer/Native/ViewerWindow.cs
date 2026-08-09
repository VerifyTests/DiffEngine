/// <summary>
/// Owns the native window. The only type in the app that touches the shim, so everything else
/// stays testable on a machine with no GPU.
/// </summary>
sealed class ViewerWindow : IDisposable
{
    /// <summary>
    /// Pixels per character cell, used to translate the window size into the character grid the
    /// rest of the app reasons in. Measured for JetBrains Mono at 15px.
    /// </summary>
    const int cellWidth = 9;

    const int cellHeight = 18;

    readonly ScreenPayload payload = new();
    bool disposed;

    ViewerWindow()
    {
    }

    public static bool TryOpen(
        string title,
        int width,
        int height,
        bool hidden,
        [NotNullWhen(true)] out ViewerWindow? window,
        [NotNullWhen(false)] out string? error)
    {
        window = null;
        error = null;
        int version;
        try
        {
            version = Deview.Version();
        }
        catch (DllNotFoundException exception)
        {
            error = $"Could not load the native renderer for this platform. {exception.Message}";
            return false;
        }
        catch (EntryPointNotFoundException exception)
        {
            error = $"The native renderer is missing an entry point. {exception.Message}";
            return false;
        }

        if (version != Deview.ExpectedVersion)
        {
            error = $"Native renderer version {version} does not match the expected {Deview.ExpectedVersion}.";
            return false;
        }

        var font = Font();
        if (!Open(title, width, height, hidden, font))
        {
            error = "The native renderer could not open a window.";
            return false;
        }

        window = new();
        return true;
    }

    static unsafe bool Open(string title, int width, int height, bool hidden, byte[] font)
    {
        fixed (byte* bytes = font)
        {
            return Deview.Init(width, height, title, bytes, font.Length, 15f, hidden ? 1 : 0) == 1;
        }
    }

    static byte[] Font()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("DiffEngineViewer.JetBrainsMono-Regular.ttf");
        if (stream is null)
        {
            // The shim falls back to ImGui's built in font for an empty buffer.
            return [];
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public bool Present(Screen screen)
    {
        payload.Build(screen);
        return payload.Present() == 1;
    }

    public bool Capture(Screen screen, int width, int height, string pngPath)
    {
        payload.Build(screen);
        return payload.Capture(width, height, pngPath) == 1;
    }

    public static unsafe ViewerInput Poll()
    {
        DeviewInput input;
        Deview.PollInput(&input);
        return new(
            Key: Key(input.Key),
            ClickedButton: input.ClickedButton,
            ClickedQueueItem: input.ClickedQueueItem,
            ScrollDelta: input.ScrollDelta,
            CloseRequested: input.CloseRequested != 0,
            Columns: Math.Max(40, input.Columns / cellWidth),
            Rows: Math.Max(10, input.Rows / cellHeight));
    }

    public static void SetHidden(bool hidden) =>
        Deview.SetHidden(hidden ? 1 : 0);

    public static void Focus() =>
        Deview.Focus();

    /// <summary>
    /// Explicit rather than a cast, because the shim reports only the keys a window can produce
    /// and the two enums deliberately do not line up.
    /// </summary>
    static CommandKind Key(int key) =>
        (DeviewKey) key switch
        {
            DeviewKey.ScrollUp => CommandKind.ScrollUp,
            DeviewKey.ScrollDown => CommandKind.ScrollDown,
            DeviewKey.PageUp => CommandKind.PageUp,
            DeviewKey.PageDown => CommandKind.PageDown,
            DeviewKey.Home => CommandKind.ScrollHome,
            DeviewKey.End => CommandKind.ScrollEnd,
            DeviewKey.NextChange => CommandKind.NextChange,
            DeviewKey.PreviousChange => CommandKind.PreviousChange,
            DeviewKey.NextItem => CommandKind.NextItem,
            DeviewKey.PreviousItem => CommandKind.PreviousItem,
            DeviewKey.Accept => CommandKind.Accept,
            DeviewKey.Discard => CommandKind.Discard,
            DeviewKey.AcceptAll => CommandKind.AcceptAll,
            DeviewKey.Quit => CommandKind.Quit,
            _ => CommandKind.None
        };

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Deview.Shutdown();
    }
}
