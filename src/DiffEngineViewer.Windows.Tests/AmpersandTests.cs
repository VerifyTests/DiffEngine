/// <summary>
/// An ampersand in a solution name, a path or an applier message. WinForms reads one as a mnemonic
/// wherever it renders text, so "R&amp;D" draws as "R_D" - with D live as an accelerator, which is
/// worse than the missing character.
/// </summary>
[NotInParallel]
[TUnit.Core.Executors.STAThreadExecutor]
public class AmpersandTests
{
    [Test]
    public async Task A_menu_label_keeps_its_ampersand()
    {
        using var strip = ViewerMenu.Build(new(0, ["Accept all in R&D"]));

        var item = strip.Items
            .Cast<System.Windows.Forms.ToolStripItem>()
            .Single();
        // Doubled, which is how a literal one is written. What is drawn is one
        await Assert.That(item.Text).IsEqualTo("Accept all in R&&D");
    }

    [Test]
    public async Task The_status_line_does_not_read_one_as_a_mnemonic()
    {
        using var form = new ViewerForm("title", 800, 600);

        var status = form.Controls
            .Find("status", true)
            .OfType<System.Windows.Forms.Label>()
            .Single();

        await Assert.That(status.UseMnemonic).IsFalse();
    }
}
