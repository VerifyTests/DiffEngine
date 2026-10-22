/// <summary>
/// The WinForms renderer. Consumes <see cref="Screen" /> directly, so this head marshals nothing
/// and ships no native code.
/// <para>
/// Pumped rather than inverted onto <c>Application.Run</c>. ViewerProgram owns the loop for all
/// three heads, and keeping it that way means the scroll amplification, the button lookup and the
/// close-means-hide rule stay in one place. <c>DoEvents</c> is usually a smell, but the conditions
/// that make it one are absent here: no modal dialogs, and session state already behind its own
/// lock. user32's own modal loops - a scroll bar thumb being dragged, the window being moved or
/// sized - do hold the thread inside DoEvents, and <see cref="ILoopHooks"/> is how frames keep
/// coming while they do.
/// </para>
/// </summary>
sealed class FormsViewerWindow :
    IViewerWindow,
    ILoopHooks
{
    readonly ViewerForm form;
    bool disposed;

    FormsViewerWindow(ViewerForm form) =>
        this.form = form;

    public static IViewerWindow? Open(string title, int width, int height, bool hidden, WindowPlacement? placement, out string? error)
    {
        error = null;
        try
        {
            var form = new ViewerForm(title, width, height, placement);
            // Forces the handle, so a hidden window can still measure text and be captured.
            form.CreateControl();
            _ = form.Handle;
            if (!hidden)
            {
                form.Show();
            }

            return new FormsViewerWindow(form);
        }
        catch (Exception exception)
        {
            // No desktop session, or a station that cannot host a window. Same shape as a missing
            // native renderer: a message rather than a stack trace.
            error = $"Could not open a window. {exception.Message}";
            return null;
        }
    }

    public bool Present(Screen screen)
    {
        if (disposed || form.IsDisposed)
        {
            return false;
        }

        form.LoopReturned();
        form.Apply(screen);
        // Every frame rather than only on a changed screen: a spinner turns while nothing about the
        // screen changes, which is the whole time a page is being drawn
        form.Animate();
        Application.DoEvents();
        if (form.IsDisposed)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// HeadForm's: idles on the message queue and the loop's wake together, a frame at most while
    /// a spinner is up, and not at all while input is already waiting. It woke sixty times a
    /// second with the window open and nothing happening, and ten hidden or minimised.
    /// </summary>
    public bool Wait(TimeSpan timeout, WaitHandle wake)
    {
        if (disposed || form.IsDisposed)
        {
            return false;
        }

        form.WaitForFrame(timeout, wake);
        return true;
    }

    public ViewerInput Poll() =>
        form.IsDisposed ? default : form.Drain();

    public Func<Screen>? Frame
    {
        set => form.Frame = value;
    }

    public Action? SessionEnding
    {
        set => form.SessionEnding = value;
    }

    /// <summary>
    /// Visibility only. Assigning ShowInTaskbar recreates the window handle, and doing that under
    /// a loop that is pumping with DoEvents means tearing the handle out from under an in flight
    /// paint. A hidden window has no taskbar button anyway, so it bought nothing.
    /// </summary>
    public void SetHidden(bool hidden)
    {
        if (!form.IsDisposed)
        {
            form.Visible = !hidden;
        }
    }

    public void Focus()
    {
        if (form.IsDisposed)
        {
            return;
        }

        form.Raise();
    }

    public WindowPlacement? Placement =>
        form.Placement;

    /// <summary>
    /// False when another process holds the clipboard open, which WinForms has already retried
    /// for about a second. Answered rather than thrown, and not retried here: the loop's
    /// <see cref="ClipboardPump"/> offers it again on the next frames, and says so if it never goes.
    /// </summary>
    public bool SetClipboard(string text)
    {
        if (FormsClipboard.Set(text, out var failure))
        {
            return true;
        }

        Console.Error.WriteLine($"Could not write to the clipboard: {failure?.Message}");
        return false;
    }

    public bool Capture(Screen screen, int width, int height, string pngPath)
    {
        if (form.IsDisposed)
        {
            return false;
        }

        return Sized(
            screen,
            width,
            height,
            () =>
            {
                // Invalidate only marks dirty; the paint has to have happened before the bitmap.
                form.Surface.Refresh();

                using var bitmap = new Bitmap(width, height);
                form.Surface.DrawToBitmap(bitmap, new(0, 0, width, height));
                bitmap.Save(pngPath, DrawingImageFormat.Png);
                return true;
            });
    }

    /// <summary>
    /// The grid a capture of <paramref name="screen"/> at this size draws: the window's cells
    /// once the footer that screen's buttons and status come to has been laid out.
    /// <para>
    /// A capture is handed a screen already built, and built for the whole window it shows only
    /// the rows that fit over its footer: a footer of two rows of buttons and a status under
    /// them left the last rows of the screen undrawn. The window never does that, since it
    /// reports its grid every frame and is handed a screen sliced to it. This is that report
    /// for a capture, so its caller can build the screen again for the rows there are. What a
    /// status says can turn on the rows, so the footer is the first screen's.
    /// </para>
    /// </summary>
    internal (int Columns, int Rows) MeasureGrid(Screen screen, int width, int height) =>
        Sized(screen, width, height, () => form.Grid);

    /// <summary>
    /// With the form showing <paramref name="screen"/> at this size, as a capture has it.
    /// </summary>
    T Sized<T>(Screen screen, int width, int height, Func<T> read)
    {
        // DrawToBitmap sends a paint message, and a window that has never been shown does not
        // answer one: the result is a correctly sized image of nothing. Shown off to the side
        // rather than at the default position, and without being activated: off to the side it
        // was still the foreground window, with the keyboard, for as long as the capture took.
        var wasVisible = form.Visible;
        if (!wasVisible)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new(-2000, -2000);
            form.ShowInTaskbar = false;
            form.Parked = true;
            form.Show();
        }

        // One frame, with its pictures in it and any spinner stood still: there is no later paint
        // for a picture decoded on the pool to land in, and a baseline has to come out the same
        var wasSynchronous = form.Synchronous;
        form.Synchronous = true;
        try
        {
            form.ClientSize = new(width, height);
            form.Apply(screen);
            form.PerformLayout();
            return read();
        }
        finally
        {
            form.Synchronous = wasSynchronous;
            if (!wasVisible)
            {
                form.Visible = false;
                form.Parked = false;
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (!form.IsDisposed)
        {
            form.CloseForReal();
            form.Dispose();
        }
    }
}
