// ReSharper disable RedundantUnsafeContext
/// <summary>
/// The <see cref="IViewerWindow" /> backed by the native shim. The only type in the app that
/// touches it, so everything else stays testable on a machine with no GPU.
/// </summary>
sealed class NativeViewerWindow : IViewerWindow
{
    readonly ScreenPayload payload = new();
    bool disposed;

    NativeViewerWindow()
    {
    }

    public static IViewerWindow? Open(string title, int width, int height, bool hidden, out string? error)
    {
        error = null;
        int version;
        try
        {
            version = Deview.Version();
        }
        catch (DllNotFoundException exception)
        {
            error = $"Could not load the native renderer for this platform. {exception.Message}";
            return null;
        }
        catch (EntryPointNotFoundException exception)
        {
            error = $"The native renderer is missing an entry point. {exception.Message}";
            return null;
        }

        if (version != Deview.ExpectedVersion)
        {
            error = $"Native renderer version {version} does not match the expected {Deview.ExpectedVersion}.";
            return null;
        }

        if (!Init(title, width, height, hidden, EmbeddedFont.Bytes()))
        {
            error = "The native renderer could not open a window.";
            return null;
        }

        return new NativeViewerWindow();
    }

    static unsafe bool Init(string title, int width, int height, bool hidden, byte[] font)
    {
        fixed (byte* bytes = font)
        {
            // An em size, which is what the ABI takes and what the WinForms head's 11pt works out
            // as, so all three heads draw the same size text.
            return Deview.Init(width, height, title, bytes, font.Length, 15f, hidden ? 1 : 0) == 1;
        }
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

    /// <summary>
    /// The shim owns one process wide window, so these forward to statics. Exposed as instance
    /// members anyway, because that is the shape a per window toolkit needs.
    /// </summary>
    public unsafe ViewerInput Poll()
    {
        DeviewInput input;
        Deview.PollInput(&input);
        return new(
            Key: Key(input.Key),
            ClickedButton: input.ClickedButton,
            ClickedQueueItem: input.ClickedQueueItem,
            ScrollDelta: input.ScrollDelta,
            CloseRequested: input.CloseRequested != 0,
            // Already cells: the shim measures them from the font it loaded. Only the floors are
            // applied here, because they are the app's rule rather than the renderer's.
            Columns: Math.Max(40, input.Columns),
            Rows: Math.Max(10, input.Rows),
            RightClickedQueueItem: input.RightClickedQueueItem,
            ClickedMenuItem: input.ClickedMenuItem,
            MenuClosed: input.MenuClosed != 0,
            ScrollTo: input.ScrollTo);
    }

    public void SetHidden(bool hidden) =>
        Deview.SetHidden(hidden ? 1 : 0);

    public void Focus() =>
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
            DeviewKey.NextVariant => CommandKind.NextVariant,
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
