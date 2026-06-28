// A plain, OS-independent projection of a NativeMenu so the tray menu structure can be snapshotted
// as text (NativeMenu/NativeMenuItem are platform-native objects and cannot be rendered).
public record MenuNode(string Kind, string? Header, bool Enabled, bool HasCommand, IReadOnlyList<MenuNode> Items);

static class MenuDump
{
    public static IReadOnlyList<MenuNode> ToModel(NativeMenu menu) =>
        menu.Items
            .Select(ToNode)
            .ToList();

    static MenuNode ToNode(NativeMenuItemBase item)
    {
        if (item is NativeMenuItemSeparator)
        {
            return new("Separator", null, true, false, []);
        }

        var menuItem = (NativeMenuItem) item;
        var children = menuItem.Menu == null ? Array.Empty<MenuNode>() : ToModel(menuItem.Menu);
        return new("Item", menuItem.Header, menuItem.IsEnabled, menuItem.Command != null, children);
    }
}
