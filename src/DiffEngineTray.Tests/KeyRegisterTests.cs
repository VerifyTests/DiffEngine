using System.Windows.Forms;

/// <summary>
/// A hot key's action runs inside a message filter, and what is thrown from one of those comes out
/// of <c>Application.Run()</c> rather than going to <c>Application.ThreadException</c>: the tray
/// ended, with everything it was tracking, because one press went wrong.
/// <para>
/// The filter is called directly here and never through a message loop, so nothing thrown can
/// reach WinForms' own handling of it.
/// </para>
/// </summary>
[NotInParallel]
public class KeyRegisterTests
{
    const int id = 9731;

    // Every modifier and a key no keyboard has, since registering is for the whole desktop for
    // as long as the test takes
    const KeyModifiers modifiers = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift;

    [Test]
    public async Task AnActionThatThrowsIsReportedAndTheKeyIsStillHandled()
    {
        // No awaiting until the register is disposed: a hot key registered with no window belongs
        // to the thread that registered it, which is the one that has to take it back
        bool bound;
        var handled = false;
        using (var register = new KeyRegister(IntPtr.Zero))
        {
            bound = register.TryAddBinding(
                id,
                modifiers,
                Keys.F24,
                () => throw new InvalidOperationException("TheHotKeyFailure"));
            if (bound)
            {
                handled = register.PreFilterMessage(ref HotKeyPressed);
            }
        }

        await Assert.That(bound).IsTrue();
        await Assert.That(handled).IsTrue();
        await Assert.That(ModuleInitializer.IssuesAsked.Where(_ => _.Contains("TheHotKeyFailure"))).HasSingleItem();
    }

    [Test]
    public async Task AnActionRuns()
    {
        bool bound;
        var handled = false;
        var ran = false;
        using (var register = new KeyRegister(IntPtr.Zero))
        {
            bound = register.TryAddBinding(id, modifiers, Keys.F24, () => ran = true);
            if (bound)
            {
                handled = register.PreFilterMessage(ref HotKeyPressed);
            }
        }

        await Assert.That(bound).IsTrue();
        await Assert.That(handled).IsTrue();
        await Assert.That(ran).IsTrue();
    }

    // WM_HOTKEY, with the binding's id where the message carries it
    static Message HotKeyPressed = Message.Create(IntPtr.Zero, 0x0312, id, IntPtr.Zero);
}
