/// <summary>
/// The thread the tray starts on is the one its windows live on, and WinForms needs that to be a
/// single threaded apartment for everything it does through OLE: the clipboard, drag and drop, the
/// common dialogs.
/// <para>
/// It was not. Main was <c>async Task</c>, and the entry point the runtime starts for one of those
/// is a method the compiler writes, which carries no attribute. So the thread was MTA, and "Copy"
/// in the debug view threw ThreadStateException out of Clipboard.SetText on every click.
/// </para>
/// <para>
/// Asserted on the assembly's entry point rather than on Program.Main, because that is the method
/// the apartment is read from: an attribute on an async Main would sit on a method the runtime
/// never starts with, and pass a test that looked for it there.
/// </para>
/// </summary>
public class ProgramEntryPointTest
{
    [Test]
    public async Task TheEntryPointStartsOnAnStaThread()
    {
        var entryPoint = typeof(Tracker).Assembly.EntryPoint!;

        await Assert.That(entryPoint.GetCustomAttribute<STAThreadAttribute>()).IsNotNull();
    }
}
