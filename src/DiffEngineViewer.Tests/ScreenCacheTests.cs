/// <summary>
/// A screen is built when the state is another one, and not otherwise. The loop presents sixty
/// times a second, so anything built per frame is built for a window nobody is touching.
/// </summary>
public class ScreenCacheTests
{
    [Test]
    public async Task The_same_state_is_the_same_screen()
    {
        var cache = new ScreenCache();
        var state = Scrollable();

        var first = cache.For(state);

        await Assert.That(ReferenceEquals(cache.For(state), first)).IsTrue();
        await Assert.That(ReferenceEquals(cache.For(state), first)).IsTrue();
    }

    [Test]
    public async Task Another_state_is_another_screen()
    {
        var cache = new ScreenCache();
        var state = Scrollable();
        var first = cache.For(state);

        var scrolled = cache.For(ViewerSession.Apply(state, CommandKind.ScrollDown));

        await Assert.That(ReferenceEquals(scrolled, first)).IsFalse();
        await Assert.That(scrolled.Left.ScrollTop).IsEqualTo(first.Left.ScrollTop + 1);
    }

    /// <summary>
    /// Only the state last asked about is kept, so going back to an earlier one builds it again,
    /// and what comes back says the same thing.
    /// </summary>
    [Test]
    public async Task A_state_come_back_to_is_built_again_and_says_the_same()
    {
        var cache = new ScreenCache();
        var state = Scrollable();
        var first = cache.For(state);
        cache.For(ViewerSession.Apply(state, CommandKind.ScrollDown));

        var again = cache.For(state);

        await Assert.That(AsciiRenderer.Render(again)).IsEqualTo(AsciiRenderer.Render(first));
    }

    /// <summary>
    /// The loop itself: five frames with nothing in any of them are five presents of one screen.
    /// </summary>
    [Test]
    public async Task Frames_with_nothing_in_them_are_handed_the_screen_of_the_frame_before()
    {
        var window = new Recording(5, _ => Idle);

        ViewerProgram.Run(new(Scrollable()), server: null, link: null, window.Open);

        await Assert.That(window.Presented.Count).IsEqualTo(5);
        await Assert.That(window.Presented.All(_ => ReferenceEquals(_, window.Presented[0]))).IsTrue();
    }

    /// <summary>
    /// And a frame something happened in is followed by another screen, which then stands until
    /// something else does.
    /// </summary>
    [Test]
    public async Task A_frame_something_happened_in_is_followed_by_another_screen()
    {
        var window = new Recording(4, _ => _ == 1 ? Idle with { Key = CommandKind.ScrollDown } : Idle);

        ViewerProgram.Run(new(Scrollable()), server: null, link: null, window.Open);

        var presented = window.Presented;
        await Assert.That(presented.Count).IsEqualTo(4);
        await Assert.That(ReferenceEquals(presented[1], presented[0])).IsFalse();
        await Assert.That(presented[1].Left.ScrollTop).IsEqualTo(presented[0].Left.ScrollTop + 1);
        await Assert.That(ReferenceEquals(presented[2], presented[1])).IsTrue();
        await Assert.That(ReferenceEquals(presented[3], presented[1])).IsTrue();
    }

    /// <summary>
    /// Forty lines in a body of sixteen rows, opened at its first change on line 3, so there is
    /// somewhere to scroll to.
    /// </summary>
    static SessionState Scrollable() =>
        Fixtures.File(Fixtures.Long(true), Fixtures.Long(false));

    static readonly ViewerInput Idle = new(CommandKind.None, -1, -1, 0, false, Fixtures.Columns, Fixtures.Rows);

    /// <summary>
    /// Keeps every screen it is handed, and closes after a number of frames.
    /// </summary>
    /// <param name="frames">How many presents before the window says it has closed.</param>
    /// <param name="input">What the poll after each present reports, by how many there have been.</param>
    sealed class Recording(int frames, Func<int, ViewerInput> input) : IViewerWindow
    {
        public List<Screen> Presented { get; } = [];

        public IViewerWindow Open(string title, int width, int height, bool hidden, WindowPlacement? placement, out string? error)
        {
            error = null;
            return this;
        }

        public bool Present(Screen screen)
        {
            Presented.Add(screen);
            return Presented.Count < frames;
        }

        public ViewerInput Poll() =>
            input(Presented.Count);

        public void SetHidden(bool hidden)
        {
        }

        public void Focus()
        {
        }

        public void SetClipboard(string text)
        {
        }

        public bool Capture(Screen screen, int width, int height, string pngPath) =>
            false;

        public void Dispose()
        {
        }
    }
}
