/// <summary>
/// A window for a test to put a canvas in, shown so that it will paint and take messages and
/// never made the window the keyboard goes to. A form shown the ordinary way is activated, and
/// off every display it was still the foreground window: for as long as a test ran, what the
/// person at the machine typed went to it rather than to what they were typing into, and reached
/// the canvas as its own commands.
/// </summary>
[System.ComponentModel.DesignerCategory("")]
sealed class ParkedForm : Form
{
    protected override bool ShowWithoutActivation =>
        true;
}
