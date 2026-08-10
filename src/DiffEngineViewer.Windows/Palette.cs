/// <summary>
/// Transcribed from <c>RowColour</c> and <c>RowBackground</c> in the native shim, so a change looks
/// the same whichever renderer drew it. The <see cref="Screen" /> model deliberately carries a
/// <see cref="RowKind" /> and never a colour, so this mapping is each renderer's own.
/// </summary>
static class Palette
{
    public static readonly Color Background = Color.FromArgb(24, 24, 24);
    public static readonly Color Filler = Color.FromArgb(28, 28, 28);
    public static readonly Color Text = Color.FromArgb(212, 212, 212);

    /// <summary>
    /// The gutter, and the subtitle and status that ImGui draws with TextDisabled.
    /// </summary>
    public static readonly Color Dim = Color.FromArgb(130, 130, 130);

    public static readonly Color Rule = Color.FromArgb(70, 70, 70);

    /// <summary>
    /// ImGui draws a selected Selectable as its accent at 31% over the window background. This is
    /// that composite, so the queue highlight matches without carrying an alpha channel around.
    /// </summary>
    public static readonly Color Selected = Color.FromArgb(38, 64, 90);

    public static Color Foreground(RowKind kind) =>
        kind switch
        {
            RowKind.Added => Color.FromArgb(126, 214, 139),
            RowKind.Removed => Color.FromArgb(233, 129, 129),
            RowKind.Modified => Color.FromArgb(231, 197, 113),
            _ => Text
        };

    /// <summary>
    /// Null where the row takes the window background, which is every unchanged row.
    /// </summary>
    public static Color? RowBackground(RowKind kind) =>
        kind switch
        {
            RowKind.Added => Color.FromArgb(38, 74, 44),
            RowKind.Removed => Color.FromArgb(84, 40, 40),
            RowKind.Modified => Color.FromArgb(74, 64, 32),
            RowKind.Filler => Filler,
            _ => null
        };

    public static char Marker(RowKind kind) =>
        kind switch
        {
            RowKind.Added => '+',
            RowKind.Removed => '-',
            RowKind.Modified => '~',
            _ => ' '
        };
}
