using Keys = System.Windows.Forms.Keys;

/// <summary>
/// The key half of a hot key, which reaches the tray as whatever text settings.json holds. The
/// Options form only ever writes a letter, so everything rejected here arrives by hand edit.
/// </summary>
[TUnit.Core.Executors.STAThreadExecutor]
public class KeyNameTests
{
    [Test]
    public async Task Reads_a_key_name()
    {
        var parsed = KeyName.TryParse("A", out var key);

        await Assert.That(parsed).IsTrue();
        await Assert.That(key).IsEqualTo(Keys.A);
    }

    [Test]
    public async Task Reads_a_key_name_in_any_case()
    {
        var parsed = KeyName.TryParse("f12", out var key);

        await Assert.That(parsed).IsTrue();
        await Assert.That(key).IsEqualTo(Keys.F12);
    }

    /// <summary>
    /// Which is why this is not a round trip through <c>ToString</c>: Keys gives several values
    /// two names, and prints the other one.
    /// </summary>
    [Test]
    public async Task Reads_an_alias()
    {
        var parsed = KeyName.TryParse("Enter", out var key);

        await Assert.That(parsed).IsTrue();
        await Assert.That(key).IsEqualTo(Keys.Return);
    }

    /// <summary>
    /// Enum.Parse reads the underlying number as readily as the name, so "1" used to bind the
    /// left mouse button to a hot key that looked like it was for the digit.
    /// </summary>
    [Test]
    public async Task Rejects_a_number()
    {
        var parsed = KeyName.TryParse("1", out var key);

        await Assert.That(parsed).IsFalse();
        await Assert.That(key).IsNotEqualTo(Keys.LButton);
    }

    /// <summary>
    /// The modifiers are checkboxes of their own, so a key holding them is a misunderstanding of
    /// the file rather than a key.
    /// </summary>
    [Test]
    public async Task Rejects_a_modifier_combination() =>
        await Assert.That(KeyName.TryParse("Ctrl+A", out _)).IsFalse();

    [Test]
    public async Task Rejects_a_flag_list() =>
        await Assert.That(KeyName.TryParse("A,B", out _)).IsFalse();

    [Test]
    public async Task Rejects_nothing()
    {
        await Assert.That(KeyName.TryParse(null, out _)).IsFalse();
        await Assert.That(KeyName.TryParse("", out _)).IsFalse();
        await Assert.That(KeyName.TryParse(" ", out _)).IsFalse();
    }

    [Test]
    public async Task A_bad_key_leaves_the_hot_key_unbound()
    {
        using var register = new KeyRegister(0);

        // Nothing is registered with the OS for a key that is not one, so the handle above is
        // never used and no hot key is taken from the machine running this
        var bound = register.TryAddBinding(
            KeyBindingIds.AcceptAll,
            shift: true,
            control: false,
            alt: false,
            "Ctrl+A",
            () => throw new("Not bound, so never invoked"));

        await Assert.That(bound).IsFalse();
    }

    /// <summary>
    /// The tray binds its hot keys at startup, so a key name it cannot read used to throw out of
    /// startup and take the tray down at every login until settings.json was deleted by hand.
    /// </summary>
    [Test]
    public async Task A_bad_key_does_not_stop_the_tray_starting()
    {
        var settings = new Settings
        {
            AcceptAllHotKey = new()
            {
                Control = true,
                Key = "Ctrl+A"
            }
        };
        await using var tracker = new RecordingTracker();
        using var register = new KeyRegister(0);
        var warnings = new List<string>();

        Program.ReBindKeys(settings, register, tracker, warnings.Add);

        await Assert.That(warnings).HasSingleItem();
        await Assert.That(warnings[0]).Contains("Ctrl+A");
    }
}
