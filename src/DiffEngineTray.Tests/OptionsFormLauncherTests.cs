using Keys = System.Windows.Forms.Keys;

/// <summary>
/// Saving the Options form re-registers all three hot keys. The combinations used here are held
/// for the length of a test and are ones nothing else is likely to want.
/// </summary>
[TUnit.Core.Executors.STAThreadExecutor]
public class OptionsFormLauncherTests
{
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
        // Both on one combination, so the second registration is refused by the OS rather than by
        // whatever else happens to be running
        var settings = new Settings
        {
            AcceptAllHotKey = HotKey(Keys.F14),
            DiscardAllHotKey = HotKey(Keys.F14)
        };
        await using var tracker = new RecordingTracker();
        using var register = new KeyRegister(0);
        Bind(register, previous, tracker);

        var errors = OptionsFormLauncher.ReBind(register, tracker, previous, settings);

        await Assert.That(errors).IsNotEmpty();
        // What the OS thinks, which is the only account of it that matters
        await Assert.That(IsHeld(Keys.F14)).IsFalse();
        await Assert.That(IsHeld(Keys.F13)).IsTrue();
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
        await using var tracker = new RecordingTracker();
        using var register = new KeyRegister(0);
        Bind(register, previous, tracker);

        var errors = OptionsFormLauncher.ReBind(register, tracker, previous, settings);

        await Assert.That(errors).IsEmpty();
        await Assert.That(IsHeld(Keys.F16)).IsTrue();
        await Assert.That(IsHeld(Keys.F17)).IsTrue();
        // The one it replaced is given up, rather than left registered for a key the settings no
        // longer mention
        await Assert.That(IsHeld(Keys.F15)).IsFalse();
    }

    static void Bind(KeyRegister register, Settings settings, RecordingTracker tracker) =>
        Program.ReBindKeys(settings, register, tracker);

    /// <summary>
    /// Whether anything holds the combination, asked by trying to take it. A register of one that
    /// is already taken is what the OS refuses, so a success means it was free - and gives it
    /// straight back.
    /// </summary>
    static bool IsHeld(Keys key)
    {
        using var probe = new KeyRegister(0);
        return !probe.TryAddBinding(
            id: 9,
            KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt,
            key,
            () =>
            {
            });
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
