/// <summary>
/// A footer button whose label stays readable while it is disabled. In dark mode WinForms draws
/// a disabled Standard button's text near black whatever the button's colours are, which on
/// <see cref="Palette.Background" /> could not be seen at all. Its border and face are right, so
/// they are left to WinForms and only the label is drawn again, over a face that is a plain fill.
/// </summary>
[DesignerCategory("")]
sealed class FooterButton : FormsButton
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Enabled)
        {
            return;
        }

        var graphics = e.Graphics;
        var inside = ClientRectangle;
        // Inside the rounded border, which WinForms draws one pixel in from the edge
        inside.Inflate(-2, -2);
        using (var face = new SolidBrush(BackColor))
        {
            graphics.FillRectangle(face, inside);
        }

        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            ClientRectangle,
            Palette.Dim,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix);
    }
}
