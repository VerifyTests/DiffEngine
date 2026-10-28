/// <summary>
/// The tray's menu as WinForms shows it: <see cref="TrayMenu" />'s items made ToolStrip items by
/// PlatformHeads' <see cref="TrayMenuItems" />, which BuildMonitor's tray shows its model with too.
/// What the menu says and does is the core's; what is here is how it looks.
/// </summary>
static class MenuBuilder
{
    public static ContextMenuStrip Build(TrayActions actions, Tracker tracker)
    {
        var menu = new ContextMenuStrip
        {
            DefaultDropDownDirection = ToolStripDropDownDirection.AboveLeft
        };
        menu.Font = new(menu.Font.FontFamily, 10);
        var fixedItems = TrayMenu.Fixed(actions);
        TrayMenu? tracked = null;

        var options = new NotifyIconOptions<TrayIcon>(
            "DiffEngineTray",
            _ => _ == TrayIcon.Active ? Images.Active : Images.Default,
            Glyph,
            Style: (item, model) =>
            {
                item.ToolTipText = fixedItems.Tooltip(model.Id) ?? tracked?.Tooltip(model.Id);
                if (model.HasChildren)
                {
                    item.DropDownDirection = ToolStripDropDownDirection.Left;
                }
            });

        void Clicked(string id)
        {
            if (!fixedItems.Invoke(id))
            {
                tracked?.Invoke(id);
            }
        }

        foreach (var item in fixedItems.Items)
        {
            menu.Items.Add(TrayMenuItems.Create(item, options, Clicked));
        }

        // After the fixed items, which it keeps as themselves: told apart by their text, a
        // solution named after one of them - Options, say - gave that group a header that was
        // never removed. The tracked ones are built again on every open, and disposed at the next.
        MenuOnOpening.Rebuild(
            menu,
            _ =>
            {
                tracked = TrayMenu.Tracked(tracker, actions);
                foreach (var item in tracked.Items)
                {
                    _.Add(TrayMenuItems.Create(item, options, Clicked));
                }
            });
        return menu;
    }

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
