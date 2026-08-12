/// <summary>
/// The context menu's colours, taken from <see cref="Palette" /> so the one native popup in this
/// head matches the grid it sits over, and matches what the other two heads draw for the same
/// model.
/// <para>
/// Only the first four members are reachable today: there are no icons, no submenus and no
/// separators, and <c>ShowImageMargin</c> is off. The rest are set anyway, because the cost is
/// nothing and the alternative is a later separator or icon quietly bleeding the light default
/// into a committed baseline.
/// </para>
/// </summary>
sealed class MenuColours : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Palette.Filler;
    public override Color MenuBorder => Palette.Rule;
    public override Color MenuItemBorder => Palette.Selected;
    public override Color MenuItemSelected => Palette.Selected;

    public override Color MenuItemSelectedGradientBegin => Palette.Selected;
    public override Color MenuItemSelectedGradientEnd => Palette.Selected;
    public override Color MenuItemPressedGradientBegin => Palette.Filler;
    public override Color MenuItemPressedGradientMiddle => Palette.Filler;
    public override Color MenuItemPressedGradientEnd => Palette.Filler;
    public override Color ImageMarginGradientBegin => Palette.Filler;
    public override Color ImageMarginGradientMiddle => Palette.Filler;
    public override Color ImageMarginGradientEnd => Palette.Filler;
    public override Color SeparatorDark => Palette.Rule;
    public override Color SeparatorLight => Palette.Rule;
}

/// <summary>
/// Draws the context menu from <see cref="MenuColours" />.
/// <para>
/// Assigned per strip rather than through <c>ToolStripManager.Renderer</c>, which is process wide
/// state a test host would share. It also means this beats the system dark mode renderer that
/// <see cref="ViewerApp" /> installs globally, so the menu's pixels are ours on any machine and its
/// baseline does not move with the OS theme.
/// </para>
/// </summary>
sealed class MenuRenderer() :
    ToolStripProfessionalRenderer(new MenuColours())
{
    /// <summary>
    /// Item text is not a colour table member. <c>ToolStripMenuItem</c> resolves
    /// <c>SystemColors.MenuText</c> unless the item's own ForeColor was set — and it is per item,
    /// so setting it on the strip does nothing — then hands that to the renderer. This is the one
    /// place it can be corrected for every item at once.
    /// </summary>
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Palette.Text : Palette.Dim;
        base.OnRenderItemText(e);
    }

    /// <summary>
    /// Only reachable through the overflow arrows on a menu taller than the screen, which the
    /// four item menus here are not. Cheap enough to be right anyway.
    /// </summary>
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Palette.Text;
        base.OnRenderArrow(e);
    }
}
