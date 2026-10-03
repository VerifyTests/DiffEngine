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
            .Cast<ToolStripItem>()
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
            .OfType<Label>()
            .Single();

        await Assert.That(status.UseMnemonic).IsFalse();
    }

    /// <summary>
    /// Nor a footer button, whose label is the model's as the status is. One that did made the
    /// letter after the ampersand an Alt chord that pressed it.
    /// </summary>
    [Test]
    public async Task A_footer_button_does_not_read_one_as_a_mnemonic()
    {
        using var form = new ViewerForm("title", 800, 600);
        var screen = ScreenBuilder.Build(Fixtures.File());

        form.Apply(
            screen with
            {
                Buttons = [new("Accept R&D", true, CommandKind.Accept), .. screen.Buttons]
            });

        var buttons = form.Controls
            .Find("buttons", true)
            .Single()
            .Controls
            .OfType<System.Windows.Forms.Button>()
            .ToList();
        await Assert.That(buttons.Count).IsGreaterThan(1);
        await Assert.That(buttons[0].Text).IsEqualTo("Accept R&D");
        await Assert.That(buttons.All(_ => !_.UseMnemonic)).IsTrue();
    }
}
