using System.Text;

/// <summary>
/// The macOS tray: PlatformHeadsMac's StatusItem in the tray's own native library
/// (<see cref="Detray" />), since the managed side has no AppKit. Every call is the thread Main
/// started on's to make, as AppKit requires, so the Mac head's loop is
/// <see cref="TrayLoop.RunPumped" />, with <see cref="Pump" /> as its wait.
/// </summary>
sealed class MacTray : ITray<TrayIcon>
{
    // An id is a path or a snapshot's key with a word either side, which this holds with room over
    readonly byte[] chosen = new byte[64 * 1024];

    MacTray()
    {
    }

    /// <summary>
    /// The item in the status bar, with every icon and glyph handed over. Null with an
    /// <paramref name="error" /> when there is no library for this machine, one of another version,
    /// or no status bar to put the item in.
    /// </summary>
    public static MacTray? Open(out string? error)
    {
        try
        {
            Detray.Resolve();
            var version = Detray.Version();
            if (version != Detray.ExpectedVersion)
            {
                error = $"The tray's native library is version {version}, and this tray was built for {Detray.ExpectedVersion}.";
                return null;
            }

            if (Detray.Init() == 0)
            {
                error = "AppKit gave the status item no button to draw in.";
                return null;
            }
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            error = exception.Message;
            return null;
        }

        foreach (var icon in Enum.GetValues<TrayIcon>())
        {
            var png = TrayPictures.Png(icon);
            Detray.SetIcon((int) icon, png, png.Length);
        }

        foreach (var (name, png) in TrayPictures.Glyphs)
        {
            Detray.SetMenuIcon(name, png, png.Length);
        }

        error = null;
        return new();
    }

    public void Apply(TrayModel<TrayIcon> model) =>
        Send(model.Icon, model.Tooltip, model.Items);

    /// <summary>
    /// What <see cref="Apply" /> hands over, which needs no status item, so a test can hand over a
    /// menu and read back what the library made of it. The menu goes as PlatformHeads'
    /// <see cref="TrayMenuItem.Flatten" /> writes it, which PlatformHeadsMac's
    /// <c>StatusMenuItem.tree</c> reads back, with <see cref="TrayMenuItem.Flags" /> as
    /// <c>detray.h</c>'s <c>DETRAY_*</c> bits, which have the same values.
    /// </summary>
    internal static void Send(TrayIcon icon, string tooltip, IReadOnlyList<TrayMenuItem> items)
    {
        var flat = TrayMenuItem.Flatten(items);
        var native = new DetrayItem[flat.Count];
        var strings = new List<IntPtr>();
        try
        {
            for (var i = 0; i < flat.Count; i++)
            {
                var (item, parent) = flat[i];
                native[i] = new()
                {
                    Id = Utf8(item.Id, strings),
                    Label = Utf8(item.Label, strings),
                    Icon = Utf8(item.IconName ?? "", strings),
                    Flags = (int) item.Flags,
                    Parent = parent
                };
            }

            Detray.Apply((int) icon, tooltip, native, native.Length);
        }
        finally
        {
            foreach (var pointer in strings)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
    }

    static IntPtr Utf8(string text, List<IntPtr> strings)
    {
        var pointer = Marshal.StringToCoTaskMemUTF8(text);
        strings.Add(pointer);
        return pointer;
    }

    public TrayInput Poll()
    {
        var length = Detray.Poll(chosen, chosen.Length);
        if (length == 0)
        {
            return new();
        }

        return new(ClickedItem: Encoding.UTF8.GetString(chosen, 0, Math.Min(length, chosen.Length)));
    }

    /// <summary>
    /// AppKit's events dispatched, for a tenth of a second at most: the loop's wait, and the only
    /// way the menu opens at all.
    /// </summary>
    public static void Pump() =>
        Detray.Pump();

    /// <summary>
    /// AppKit was asked to quit. With no Dock icon, that is a logout, which the library turned
    /// into this, for the loop to end the way Exit does, staging the queue on its way out.
    /// </summary>
    public static bool Quitting =>
        Detray.Quitting() != 0;

    /// <summary>
    /// The menu as the library rebuilt it from the last <see cref="Send" />, as text.
    /// </summary>
    internal static string Describe()
    {
        var buffer = new byte[64 * 1024];
        var length = Detray.Describe(buffer, buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, Math.Min(length, buffer.Length));
    }

    public void Notify(TrayNotification notification) =>
        DesktopNotification.Show("DiffEngineTray", notification);

    public void Dispose() =>
        Detray.Shutdown();
}
