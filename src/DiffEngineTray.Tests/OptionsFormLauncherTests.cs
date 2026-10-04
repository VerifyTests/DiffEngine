using Keys = System.Windows.Forms.Keys;

/// <summary>
/// Saving the Options form re-registers all three hot keys.
/// <para>
/// Nothing is registered with the desktop. These used to take Ctrl+Alt+Shift+F13 to F17 for the
/// length of a test, keys held for every process on the machine: they failed wherever something
/// else had one. What they are about is which keys a save leaves bound, and the one thing the
/// desktop adds to that is refusing a combination that is already held, which the stand-in below
/// does as Windows does.
/// </para>
/// </summary>
[TUnit.Core.Executors.STAThreadExecutor]
public class OptionsFormLauncherTests
{
    const KeyModifiers modifiers = KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt;

    /// <summary>
    /// Each hot key used to be bound as it was reached, so a collision on the second returned
    /// with the first already live on its new combination - while settings.json, which is written
    /// after this, still held the old one. The dialog said the save failed and the keys disagreed
    /// with it until the tray was restarted.
    /// </summary>
    [Test]
    public async Task A_collision_leaves_every_hot_key_as_it_was()
    {
        var previous = new Settings
        {
            AcceptAllHotKey = HotKey(Keys.F13)
        };
        // Both on one combination, so the second registration is refused for a reason this test
        // made itself
        var settings = new Settings
        {
            AcceptAllHotKey = HotKey(Keys.F14),
            DiscardAllHotKey = HotKey(Keys.F14)
        };
        var desktop = new Desktop();
        await using var tracker = new RecordingTracker();
        using var register = desktop.Register();
        Bind(register, previous, tracker);

        var errors = OptionsFormLauncher.ReBind(register, tracker, previous, settings);

        await Assert.That(errors).IsNotEmpty();
        // What the desktop was left holding, which is the only account of it that matters
        await Assert.That(desktop.IsHeld(Keys.F14)).IsFalse();
        await Assert.That(desktop.IsHeld(Keys.F13)).IsTrue();
    }

    [Test]
    public async Task A_save_that_binds_takes_every_hot_key()
    {
        var previous = new Settings
        {
            AcceptAllHotKey = HotKey(Keys.F15)
        };
        var settings = new Settings
        {
            AcceptAllHotKey = HotKey(Keys.F16),
            DiscardAllHotKey = HotKey(Keys.F17)
        };
        var desktop = new Desktop();
        await using var tracker = new RecordingTracker();
        using var register = desktop.Register();
        Bind(register, previous, tracker);

        var errors = OptionsFormLauncher.ReBind(register, tracker, previous, settings);

        await Assert.That(errors).IsEmpty();
        await Assert.That(desktop.IsHeld(Keys.F16)).IsTrue();
        await Assert.That(desktop.IsHeld(Keys.F17)).IsTrue();
        // The one it replaced is given up, rather than left registered for a key the settings no
        // longer mention
        await Assert.That(desktop.IsHeld(Keys.F15)).IsFalse();
    }

    static void Bind(KeyRegister register, Settings settings, RecordingTracker tracker) =>
        Program.ReBindKeys(settings, register, tracker);

    /// <summary>
    /// What a register asks of the desktop, answered here: which id holds which combination. A
    /// combination something holds is refused to the next id that asks for it, and so is an id
    /// that already holds one, which are the two refusals <c>RegisterHotKey</c> gives.
    /// </summary>
    class Desktop
    {
        Dictionary<int, (KeyModifiers modifiers, Keys key)> held = [];

        public bool IsHeld(Keys key) =>
            held.ContainsValue((modifiers, key));

        public KeyRegister Register() =>
            new(
                IntPtr.Zero,
                (_, id, modifiers, key) =>
                {
                    if (held.ContainsKey(id) ||
                        held.ContainsValue((modifiers, key)))
                    {
                        return false;
                    }

                    held.Add(id, (modifiers, key));
                    return true;
                },
                (_, id) => held.Remove(id));
    }

    static HotKey HotKey(Keys key) =>
        new()
        {
            Control = true,
            Shift = true,
            Alt = true,
            Key = key.ToString()
        };
}
