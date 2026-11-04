/// <summary>
/// The context menu's colours, taken from <see cref="Palette" /> so the one native popup in this
/// head matches the grid it sits over, and matches what the other two heads draw for the same
/// model. PlatformHeads' <see cref="PaletteMenuRenderer" /> draws in them.
/// <para>
/// Only some are reachable today: there are no icons, no submenus and no separators, and
/// <c>ShowImageMargin</c> is off. The renderer sets the rest anyway, because the cost is nothing
/// and the alternative is a later separator or icon quietly bleeding the light default into a
/// committed baseline.
/// </para>
/// </summary>
static class MenuTheme
{
    public static MenuPalette Colours { get; } = new(
        Background: Palette.Filler,
        Border: Palette.Rule,
        ItemBorder: Palette.Selected,
        Selected: Palette.Selected,
        Pressed: Palette.Filler,
        Separator: Palette.Rule,
        Text: Palette.Text,
        Dim: Palette.Dim);
}
