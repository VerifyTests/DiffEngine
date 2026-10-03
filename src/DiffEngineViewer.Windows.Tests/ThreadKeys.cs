/// <summary>
/// The keys the calling thread takes to be held, which is what WinForms asks when a message
/// reaches it: a key message says which key and nothing of Shift, Control or Alt, and a wheel
/// turn is read the same way, so <see cref="Control.ModifierKeys" /> is put beside both.
/// <para>
/// A test that posts a key to a form is posting half of what the form reads. The other half is
/// this table, which follows the keyboard whenever input reaches the thread, and the forms
/// these tests post to were the foreground window (see <see cref="ParkedForm" />). So with Alt
/// down at the machine for an Alt+Tab, or Control for a copy, a test read the key it posted as
/// that chord: Down was no command, and a wheel turn over the rows zoomed.
/// </para>
/// <para>
/// So a test says what is held before each message is read. Only for the calling thread: nothing
/// here reaches the keyboard, another thread or another process.
/// </para>
/// </summary>
static class ThreadKeys
{
    /// <summary>
    /// Shift, Control and Alt, and the left and right key of each, which the table holds apart.
    /// </summary>
    static readonly Keys[] modifiers =
    [
        Keys.ShiftKey,
        Keys.ControlKey,
        Keys.Menu,
        Keys.LShiftKey,
        Keys.RShiftKey,
        Keys.LControlKey,
        Keys.RControlKey,
        Keys.LMenu,
        Keys.RMenu
    ];

    /// <summary>
    /// No modifier held, whatever the keyboard says.
    /// </summary>
    public static void ReleaseModifiers() =>
        Set(modifiers, false);

    /// <summary>
    /// <paramref name="key"/> held, until <see cref="ReleaseModifiers"/> or the thread ends.
    /// </summary>
    public static void Hold(Keys key) =>
        Set([key], true);

    static void Set(Keys[] keys, bool held)
    {
        var state = new byte[256];
        GetKeyboardState(state);
        foreach (var key in keys)
        {
            // The top bit is down. The bottom one is a toggle, which no modifier has
            state[(int) key] = held ? (byte) 0x80 : (byte) 0;
        }

        SetKeyboardState(state);
    }

    [DllImport("user32.dll")]
    static extern bool GetKeyboardState(byte[] state);

    [DllImport("user32.dll")]
    static extern bool SetKeyboardState(byte[] state);
}
