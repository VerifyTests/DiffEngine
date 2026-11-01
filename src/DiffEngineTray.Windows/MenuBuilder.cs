/// <summary>
/// How the tray's menu looks on Windows: the options PlatformHeads' <see cref="NotifyIconTray{TIcon}" />
/// makes its menu from <see cref="TrayMenu" />'s items with, by <see cref="TrayMenuItems" />,
/// which BuildMonitor's tray shows its model with too. What the menu says and does is the core's.
/// </summary>
static class MenuBuilder
{
    /// <param name="tooltip">The tip for an item, by its id, from the menus last shown.</param>
    public static NotifyIconOptions<TrayIcon> Options(Func<string, string?> tooltip) =>
        new(
            "DiffEngineTray",
            _ => _ == TrayIcon.Active ? Images.Active : Images.Default,
            Glyph,
            Theme: _ =>
            {
                // The menu itself, which this is handed before every opening, and not a submenu,
                // which opens where its item says
                if (_ is not ContextMenuStrip menu)
                {
                    return;
                }

                menu.DefaultDropDownDirection = ToolStripDropDownDirection.AboveLeft;
                if (menu.Font.Size != fontSize)
                {
                    menu.Font = new(menu.Font.FontFamily, fontSize);
                }
            },
            Style: (item, model) =>
            {
                item.ToolTipText = tooltip(model.Id);
                if (model.HasChildren)
                {
                    item.DropDownDirection = ToolStripDropDownDirection.Left;
                }
            },
            // The menu is all the icon has, so either button opens it
            LeftClickOpensMenu: true);

    const float fontSize = 10;

    static Image? Glyph(string name) =>
        name switch
        {
            "exit" => Images.Exit,
            "options" => Images.Options,
            "folder" => Images.Folder,
            "link" => Images.Link,
            "solution" => Images.VisualStudio,
            "delete" => Images.Delete,
            "accept" => Images.Accept,
            "acceptAll" => Images.AcceptAll,
            "discard" => Images.Discard,
            _ => null
        };
}
