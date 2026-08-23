/// <summary>
/// Projects the <see cref="MenuOverlay" /> the shared model carries into a real
/// <see cref="ContextMenuStrip" />, which is what gives this head the keyboard navigation, the
/// screen reader support and the screen edge flipping that a drawn menu has none of.
/// <para>
/// Static and window free, so a test can build a strip from a <c>SessionState</c> and snapshot it
/// without opening anything. The other two heads still draw the overlay themselves.
/// </para>
/// </summary>
static class ViewerMenu
{
    public static ContextMenuStrip Create() =>
        new()
        {
            // No item carries an icon or a check, and the margins for them are a light gutter the
            // colour table would otherwise have to fight.
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Renderer = new MenuRenderer(),
            // For the slivers the renderer does not paint, such as overflow buttons.
            BackColor = Palette.Filler,
            ForeColor = Palette.Text
        };

    /// <summary>
    /// Replaces the items. The index is the payload rather than an action, because
    /// <c>ViewerProgram</c> resolves it against the model's own item list, which is where the
    /// command actually lives.
    /// </summary>
    public static void Fill(ContextMenuStrip strip, MenuOverlay menu, Action<int> clicked)
    {
        // Copy, then clear, then dispose. Disposing a ToolStripItem removes it from its owner
        // collection, so disposing while walking the live collection skips every second item.
        var stale = strip.Items.Cast<ToolStripItem>().ToArray();
        strip.Items.Clear();
        foreach (var item in stale)
        {
            item.Dispose();
        }

        for (var index = 0; index < menu.Labels.Count; index++)
        {
            var captured = index;
            var item = new ToolStripMenuItem(Escape(menu.Labels[index]));
            item.Click += (_, _) => clicked(captured);
            strip.Items.Add(item);
        }
    }

    /// <summary>
    /// Menu labels carry solution names and file names - "Accept all in R&amp;D" - and a menu item
    /// reads an ampersand as a mnemonic, so that one drew as "Accept all in R_D" with D live as an
    /// accelerator. Doubling it is how a literal one is written; ToolStripItem has no
    /// <c>UseMnemonic</c> to turn the reading off.
    /// </summary>
    static string Escape(string label) =>
        label.Replace("&", "&&");

    /// <summary>
    /// A strip on its own, for tests and for anything that wants one without a window.
    /// </summary>
    public static ContextMenuStrip Build(MenuOverlay menu, Action<int>? clicked = null)
    {
        var strip = Create();
        Fill(strip, menu, clicked ?? (_ => { }));
        return strip;
    }
}
