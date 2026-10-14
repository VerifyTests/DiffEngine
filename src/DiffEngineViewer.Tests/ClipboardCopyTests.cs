/// <summary>
/// A copy waits in the state until the window takes it, and the status says so when it never does.
/// The window was handed the text inside the copy's mutation, and a clipboard another process held
/// lost it while the status said it had been copied.
/// </summary>
public class ClipboardCopyTests
{
    [Test]
    public async Task ACopyTheWindowTakesIsLetGo()
    {
        var window = new Desktop();
        var host = Copied(out var copy);

        ClipboardCopy.Offer(new(), host, window, copy);

        await Assert.That(window.Taken).IsEquivalentTo(["the text"]);
        await Assert.That(host.State.Clipboard).IsNull();
        await Assert.That(host.State.Message).IsEqualTo("Copied 1 line from the selection.");
    }

    [Test]
    public async Task ABusyClipboardKeepsTheCopyForTheNextFrame()
    {
        var window = new Desktop { Busy = true };
        var host = Copied(out var copy);

        ClipboardCopy.Offer(new(), host, window, copy);

        await Assert.That(host.State.Clipboard).IsSameReferenceAs(copy);
        await Assert.That(host.State.Message).IsEqualTo("Copied 1 line from the selection.");
    }

    [Test]
    public async Task AClipboardThatStaysBusyIsGivenUpOnAndSaidSo()
    {
        var window = new Desktop { Busy = true };
        var host = Copied(out var copy);
        var pump = new ClipboardPump();
        for (var attempt = 0; attempt < ClipboardPump.Attempts; attempt++)
        {
            ClipboardCopy.Offer(pump, host, window, copy);
        }

        await Assert.That(host.State.Clipboard).IsNull();
        await Assert.That(host.State.Message).IsEqualTo("Could not copy the selection: another program is holding the clipboard.");
    }

    /// <summary>
    /// What came of one copy is not what came of a copy made since, which is still to be offered.
    /// </summary>
    [Test]
    public async Task AnOutcomeLeavesALaterCopyAlone()
    {
        var window = new Desktop();
        var host = Copied(out var first);
        var second = host.Mutate(_ => _ with { Clipboard = new("other text", "the selection") }).Clipboard!;

        ClipboardCopy.Offer(new(), host, window, first);

        await Assert.That(host.State.Clipboard).IsSameReferenceAs(second);
    }

    /// <summary>
    /// The loop offers what is waiting before it presents, so a copy reaches the window on the
    /// frame after the key that made it.
    /// </summary>
    [Test]
    public async Task TheLoopHandsTheCopyToTheWindow()
    {
        var window = new Desktop { Frames = 2 };
        var host = Copied(out _);

        ViewerProgram.Loop(host, window, null, null, null, new(), null, new(), new(ScreenBuilder.Build));

        await Assert.That(window.Taken).IsEquivalentTo(["the text"]);
        await Assert.That(host.State.Clipboard).IsNull();
    }

    static SessionHost Copied(out PendingCopy copy)
    {
        var host = new SessionHost(Fixtures.File());
        copy = host.Mutate(_ => _ with
        {
            Clipboard = new("the text", "the selection"),
            Message = "Copied 1 line from the selection."
        }).Clipboard!;
        return host;
    }

    /// <summary>
    /// A window whose clipboard can be held by someone else, and which closes after a number of
    /// frames.
    /// </summary>
    sealed class Desktop : IViewerWindow
    {
        int presented;

        public bool Busy { get; init; }

        public int Frames { get; init; } = 1;

        public List<string> Taken { get; } = [];

        public bool Present(Screen screen) =>
            ++presented < Frames;

        public ViewerInput Poll() =>
            new(CommandKind.None, -1, -1, 0, false, Fixtures.Columns, Fixtures.Rows);

        public void SetHidden(bool hidden)
        {
        }

        public void Focus()
        {
        }

        public bool SetClipboard(string text)
        {
            if (Busy)
            {
                return false;
            }

            Taken.Add(text);
            return true;
        }

        public bool Capture(Screen screen, int width, int height, string pngPath) =>
            false;

        public void Dispose()
        {
        }
    }
}
