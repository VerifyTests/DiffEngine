using System.Windows.Forms;

/// <summary>
/// A hot key's action runs inside a message filter, and what is thrown from one of those comes out
/// of <c>Application.Run()</c> rather than going to <c>Application.ThreadException</c>: the tray
/// ended, with everything it was tracking, because one press went wrong.
/// <para>
/// The filter is called directly here and never through a message loop, so nothing thrown can
/// reach WinForms' own handling of it.
/// </para>
/// <para>
/// Nothing is registered with the desktop. These used to take Ctrl+Alt+Shift+F24 for the length
/// of each test, a key held for every process on the machine: they failed wherever something
/// else had it, and beside themselves whenever two runs of them overlapped. What they are about
/// is what happens to a press once a key is bound, which the desktop has no part in.
/// </para>
/// </summary>
public class KeyRegisterTests
{
    const int id = 9731;
    const KeyModifiers modifiers = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift;

    [Test]
    public async Task AnActionThatThrowsIsReportedAndTheKeyIsStillHandled()
    {
        var desktop = new Desktop();
        bool bound;
        bool handled;
        using (var register = desktop.Register())
        {
            bound = register.TryAddBinding(
                id,
                modifiers,
                Keys.F24,
                () => throw new InvalidOperationException("TheHotKeyFailure"));
            handled = register.PreFilterMessage(ref HotKeyPressed);
        }

        await Assert.That(bound).IsTrue();
        await Assert.That(handled).IsTrue();
        await Assert.That(ModuleInitializer.IssuesAsked.Where(_ => _.Contains("TheHotKeyFailure"))).HasSingleItem();
    }

    [Test]
    public async Task AnActionRuns()
    {
        var desktop = new Desktop();
        bool bound;
        bool handled;
        var ran = false;
        using (var register = desktop.Register())
        {
            bound = register.TryAddBinding(id, modifiers, Keys.F24, () => ran = true);
            handled = register.PreFilterMessage(ref HotKeyPressed);
        }

        await Assert.That(bound).IsTrue();
        await Assert.That(handled).IsTrue();
        await Assert.That(ran).IsTrue();
        await Assert.That(desktop.Asked).IsEquivalentTo([(id, modifiers, Keys.F24)]);
    }

    /// <summary>
    /// A combination something else holds is refused by the desktop, and the press that then
    /// never comes must not find an action waiting for it.
    /// </summary>
    [Test]
    public async Task AKeyTheDesktopRefusesIsNotBound()
    {
        var desktop = new Desktop
        {
            Refuses = true
        };
        bool bound;
        bool handled;
        var ran = false;
        using (var register = desktop.Register())
        {
            bound = register.TryAddBinding(id, modifiers, Keys.F24, () => ran = true);
            handled = register.PreFilterMessage(ref HotKeyPressed);
        }

        await Assert.That(bound).IsFalse();
        await Assert.That(handled).IsFalse();
        await Assert.That(ran).IsFalse();
    }

    [Test]
    public async Task EveryKeyBoundIsGivenBackOnDispose()
    {
        var desktop = new Desktop();
        using (var register = desktop.Register())
        {
            register.TryAddBinding(id, modifiers, Keys.F24, () =>
            {
            });
            register.TryAddBinding(id + 1, modifiers, Keys.F23, () =>
            {
            });
        }

        await Assert.That(desktop.Held).IsEmpty();
    }

    /// <summary>
    /// What a register asks of the desktop, answered here: which ids hold a key, and whether the
    /// next one asked for is refused.
    /// </summary>
    class Desktop
    {
        public bool Refuses { get; init; }

        public HashSet<int> Held { get; } = [];

        public List<(int id, KeyModifiers modifiers, Keys key)> Asked { get; } = [];

        public KeyRegister Register() =>
            new(
                IntPtr.Zero,
                (_, id, modifiers, key) =>
                {
                    Asked.Add((id, modifiers, key));
                    if (Refuses)
                    {
                        return false;
                    }

                    Held.Add(id);
                    return true;
                },
                (_, id) => Held.Remove(id));
    }

    // WM_HOTKEY, with the binding's id where the message carries it
    static Message HotKeyPressed = Message.Create(IntPtr.Zero, 0x0312, id, IntPtr.Zero);
}
