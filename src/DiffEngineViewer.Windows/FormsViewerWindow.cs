/// <summary>
/// The WinForms renderer. Consumes <see cref="Screen" /> directly, so this head marshals nothing
/// and ships no native code.
/// <para>
/// Pumped rather than inverted onto <c>Application.Run</c>. ViewerProgram owns the loop for all
/// three heads, and keeping it that way means the scroll amplification, the button lookup and the
/// close-means-hide rule stay in one place. <c>DoEvents</c> is usually a smell, but the conditions
/// that make it one are absent here: no modal dialogs, no nested message loops, and session state
/// already behind its own lock.
/// </para>
/// </summary>
sealed class FormsViewerWindow : IViewerWindow
{
    /// <summary>
    /// Roughly sixty frames a second, which is what the shim's SetTargetFPS gives the other heads.
    /// Without it this loop would spin a core, since DoEvents returns immediately when idle.
    /// </summary>
    const int frameMilliseconds = 16;

    readonly ViewerForm form;
    bool disposed;

    FormsViewerWindow(ViewerForm form) =>
        this.form = form;

    public static IViewerWindow? Open(string title, int width, int height, bool hidden, out string? error)
    {
        error = null;
        try
        {
            var form = new ViewerForm(title, width, height);
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

        form.Apply(screen);
        Application.DoEvents();
        if (form.IsDisposed)
        {
            return false;
        }

        Thread.Sleep(frameMilliseconds);
        return true;
    }

    public ViewerInput Poll() =>
        form.IsDisposed ? default : form.Drain();

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

    /// <summary>
    /// Best effort, the way revealing a file is. Another process can hold the clipboard open, and
    /// failing to copy is not worth taking the reviewer's window down over.
    /// </summary>
    public void SetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException exception)
        {
            Console.Error.WriteLine($"Could not write to the clipboard: {exception.Message}");
        }
    }

    public bool Capture(Screen screen, int width, int height, string pngPath)
    {
        if (form.IsDisposed)
        {
            return false;
        }

        // DrawToBitmap sends a paint message, and a window that has never been shown does not
        // answer one: the result is a correctly sized image of nothing. Shown off to the side
        // rather than at the default position, so a capture run does not steal focus mid screen.
        var wasVisible = form.Visible;
        if (!wasVisible)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new(-2000, -2000);
            form.ShowInTaskbar = false;
            form.Show();
        }

        try
        {
            form.ClientSize = new(width, height);
            form.Apply(screen);
            form.PerformLayout();
            // Invalidate only marks dirty; the paint has to have happened before the bitmap.
            form.Surface.Refresh();

            using var bitmap = new Bitmap(width, height);
            form.Surface.DrawToBitmap(bitmap, new(0, 0, width, height));
            bitmap.Save(pngPath, DrawingImageFormat.Png);
        }
        finally
        {
            if (!wasVisible)
            {
                form.Visible = false;
            }
        }

        return true;
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
