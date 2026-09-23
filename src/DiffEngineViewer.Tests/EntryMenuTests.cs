/// <summary>
/// An entry's context menu acts on the entry selected when one of its items is clicked, so the
/// selection moving while the menu is open retargets it: Discard, clicked in a menu opened on one
/// entry, discarded another. Nothing the reader did has to happen for that - a focus from the tray
/// or an IDE, or a test re-sending a snapshot, moves the selection.
/// </summary>
public class EntryMenuTests
{
    /// <summary>
    /// A menu opened on A, then a wire Focus naming B - the tray menu or an IDE asking for it -
    /// through the real handler, then Discard.
    /// </summary>
    [Test]
    public async Task A_wire_focus_does_not_retarget_an_open_entry_menu()
    {
        var a = Fixtures.Patch("A.cs", 1);
        var b = Fixtures.Patch("B.cs", 2);
        var host = new SessionHost(Fixtures.Inline(a, b));
        var handler = new MessageHandler(host, Fixtures.Applied, _ =>
        {
        });
        host.Mutate(_ => ViewerSession.OpenMenu(_, VisibleRowOf(_, _ => _.EntryIndex == 0)));
        await Assert.That(host.State.Current!.Key).IsEqualTo(Key(a));
        var discard = MenuIndex(host.State, "Discard");

        var focus = handler.Handle(new(ViewerVerb.Focus, Key(b)));
        await Assert.That(focus.Ok).IsTrue();

        host.Mutate(_ => ViewerProgram.Apply(_, Click(discard), link: null, new NoWindow()));

        await Assert.That(host.State.Queue.Select(_ => _.Key)).Contains(Key(b));
    }

    /// <summary>
    /// Attached, with nothing but a test process involved. B's test fails again with the same
    /// content: a tray owner folds it into the same entry and stashes a focus on it, which its next
    /// full listing carries. The window syncs - the same entries, so the open menu is kept - and
    /// then selects B, under the menu opened on A.
    /// </summary>
    [Test]
    public async Task A_focus_riding_the_owners_listing_does_not_retarget_an_open_entry_menu()
    {
        var a = Fixtures.Patch("A.cs", 1);
        var b = Fixtures.Patch("B.cs", 2);
        using var owner = new TrayLikeOwner(Fixtures.Pending(a, b));
        var host = new SessionHost(SessionState.Start(ViewerMode.Inline, Fixtures.Columns, Fixtures.Rows));
        var link = new OwnerLink(host, owner.Port);
        await Assert.That(link.Pump()).IsTrue();
        host.Mutate(_ => ViewerSession.OpenMenu(_, VisibleRowOf(_, _ => _.EntryIndex == 0)));
        await Assert.That(host.State.Current!.Key).IsEqualTo(Key(a));
        var discard = MenuIndex(host.State, "Discard");

        owner.StashFocus(Key(b));
        await Assert.That(link.Pump()).IsTrue();

        host.Mutate(_ => ViewerProgram.Apply(_, Click(discard), link, new NoWindow()));
        link.Pump();

        await Assert.That(owner.Discarded).DoesNotContain(Key(b));
    }

    static string Key(InlinePatch patch) =>
        QueueEntry.KeyForInline(patch.SourceFile, patch.LineHint);

    static int VisibleRowOf(SessionState state, Func<QueueItem, bool> match) =>
        QueueProjection.Visible(state, ScreenBuilder.BodyRows(state), out _).ToList().FindIndex(_ => match(_));

    static int MenuIndex(SessionState state, string label) =>
        state.Menu!.Items.ToList().FindIndex(_ => _.Label == label);

    static ViewerInput Click(int menuItem) =>
        new(CommandKind.None, -1, -1, 0, false, Fixtures.Columns, Fixtures.Rows)
        {
            ClickedMenuItem = menuItem
        };

    sealed class NoWindow : IViewerWindow
    {
        public bool Present(Screen screen) =>
            true;

        public ViewerInput Poll() =>
            default;

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

    /// <summary>
    /// A queue owner answering the way OwnedInlineHost does where it matters here: a full listing
    /// of a fixed queue, carrying a stashed window command once. Discards are recorded rather than
    /// carried out.
    /// </summary>
    sealed class TrayLikeOwner : IDisposable
    {
        readonly CancelSource cancel = new();
        readonly ViewerServer server;
        readonly Task listening;
        readonly List<ViewerResponseItem> items;
        string? focus;

        public TrayLikeOwner(InlineQueue queue)
        {
            items = ViewerListing.Items(queue.Items, withPatches: true);
            if (!ViewerServer.TryBind(0, out var bound))
            {
                throw new("Could not bind an ephemeral port.");
            }

            server = bound;
            listening = server.Listen(Handle, cancel.Token);
        }

        public int Port => server.Port;

        public List<string?> Discarded { get; } = [];

        public void StashFocus(string key) =>
            Interlocked.Exchange(ref focus, key);

        ViewerResponse Handle(ViewerMessage message)
        {
            if (message.Verb == ViewerVerb.ListFull)
            {
                if (Interlocked.Exchange(ref focus, null) is { } key)
                {
                    return ViewerResponse.Listing(items, WindowCommand.Focus, key);
                }

                return ViewerResponse.Listing(items);
            }

            if (message.Verb == ViewerVerb.Discard)
            {
                lock (Discarded)
                {
                    Discarded.Add(message.Key);
                }
            }

            return ViewerResponse.Success();
        }

        public void Dispose()
        {
            cancel.Cancel();
            server.Dispose();
            try
            {
                listening.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }

            cancel.Dispose();
        }
    }
}
