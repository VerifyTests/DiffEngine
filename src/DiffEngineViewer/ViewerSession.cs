/// <summary>
/// The application logic, as pure state transitions. Every screen the user can reach is
/// reproducible by replaying commands, which is what the snapshot tests do.
/// <para>
/// What changes the inline queue is delegated to <see cref="InlineQueue"/>, the same
/// implementation DiffEngineTray hosts, so what enqueueing, settling or accepting means cannot
/// differ between the two. What stays here is the view: selection, scrolling, and the projection
/// back onto <see cref="QueueEntry"/>.
/// </para>
/// <para>
/// File mode keeps its own accept, because copying left over right is not something the tray ever
/// queues. It is deliberately not a queue: <c>RunFile</c> enqueues one comparison and runs without
/// a socket, so nothing arrives after it and every acting command is that one entry.
/// </para>
/// </summary>
static class ViewerSession
{
    public static SessionState Resize(SessionState state, int columns, int rows) =>
        Clamp(state with
        {
            Columns = Math.Max(40, columns),
            Rows = Math.Max(10, rows)
        });

    /// <summary>
    /// Adds a patch, or folds it into the existing entry for the same call site so a re-run of
    /// the same test updates its entry rather than appending a duplicate.
    /// </summary>
    public static SessionState EnqueueInline(SessionState state, InlinePatch patch)
    {
        // Nothing joins a queue whose window has committed to leaving. Returned as it is, and the
        // caller answering the wire refuses when it sees that
        if (state.Closing)
        {
            return state;
        }

        var key = InlineKey.For(patch.SourceFile, patch.LineHint);
        var current = state.Current;
        var pending = Pending(state);
        var queue = Rebuild(state, pending, pending.Enqueue(patch));
        // Grouping can reorder the list, so the selection follows its key rather than its index.
        var selected = current is null ? 0 : IndexOf(queue, current.Key);
        if (selected < 0)
        {
            // Enqueueing takes no entry away, so one that is no longer under its key has gone with
            // its call site to the line this patch reports: an accept above it moved the call, and
            // the queue took the re-run for what it was (InlineQueue.Enqueue). It is still the
            // entry being read, and the first in the list is not
            selected = Math.Max(IndexOf(queue, key), 0);
        }

        // Start the reader over, at the first change, only when the text under them changed.
        // Folding into an entry further down the list is not it, and neither is a re-send of what
        // is already there: Fold reports an identical patch as unchanged and Project hands back
        // the same entry, so a continuous runner re-sending the same failing snapshot every few
        // seconds used to bounce the reader to the top on every run. Asked of the entry rather
        // than of the key, since a patch can reach the entry on screen under a key that is not the
        // patch's own: the one it had before its call site moved
        var replaced = current is not null &&
                       !ReferenceEquals(queue[selected], current);

        var next = state with
        {
            Queue = queue,
            Selected = selected,
            // The open menu indexes the queue it was opened over, which just changed.
            Menu = null,
            // Something to show again. A settle that emptied the queue a moment ago set this, and
            // carrying it across the arrival took the new entry out with the window
            Exit = false
        };

        // Nothing on screen before means nobody has been reading this one yet either.
        if (current is null ||
            replaced)
        {
            return Open(next);
        }

        return Clamp(next);
    }

    /// <summary>
    /// The single entry a file comparison shows. Nothing arrives after it, because file mode runs
    /// without a socket.
    /// </summary>
    public static SessionState EnqueueFile(SessionState state, QueueEntry entry)
    {
        var next = state with
        {
            Queue = [..state.Queue, entry]
        };

        if (state.Selected < 0)
        {
            return Open(next with { Selected = 0 });
        }

        return Clamp(next);
    }

    /// <summary>
    /// Drops the item for a key, used when a previously failing test starts passing — or, with an
    /// origin, just that framework's variant of it.
    /// </summary>
    public static SessionState Settle(SessionState state, string key, string? origin = null, string? member = null, string? value = null)
    {
        // Nothing can settle a file comparison: settles arrive over the socket, and file mode runs
        // without one. Guarded rather than assumed, because Pending would dereference the null
        // patch a file entry carries.
        if (state.Mode != ViewerMode.Inline)
        {
            return state;
        }

        // A tracked key settles by being dropped. The pair is a test that now passes, so
        // DiffEngine has taken the received file away already and neither accepting nor
        // discarding this has anything left to act on - both would fail on a file that is gone.
        if (TrackedKeys.IsTracked(key))
        {
            var kept = state.Queue.Where(_ => _.Key != key).ToList();
            if (kept.Count == state.Queue.Count)
            {
                return state;
            }

            return Remove(state, kept, null);
        }

        var pending = Pending(state);
        var settled = pending.Settle(key, origin, member, value);
        if (ReferenceEquals(settled, pending))
        {
            return state;
        }

        return Remove(state, Rebuild(state, pending, settled), null);
    }

    /// <summary>
    /// Adds a pending move or delete, or replaces the entry for the same file: a re-run stages the
    /// same received file again, and a second entry for it would be a duplicate rather than news.
    /// <para>
    /// Takes a built entry rather than paths, because building one reads both files.
    /// <see cref="TrackedEntry"/> does that on the listener thread, which is the same seam
    /// <see cref="Sync"/> takes the tray's through.
    /// </para>
    /// <para>
    /// A re-send of what is already queued leaves the reader where they are, as
    /// <see cref="EnqueueInline"/> does. A test that keeps failing the same way sends its pair on
    /// every run, and each one used to open the entry again: back to its first change and its
    /// first page, fitted, with the menu closed.
    /// </para>
    /// </summary>
    public static SessionState EnqueueTracked(SessionState state, QueueEntry entry)
    {
        // As EnqueueInline: refused, by the caller, once the window has committed to leaving
        if (state.Closing)
        {
            return state;
        }

        var existing = IndexOf(state.Queue, entry.Key);
        if (existing >= 0 &&
            SameContent(state.Queue[existing], entry))
        {
            return Restaged(state, existing, entry);
        }

        var replacedCurrent = state.Current?.Key == entry.Key;
        var kept = state.Queue.Where(_ => _.Key != entry.Key);
        var queue = QueueProjection.Order([..kept, entry]);
        var currentKey = state.Current?.Key;
        var selected = currentKey is null ? 0 : IndexOf(queue, currentKey);
        var next = state with
        {
            Queue = queue,
            Selected = selected < 0 ? 0 : selected,
            Menu = null,
            // As EnqueueInline: an arrival is a reason to stay
            Exit = false
        };

        if (currentKey is null ||
            replacedCurrent)
        {
            return Open(next);
        }

        return Clamp(next);
    }

    /// <summary>
    /// The entry already queued for a pair that arrived again saying the same thing, with the
    /// files' new stamps and nothing else about the window changed.
    /// <para>
    /// A new entry all the same, never the one that was there. <see cref="TrackedWatch"/> applies
    /// what a pass found by reference, and a pass that looked while the run had cleared its
    /// received file found it gone: that must not take the pair the run has since staged again.
    /// </para>
    /// <para>
    /// The queued entry's content rather than the arrival's, because it can be further along: a
    /// document's text is read after it arrives, and the arrival may not have it yet.
    /// </para>
    /// </summary>
    static SessionState Restaged(SessionState state, int index, QueueEntry entry)
    {
        var queue = new List<QueueEntry>(state.Queue);
        queue[index] = queue[index] with
        {
            LeftStamp = entry.LeftStamp,
            RightStamp = entry.RightStamp
        };
        return Clamp(state with
        {
            Queue = queue,
            // As any arrival: a reason to stay
            Exit = false
        });
    }

    /// <summary>
    /// Whether two entries for one key show the same thing: the same files, holding the same
    /// text, pictures or documents. Stamps are left out, since a run that rewrites a file with
    /// what it already held changes those and nothing a reader can see.
    /// </summary>
    static bool SameContent(QueueEntry queued, QueueEntry arrived) =>
        queued.Kind == arrived.Kind &&
        queued.Name == arrived.Name &&
        queued.Solution == arrived.Solution &&
        queued.LeftFile == arrived.LeftFile &&
        queued.TargetFile == arrived.TargetFile &&
        queued.LeftHeader == arrived.LeftHeader &&
        queued.RightHeader == arrived.RightHeader &&
        queued.Warning == arrived.Warning &&
        QueueEntry.SameSide(queued.LeftText, queued.LeftImage, queued.LeftDocument, arrived.LeftText, arrived.LeftImage, arrived.LeftDocument) &&
        QueueEntry.SameSide(queued.RightText, queued.RightImage, queued.RightDocument, arrived.RightText, arrived.RightImage, arrived.RightDocument);

    /// <summary>
    /// Replaces the queue with what its owner reports, for a viewer that is displaying rather
    /// than owning: the inline entries plus the owner's tracked moves and deletes, already
    /// materialized by the poller. Selection follows the key, so something accepted elsewhere in
    /// the list does not silently change what is on screen, and the scroll position is only given
    /// up when the item being read has gone.
    /// </summary>
    /// <param name="progress">
    /// The owner's accept-all, when the listing was taken while one was running. Replaced on every
    /// listing rather than carried, so the one after the batch finishes is what clears it.
    /// </param>
    public static SessionState Sync(
        SessionState state,
        InlineQueue pending,
        IReadOnlyList<QueueEntry> changes,
        string? message,
        AcceptProgress? progress = null)
    {
        var entries = Project(state, pending);
        entries.AddRange(changes);
        var queue = QueueProjection.Order(entries);

        // A listing that changed nothing, which is most of them at five a second. Project and
        // ReadChanges hand back the same entries when nothing moved, so the list can be compared
        // by reference. Replacing it anyway cleared the open menu on every poll, so an attached
        // viewer's right-click menu closed within 200ms and a click on it went nowhere.
        if (message is null &&
            progress == state.OwnerProgress &&
            SameEntries(queue, state.Queue))
        {
            return state;
        }

        var key = state.Current?.Key;
        var selected = key is null ? -1 : IndexOf(queue, key);
        if (selected < 0)
        {
            // Gone from under its key is not always gone. An accept above a call site moves it,
            // and the owner takes its entry to the line the re-run reports it at
            selected = IndexOfMoved(queue, state.Current);
        }

        var next = state with
        {
            Queue = queue,
            Selected = selected < 0 ? state.Selected : selected,
            Message = message ?? state.Message,
            OwnerProgress = progress,
            // Nothing left to show, and this window is not what is holding the queue.
            Exit = queue.Count == 0,
            // The open menu indexes the queue it was opened over. Kept when the entries are the
            // same ones in the same places, since its indexes still mean what they did.
            Menu = SameEntries(queue, state.Queue) ? state.Menu : null
        };

        if (selected < 0)
        {
            return Reopen(next);
        }

        return Clamp(next);
    }

    /// <summary>
    /// A pass over the tracked files this process owns: entries whose file has gone drop out, and
    /// entries whose file changed underneath the window are replaced by the re-read one.
    /// <para>
    /// The owning half of what <see cref="Sync" /> does for a displaying viewer. An attached one
    /// re-reads the owner's files on every pump and so has always followed them; an owned queue is
    /// only ever pushed to, so its rows stayed frozen at the moment they arrived - showing content
    /// a re-run had already replaced, and offering a received file that was no longer there.
    /// </para>
    /// <para>
    /// Both arguments name the entries the pass looked at, and any of those no longer queued is
    /// skipped: the read that produced them ran outside the lock, so a patch or a pair can have
    /// arrived since, under the same key or not.
    /// A pass that changes nothing returns the same state, because this runs several times a
    /// second and rebuilding the queue - or clearing the open menu - on every one of them is not
    /// housekeeping the reader should be able to feel.
    /// </para>
    /// </summary>
    public static SessionState Refresh(
        SessionState state,
        IReadOnlyCollection<QueueEntry> gone,
        IReadOnlyList<(QueueEntry Seen, QueueEntry Fresh)> changed)
    {
        // By the entries the pass looked at, not by their keys: one staged again under the same
        // key since then is news the pass has not seen, and is left as it arrived.
        var replacements = new Dictionary<QueueEntry, QueueEntry>(ReferenceEqualityComparer.Instance);
        foreach (var (seen, fresh) in changed)
        {
            replacements[seen] = fresh;
        }

        var went = new HashSet<QueueEntry>(gone, ReferenceEqualityComparer.Instance);
        var queue = new List<QueueEntry>(state.Queue.Count);
        var any = false;
        var shownAnew = false;
        foreach (var entry in state.Queue)
        {
            if (went.Contains(entry))
            {
                any = true;
                shownAnew = true;
                continue;
            }

            if (replacements.TryGetValue(entry, out var fresh))
            {
                any = true;
                shownAnew |= !IsRestamped(entry, fresh);
                queue.Add(fresh);
                continue;
            }

            queue.Add(entry);
        }

        if (!any)
        {
            return state;
        }

        // Files written again with what they held, and nothing else: a run that fails the same
        // way does that every time. Every entry is where it was and shows what it showed, so the
        // open menu's indexes still mean what they did and nothing on screen is another thing.
        // The stamps have to be taken all the same, or every pass after reads the file again. As
        // the same pair arriving again over the socket is taken (Restaged), and for its reason:
        // going through Remove closed a menu the reader had open, once a run.
        if (!shownAnew)
        {
            return Clamp(state with { Queue = queue });
        }

        // The message is carried rather than cleared, unlike every other path through Remove: this
        // is not something the reader did, and "Accepted Foo" disappearing because an unrelated
        // file went away reads as the accept having been undone.
        return Remove(state, queue, state.Message);
    }

    /// <summary>
    /// Whether <paramref name="fresh"/> is <paramref name="seen"/> with its files' new stamps and
    /// nothing else: the copy <see cref="TrackedEntry.MoveAgain"/> makes of an entry whose files
    /// still hold what it shows. Told by its rows being the very rows, which a copy keeps and an
    /// entry built from the files again does not.
    /// </summary>
    static bool IsRestamped(QueueEntry seen, QueueEntry fresh) =>
        seen.Key == fresh.Key &&
        seen.Status == fresh.Status &&
        ReferenceEquals(seen.LeftRows, fresh.LeftRows) &&
        ReferenceEquals(seen.RightRows, fresh.RightRows);

    /// <summary>
    /// The loop's decision to leave, taken under the host's lock so it cannot cross an arrival:
    /// <see cref="SessionState.Exit"/> still set means nothing has joined the queue since it
    /// emptied, and from here nothing can. See <see cref="SessionState.Closing"/>.
    /// </summary>
    public static SessionState CommitExit(SessionState state)
    {
        if (!state.Exit)
        {
            return state;
        }

        return state with { Closing = true };
    }

    /// <summary>
    /// Selects by key rather than index, for a queue owner asking that a particular item be the
    /// one on screen. A key that is not here leaves the selection alone, because a listing and the
    /// command that came with it can disagree by one refresh.
    /// </summary>
    /// <summary>
    /// Something outside the window asked for an entry — the tray, or a second process handing one
    /// over. It is unfolded on the way, because a selection nobody can see is not a selection.
    /// </summary>
    public static SessionState SelectKey(SessionState state, string key)
    {
        var index = IndexOf(state.Queue, key);
        return index < 0 ? state : Select(Reveal(state, index), index);
    }

    /// <summary>
    /// Folds or unfolds a group, for a head reporting a click on a header row. The context menu
    /// reaches the same place through <see cref="CommandKind.ToggleGroup"/>.
    /// </summary>
    public static SessionState ToggleGroup(SessionState state, string key) =>
        // Menu cleared as every other command does, since this is the user moving on.
        Toggle(state with { Menu = null }, key);

    /// <summary>
    /// For commands that only move the view. Accept and accept all reach disk, so they go through
    /// the overload that takes the actions; passing one here throws rather than doing nothing.
    /// </summary>
    public static SessionState Apply(SessionState state, Command command) =>
        Apply(state, command, ViewerActions.None);

    /// <summary>
    /// Opens the context menu for a visible queue row. Opening on an entry selects it first, the
    /// way every menu-driven UI reads a right-click, so the menu's commands act on what is
    /// highlighted.
    /// </summary>
    public static SessionState OpenMenu(SessionState state, int visibleRow)
    {
        var body = ScreenBuilder.BodyRows(state);
        var visible = QueueProjection.Visible(state, body, out var top);
        if (visibleRow < 0 ||
            visibleRow >= visible.Count)
        {
            return state with { Menu = null };
        }

        var row = visible[visibleRow];
        var fullRow = top + visibleRow;
        if (row.Kind == QueueRowKind.Header)
        {
            if (row.GroupName is null ||
                row.GroupMembers is null)
            {
                return state with { Menu = null };
            }

            // Solution headers carry entries of every kind; a test header only ever spans inline
            // entries from one file.
            var collapsed = row.GroupKey is not null && state.Collapsed.Contains(row.GroupKey);
            var items = state.Queue[row.GroupMembers[0]].TestName == row.GroupName
                ? ContextMenu.ForTest(row.GroupName, collapsed)
                : ContextMenu.ForSolution(row.GroupName, collapsed);
            return state with
            {
                Menu = new(fullRow, items, row.GroupMembers)
                {
                    GroupKey = row.GroupKey
                }
            };
        }

        var selected = Select(state, row.EntryIndex);
        return selected with
        {
            Menu = new(
                fullRow,
                ContextMenu.ForEntry(
                    selected.Queue[row.EntryIndex],
                    selected.LiveSelection is { IsEmpty: false }),
                [row.EntryIndex])
        };
    }

    /// <summary>
    /// Opens the context menu for a pane: copy what is selected, copy the whole side, select the
    /// whole side. A right-click where there is nothing to copy closes whatever menu was open and
    /// opens none.
    /// <para>
    /// The selection is left as it is, unlike a right-click on a queue row, which selects the row.
    /// The usual reason to right-click a pane is to copy what was just dragged across, and
    /// clearing it on the way to the menu would leave nothing to copy.
    /// </para>
    /// </summary>
    public static SessionState OpenPaneMenu(SessionState state, PaneSide side)
    {
        if (state.Current is not { } current)
        {
            return state with { Menu = null };
        }

        var items = ContextMenu.ForPane(
            current,
            side,
            state.LiveSelection is { IsEmpty: false },
            !state.ShowsProperties);
        if (items.Count == 0)
        {
            return state with { Menu = null };
        }

        return state with
        {
            Menu = new(-1, items, [state.Selected])
            {
                Pane = side
            }
        };
    }

    /// <summary>
    /// The reader dragging out a range of pane text. The two ends arrive already in rows of the
    /// whole side rather than of the visible slice, because a head knows the scroll top it drew
    /// with and a drag that continues across a wheel notch has to mean the same thing either side
    /// of it.
    /// <para>
    /// Rows of the side the head drew, that is, which in the minimal view are not the entry's. They
    /// are unfolded here into the entry's own rows, which is what a selection is held in, so the
    /// heads never learn that a view can leave rows out.
    /// </para>
    /// <para>
    /// Reported for as long as the button is held, and simply not reported once it is let go: the
    /// selection is already here, so there is nothing for a release to say. That is what makes a
    /// whole press-drag-release landing inside one frame come out right.
    /// </para>
    /// </summary>
    public static SessionState Drag(
        SessionState state,
        PaneSide side,
        int anchorRow,
        int anchorColumn,
        int focusRow,
        int focusColumn)
    {
        if (state.Current is not { } current)
        {
            return state;
        }

        // Rows describing a document are not its text, and a selection is held in rows of the text:
        // one made here would come back over different rows after switching view. Still the user
        // moving on, so an open menu still closes.
        if (state.ShowsProperties)
        {
            return state.Menu is null ? state : state with { Menu = null };
        }

        var ends = current
            .View(state.Minimal)
            .Unfold(anchorRow, anchorColumn, focusRow, focusColumn);
        var selection = SelectionText.Clamp(
            new(
                current.Key,
                current.SelectedVariant,
                side,
                ends.AnchorRow,
                ends.AnchorColumn,
                ends.FocusRow,
                ends.FocusColumn)
            {
                Text = current.View(false)
            },
            current);

        // The identical state when the pointer has not left the cell it was in, which is most
        // frames of a drag. A fresh record every frame would repaint three heads for nothing.
        if (state.Menu is null &&
            selection == state.Selection)
        {
            return state;
        }

        // A drag is the user moving on, so it closes an open menu like every other input, and
        // drops whatever the last command reported: the status line is about to describe this
        // selection, and "Copied 3 lines" sitting over a different one is a lie.
        return state with
        {
            Selection = selection,
            Message = null,
            Menu = null
        };
    }

    /// <summary>
    /// Everything on one side. The side of the current selection, so select-all after a click in
    /// the expected pane takes that pane, and the received one before anything has been pointed
    /// at.
    /// </summary>
    /// <param name="pane">
    /// The pane a context menu asked in, which is the one meant whatever is selected elsewhere, or
    /// null for the key, which has no pane of its own.
    /// </param>
    static SessionState SelectAll(SessionState state, PaneSide? pane = null)
    {
        if (state.Current is not { } current ||
            state.ShowsProperties)
        {
            return state;
        }

        var side = pane ?? state.LiveSelection?.Side ?? PaneSide.Left;
        var rows = SelectionText.Rows(current, side);
        if (rows.Count == 0)
        {
            return state with { Selection = null };
        }

        var last = rows.Count - 1;
        return state with
        {
            Message = null,
            Selection = new(
                current.Key,
                current.SelectedVariant,
                side,
                0,
                0,
                last,
                SelectionText.Cells(RowText.Flatten(rows[last].Text)))
            {
                Text = current.View(false)
            }
        };
    }

    public static SessionState Apply(SessionState state, Command command, ViewerActions actions)
    {
        // Any command closes the menu: acting is what its own items do, and everything else —
        // a scroll, a click, a key — is the user moving on. The group commands still need what
        // the menu described, so it is captured before it goes.
        var menu = state.Menu;
        if (menu is not null)
        {
            state = state with { Menu = null };
        }

        var inline = state.Mode == ViewerMode.Inline;
        var body = ScreenBuilder.PaneRows(state);
        switch (command.Kind)
        {
            case CommandKind.AcceptGroup:
                return menu is null || !inline ? state : AcceptGroup(state, menu, actions);
            case CommandKind.DiscardGroup:
                return menu is null || !inline ? state : DiscardGroup(state, menu, actions);
            case CommandKind.ToggleGroup:
                return menu?.GroupKey is not { } key ? state : Toggle(state, key);
            case CommandKind.RevealSource:
                return Reveal(state, actions);
            case CommandKind.ScrollUp:
                return Scroll(state, state.ScrollTop - 1);
            case CommandKind.ScrollDown:
                return Scroll(state, state.ScrollTop + 1);
            case CommandKind.PageUp:
                return Scroll(state, state.ScrollTop - body);
            case CommandKind.PageDown:
                return Scroll(state, state.ScrollTop + body);
            case CommandKind.ScrollHome:
                return Scroll(state, 0);
            case CommandKind.ScrollEnd:
                return Scroll(state, int.MaxValue);
            case CommandKind.ScrollTo:
                return Scroll(state, command.Index);
            case CommandKind.NextChange:
                if (ScreenBuilder.TurnsPages(state, out _))
                {
                    return TurnToChange(state, forward: true);
                }

                return Scroll(state, state.View?.Next(state.ScrollTop, body) ?? state.ScrollTop);
            case CommandKind.PreviousChange:
                if (ScreenBuilder.TurnsPages(state, out _))
                {
                    return TurnToChange(state, forward: false);
                }

                return Scroll(state, state.View?.Previous(state.ScrollTop, body) ?? state.ScrollTop);
            case CommandKind.ToggleMinimal:
                return ToggleMinimal(state, body);
            case CommandKind.ToggleDrawing:
                return ToggleDrawing(state);
            case CommandKind.NextProjection:
                return NextProjection(state);
            case CommandKind.ZoomIn:
                return ZoomTo(state, state.Zoom + 1);
            case CommandKind.ZoomOut:
                return ZoomTo(state, state.Zoom - 1);
            case CommandKind.ZoomReset:
                return ZoomTo(state, 0);
            case CommandKind.PreviousPage:
                return Turn(state, -1);
            case CommandKind.NextPage:
                return Turn(state, 1);
            case CommandKind.NextItem:
                return Step(state, 1);
            case CommandKind.PreviousItem:
                return Step(state, -1);
            case CommandKind.SelectItem:
                return Select(state, command.Index);
            case CommandKind.Accept:
                if (!inline)
                {
                    return AcceptFile(state, actions);
                }

                // A move or a delete is applied by whoever holds it, and in queue mode that is
                // either this process or the owner this one forwards to. Reaching here means the
                // former, because forwarding never gets this far.
                return state.Current is { Kind: QueueEntryKind.Move or QueueEntryKind.Delete } accepting
                    ? AcceptTracked(state, accepting, actions)
                    : AcceptInline(state, actions);
            case CommandKind.AcceptAll:
                // File mode shows one comparison and cannot grow, so accepting all of it is
                // accepting it. Reachable through shift+A even though the button is disabled for
                // a single item, so it behaves rather than being a hole.
                return inline ? AcceptAllInline(state, actions) : AcceptFile(state, actions);
            case CommandKind.Discard:
                if (!inline)
                {
                    return DiscardFile(state);
                }

                return state.Current is { Kind: QueueEntryKind.Move or QueueEntryKind.Delete } discarding
                    ? DiscardTracked(state, discarding, actions)
                    : DiscardInline(state);
            case CommandKind.DiscardAll:
                return inline ? DiscardAllInline(state, actions) : DiscardFile(state);
            case CommandKind.SelectAll:
                return SelectAll(state, menu?.Pane);
            case CommandKind.NextVariant:
                return NextVariant(state);
            case CommandKind.Quit:
                // A request, not an exit: the loop folds it into the same close semantics as the
                // window's close button, which is what lets a tray arrangement hide instead of
                // exit and an owning viewer persist what it holds on the way out.
                return state with { QuitRequested = true };
            default:
                return state;
        }
    }

    static SessionState AcceptInline(SessionState state, ViewerActions actions)
    {
        var current = state.Current;
        if (current is not { Kind: QueueEntryKind.Inline })
        {
            return state;
        }

        var pending = Pending(state);
        InlineQueue accepted;
        string? message;
        // What the applier answered, for the lines it moved
        InlineApplyResult? applied = null;
        InlineApplyResult Apply(InlinePatch patch) =>
            applied = actions.ApplyInline(patch);

        if (current.Conflicted &&
            current.Variants[current.SelectedVariant].Origins is { Count: > 0 } origins &&
            origins[0] is { } origin)
        {
            // The reviewer picked a side by cycling to it; accepting applies exactly what is on
            // screen and resolves the whole call site.
            accepted = pending.Accept(current.Key, origin, Apply, out message);
        }
        else
        {
            accepted = pending.Accept(current.Key, Apply, out message);
        }

        var queue = Rebuild(state, pending, accepted);
        if (accepted.Count < pending.Count)
        {
            if (applied is not null)
            {
                queue = Rebased(queue, current.Patch!.SourceFile, [applied]).Queue;
            }

            return Remove(state, queue, message);
        }

        // Kept pending so it can be retried, for example when an IDE holds the file open.
        return state with
        {
            Queue = queue,
            Message = message
        };
    }

    /// <summary>
    /// A whole group accept in one call, for a caller that already holds the state: what
    /// <see cref="AcceptAllInline"/> is to an accept-all, and the same batch underneath.
    /// </summary>
    static SessionState AcceptGroup(SessionState state, MenuState menu, ViewerActions actions)
    {
        // For the reason AcceptAllInline gives
        if (state.Batch is not null)
        {
            return state;
        }

        state = BeginAcceptGroup(state, menu);
        while ((state = ClaimNext(state)).Batch?.Current is not null)
        {
            state = ApplyClaimed(state, actions)(state);
        }

        return state;
    }

    /// <summary>
    /// Starts an accept of every member of the group the open menu's header describes, without
    /// applying anything yet: an accept-all over those members and nothing else, carried out the
    /// way <see cref="BeginAcceptAll"/> says. The state as it is, less the menu, when there is no
    /// group to accept.
    /// <para>
    /// It used to be one transition that applied every member before it returned, on the render
    /// thread and under the lock every arrival waits on. In a queue of one solution that header's
    /// group is the whole queue, so it was the freeze the batch was written to remove.
    /// </para>
    /// <para>
    /// A solution header spans tracked moves and deletes as well as snapshots, so the batch does
    /// too. Skipping them would make "Accept all in ..." quietly mean "accept the snapshots in
    /// ...", which is the divergence the unqualified accept-all already avoids.
    /// </para>
    /// <para>
    /// Being the same batch, it goes by the batch's rules: a snapshot the applier would not take
    /// stays in the queue with what the applier said, where a group accept used to drop it as a
    /// single accept does. That holds this group's deletes either way, and counts only this
    /// group's own attempts in doing so.
    /// </para>
    /// </summary>
    public static SessionState BeginAcceptGroup(SessionState state)
    {
        if (state.Menu is not { } menu)
        {
            return state;
        }

        state = state with { Menu = null };
        if (state.Mode != ViewerMode.Inline)
        {
            return state;
        }

        return BeginAcceptGroup(state, menu);
    }

    // By key rather than index, because each accept rebuilds the queue underneath the next.
    static SessionState BeginAcceptGroup(SessionState state, MenuState menu) =>
        BeginAccept(
            state,
            Members(state, menu)
                .Select(_ => _.Key)
                .ToList());

    /// <summary>
    /// A whole group discard in one call, for a caller that already holds the state: the steps
    /// <see cref="AcceptAllRunner"/> takes a mutation at a time, taken back to back.
    /// </summary>
    static SessionState DiscardGroup(SessionState state, MenuState menu, ViewerActions actions)
    {
        // For the reason AcceptAllInline gives
        if (state.Batch is not null)
        {
            return state;
        }

        return Carry(BeginDiscard(state, Members(state, menu)), actions);
    }

    /// <summary>
    /// Starts a discard of every member of the group the open menu's header describes, without
    /// throwing any file away yet: see <see cref="BeginDiscardAll"/>. The state as it is, less
    /// the menu, when there is no group to discard.
    /// </summary>
    public static SessionState BeginDiscardGroup(SessionState state)
    {
        if (state.Menu is not { } menu)
        {
            return state;
        }

        state = state with { Menu = null };
        if (state.Mode != ViewerMode.Inline)
        {
            return state;
        }

        return BeginDiscard(state, Members(state, menu));
    }

    /// <summary>
    /// Starts a discard of everything in a queue this process owns. The snapshots are dropped and
    /// the pending deletes untracked here and now, since neither touches a file. The moves are
    /// left in the queue as a batch to carry out through <see cref="ClaimNext"/> and
    /// <see cref="ApplyClaimed"/>, one received file thrown away per step and outside the lock:
    /// see <see cref="AcceptBatch.Discarding"/>. With no move among them it is finished where it
    /// started. A no-op while a batch is already running.
    /// </summary>
    public static SessionState BeginDiscardAll(SessionState state) =>
        BeginDiscard(state, null);

    /// <param name="state">The state to start it in.</param>
    /// <param name="members">
    /// The entries to discard, for a group header acting on its own members. Null discards
    /// everything, which is what the unqualified discard-all means.
    /// </param>
    static SessionState BeginDiscard(SessionState state, IReadOnlyList<QueueEntry>? members)
    {
        if (state.Mode != ViewerMode.Inline ||
            state.Batch is not null)
        {
            return state;
        }

        var pending = Pending(state);
        InlineQueue discarded;
        string said;
        if (members is null)
        {
            discarded = pending.DiscardAll(out said);
        }
        else
        {
            var keys = members
                .Where(_ => _.Kind == QueueEntryKind.Inline)
                .Select(_ => _.Key)
                .ToList();
            discarded = pending;
            foreach (var key in keys)
            {
                discarded = discarded.Discard(key, out _);
            }

            said = $"Discarded {keys.Count}";
        }

        var only = members is null ? null : TrackedKeysOf(members).ToHashSet();
        var remaining = new List<QueueEntry>();
        var moves = new List<string>();
        var untracked = 0;
        foreach (var entry in Rebuild(state, pending, discarded))
        {
            if (entry.Kind is not (QueueEntryKind.Move or QueueEntryKind.Delete) ||
                (only is not null && !only.Contains(entry.Key)))
            {
                remaining.Add(entry);
                continue;
            }

            // Discarding a pending delete leaves the file alone and only untracks it
            if (entry.Kind == QueueEntryKind.Delete)
            {
                untracked++;
                continue;
            }

            moves.Add(entry.Key);
            remaining.Add(entry);
        }

        var batch = new AcceptBatch(moves, moves.Count)
        {
            Discarding = true,
            Said = said,
            Swept = untracked
        };
        var begun = Remove(state, remaining, null);
        // No received file to throw away, so nothing to report progress on
        if (moves.Count == 0)
        {
            return Finish(begun, batch);
        }

        return begun with { Batch = batch };
    }

    /// <summary>
    /// Whatever batch a state holds, carried out to its end in one call.
    /// </summary>
    static SessionState Carry(SessionState state, ViewerActions actions)
    {
        while ((state = ClaimNext(state)).Batch?.Current is not null)
        {
            state = ApplyClaimed(state, actions)(state);
        }

        return state;
    }

    static List<QueueEntry> Members(SessionState state, MenuState menu) =>
        menu.Members
            .Where(_ => _ >= 0 && _ < state.Queue.Count)
            .Select(_ => state.Queue[_])
            .ToList();

    static List<string> TrackedKeysOf(IEnumerable<QueueEntry> entries) =>
        entries
            .Where(_ => _.Kind is QueueEntryKind.Move or QueueEntryKind.Delete)
            .Select(_ => _.Key)
            .ToList();

    /// <summary>
    /// Shows the current entry's file in the platform's file manager: the source for an inline
    /// entry, the target for a move, the doomed file for a delete, the left file in file mode.
    /// </summary>
    static SessionState Reveal(SessionState state, ViewerActions actions)
    {
        var current = state.Current;
        var path = current?.Kind switch
        {
            QueueEntryKind.Inline => current.Patch?.SourceFile,
            QueueEntryKind.Move => current.TargetFile,
            QueueEntryKind.Delete or QueueEntryKind.File => current.LeftFile,
            _ => null
        };
        if (path is not null)
        {
            actions.Reveal(path);
        }

        return state;
    }

    /// <summary>
    /// Rebuilds the current entry showing its next variant. A no-op unless the entry is a
    /// conflicted inline one, and purely view state: it changes what is being read, never a file.
    /// </summary>
    static SessionState NextVariant(SessionState state)
    {
        var current = state.Current;
        if (current is not { Kind: QueueEntryKind.Inline, Conflicted: true })
        {
            return state;
        }

        var rebuilt = QueueEntry.ForInline(
            new(current.Variants, current.Status),
            (current.SelectedVariant + 1) % current.Variants.Count);
        var queue = state.Queue.ToList();
        queue[state.Selected] = rebuilt;
        // The text under the reader changed, the same reason a fold resets it.
        return Open(state with { Queue = queue });
    }

    /// <summary>
    /// Points the current selection's entry at the variant carrying an origin, for a wire accept
    /// that named one. A key or origin that is not here leaves the state alone.
    /// </summary>
    public static SessionState SelectVariant(SessionState state, string origin)
    {
        var current = state.Current;
        if (current is not { Kind: QueueEntryKind.Inline })
        {
            return state;
        }

        for (var index = 0; index < current.Variants.Count; index++)
        {
            if (!current.Variants[index].Origins.Contains(origin))
            {
                continue;
            }

            if (index == current.SelectedVariant)
            {
                return state;
            }

            var queue = state.Queue.ToList();
            queue[state.Selected] = QueueEntry.ForInline(new(current.Variants, current.Status), index);
            return Open(state with { Queue = queue });
        }

        return state;
    }

    /// <summary>
    /// A whole accept-all in one call, for a caller that already holds the state: the steps
    /// <see cref="AcceptAllRunner"/> takes a mutation at a time, taken back to back. One
    /// implementation of the batch rather than two, so the one the tests drive is the one a window
    /// runs.
    /// </summary>
    static SessionState AcceptAllInline(SessionState state, ViewerActions actions)
    {
        // A runner is part way through one, and claiming its entries from here as well would apply
        // them twice
        if (state.Batch is not null)
        {
            return state;
        }

        state = BeginAcceptAll(state);
        while ((state = ClaimNext(state)).Batch?.Current is not null)
        {
            state = ApplyClaimed(state, actions)(state);
        }

        return state;
    }

    /// <summary>
    /// Starts an accept-all over this process's own queue without applying anything yet: see
    /// <see cref="AcceptBatch"/>. Whoever starts one carries it out through <see cref="ClaimNext"/>
    /// and <see cref="ApplyClaimed"/>, one mutation per step, so the lock is never held across an
    /// apply. A no-op while one is already running, since that is the batch a second request was
    /// asking for.
    /// <para>
    /// Conflicted snapshots are left out, the way every bulk accept leaves them out: it has no
    /// honest way to pick a side. They are counted into the message once the batch has finished.
    /// </para>
    /// </summary>
    public static SessionState BeginAcceptAll(SessionState state) =>
        BeginAccept(state, null);

    /// <summary>
    /// A batch over the whole queue or over some of it. One method with a name of its own, rather
    /// than an optional argument on <see cref="BeginAcceptAll"/>, so that one is still a
    /// transition a host can be handed as it is.
    /// </summary>
    /// <param name="state">The state to start it in.</param>
    /// <param name="only">
    /// The keys to accept, for a group header acting on its own members. Null accepts everything,
    /// which is what the unqualified accept-all means.
    /// </param>
    static SessionState BeginAccept(SessionState state, IReadOnlyCollection<string>? only)
    {
        if (state.Mode != ViewerMode.Inline ||
            state.Batch is not null)
        {
            return state;
        }

        var batch = new AcceptBatch([], 0)
        {
            Only = only?.ToHashSet()
        };
        var keys = new List<string>();
        foreach (var entry in state.Queue)
        {
            if (entry is { Kind: QueueEntryKind.Inline, Conflicted: false } &&
                batch.Covers(entry.Key))
            {
                keys.Add(entry.Key);
            }
        }

        foreach (var entry in state.Queue)
        {
            if (entry.Kind is QueueEntryKind.Move or QueueEntryKind.Delete &&
                batch.Covers(entry.Key))
            {
                keys.Add(entry.Key);
            }
        }

        batch = batch with
        {
            Remaining = keys,
            Total = keys.Count
        };
        // Nothing to apply, so nothing to report progress on: finished where it started
        if (keys.Count == 0)
        {
            return Finish(state, batch);
        }

        return state with
        {
            Batch = batch,
            // What the status line says from here is how far the batch has got, then what it did
            Message = null,
            Menu = null
        };
    }

    /// <summary>
    /// Claims the next entry of the running batch, so it can be applied outside the lock: it is
    /// <see cref="AcceptBatch.Current"/> in the state this returns. With nothing left to claim, the
    /// batch is finished instead, and its message becomes the state's.
    /// <para>
    /// Entries with nothing left to apply are passed over rather than claimed: one that has gone
    /// since the batch began - settled, discarded, its file taken away - and one a second
    /// framework has since made a conflict of. A delete is held rather than claimed once a
    /// snapshot in the batch was not written.
    /// </para>
    /// <para>
    /// A snapshot moving inline arrives as two unrelated entries: the patch that writes the literal
    /// into the source, and a delete of the verified file it replaces. A bulk accept ran the delete
    /// whether or not the patch landed, so a patch the applier would not take — a call site that
    /// cannot host a Snapshot call, a source that moved since the run — cost the snapshot both
    /// copies at once. Nothing ties a delete to the patch it belongs to, so every delete of the
    /// batch waits on every patch of it. Blunt, and deliberately so — the entries held are still
    /// queued, still shown, and still acceptable one at a time. Moves are left alone: a received
    /// file promoted over a verified one is the snapshot arriving, not the last copy of it leaving.
    /// </para>
    /// <para>
    /// Counted from the attempts the batch made (<see cref="AcceptAllTally.Refused"/>), never read
    /// off the queue. Every status an entry carries looks the same there, and reading them held a
    /// group's deletes over a failure in another solution, left by an accept long before this one.
    /// </para>
    /// </summary>
    public static SessionState ClaimNext(SessionState state)
    {
        // Nothing running, or an entry already out: recording it is what frees the next claim
        if (state.Batch is not { Current: null } batch)
        {
            return state;
        }

        var queue = state.Queue;
        var kept = batch.Kept;
        for (var position = 0; position < batch.Remaining.Count; position++)
        {
            var index = IndexOf(queue, batch.Remaining[position]);
            if (index < 0)
            {
                continue;
            }

            var entry = queue[index];
            if (entry is { Kind: QueueEntryKind.Inline, Conflicted: true })
            {
                continue;
            }

            if (entry.Kind == QueueEntryKind.Delete &&
                batch.Tally.Refused)
            {
                kept++;
                queue = Replace(queue, index, entry with { Status = deleteHeld });
                continue;
            }

            var remaining = batch.Remaining.Skip(position + 1).ToList();
            return state with
            {
                Queue = queue,
                Batch = batch with
                {
                    Kept = kept,
                    Current = entry,
                    Together = TakeSameFile(queue, entry, remaining),
                    Remaining = remaining
                }
            };
        }

        return Finish(
            state with { Queue = queue },
            batch with
            {
                Remaining = [],
                Kept = kept
            });
    }

    /// <summary>
    /// The other snapshots the batch still has to do in the same source file as the one just
    /// claimed, taken out of <paramref name="remaining"/> to be claimed with it: see
    /// <see cref="AcceptBatch.Together"/>. None for a move or a delete, which is a file of its own.
    /// <para>
    /// Claimed, rather than looked ahead to by whoever applies, so that nothing can settle,
    /// discard or replace one of them between its patch being written and its outcome being
    /// recorded without the record noticing, as it notices for a single entry.
    /// </para>
    /// <para>
    /// Asked of the queue first, and without making anything, since most claims find no other
    /// snapshot in their file and a batch makes one claim an entry.
    /// </para>
    /// </summary>
    static IReadOnlyList<QueueEntry> TakeSameFile(IReadOnlyList<QueueEntry> queue, QueueEntry claimed, List<string> remaining)
    {
        if (claimed is not { Kind: QueueEntryKind.Inline, Patch: { } patch } ||
            remaining.Count == 0)
        {
            return [];
        }

        List<QueueEntry>? sameFile = null;
        foreach (var entry in queue)
        {
            if (!ReferenceEquals(entry, claimed) &&
                entry is { Kind: QueueEntryKind.Inline, Conflicted: false, Patch: not null } &&
                InlineKey.SamePath(entry.Patch.SourceFile, patch.SourceFile))
            {
                sameFile ??= [];
                sameFile.Add(entry);
            }
        }

        if (sameFile is null)
        {
            return [];
        }

        // Only the ones this batch set out to do: a group's batch is some of the queue, and an
        // entry that arrived after it began is not part of it
        var waiting = new HashSet<string>(remaining);
        sameFile.RemoveAll(_ => !waiting.Contains(_.Key));
        if (sameFile.Count == 0)
        {
            return [];
        }

        var taken = new HashSet<string>(sameFile.Select(_ => _.Key));
        remaining.RemoveAll(taken.Contains);
        return sameFile;
    }

    /// <summary>
    /// Applies what a state has claimed - the one piece of IO in a batch, done without the lock -
    /// and hands back the transition that records how it went, for the caller to take the lock for.
    /// Snapshots claimed together are written together, and each still has an outcome of its own.
    /// </summary>
    /// <param name="claimed">The state <see cref="ClaimNext"/> returned, which says what was claimed.</param>
    /// <param name="actions">What applies it.</param>
    public static Func<SessionState, SessionState> ApplyClaimed(SessionState claimed, ViewerActions actions)
    {
        if (claimed.Batch is not { Current: { } entry } batch)
        {
            return static _ => _;
        }

        if (entry.Kind != QueueEntryKind.Inline)
        {
            var failure = TryApplyTracked(entry, actions, batch.Discarding);
            return _ => RecordTracked(_, entry, failure);
        }

        List<QueueEntry> entries = [entry, ..batch.Together];
        var results = actions.ApplyTogether(entries.Select(_ => _.Patch!).ToList());
        if (results.Count != entries.Count)
        {
            throw new InvalidOperationException($"{entries.Count} snapshots were applied together and {results.Count} outcomes came back.");
        }

        return _ => RecordInline(_, entries, results);
    }

    /// <summary>
    /// The transition for a claim whose apply threw rather than answering. InlineApplier answers
    /// every failure it knows of, so this is an applier that did not, and a batch left holding
    /// what it claimed would never finish. Every snapshot of the claim is failed with it, since
    /// which of them were written is not known.
    /// </summary>
    public static Func<SessionState, SessionState> FailClaimed(SessionState claimed, string failure)
    {
        if (claimed.Batch is not { Current: { } entry } batch)
        {
            return static _ => _;
        }

        if (entry.Kind != QueueEntryKind.Inline)
        {
            return _ => RecordTracked(_, entry, failure);
        }

        List<QueueEntry> entries = [entry, ..batch.Together];
        var failed = InlineApplyResult.Failed(failure);
        var results = entries.Select(_ => failed).ToList();
        return _ => RecordInline(_, entries, results);
    }

    /// <summary>
    /// The outcomes of the snapshots a claim applied, by the batch's rules rather than a single
    /// accept's: see <see cref="InlineQueue.AcceptInBatch"/>. An entry that changed while its
    /// patch was applying, because a re-run replaced it, keeps its new content and is not counted.
    /// <para>
    /// The rules are still asked of an <see cref="InlineQueue"/>, but of one holding the entry
    /// alone, and what it says is done to the list as it stands: the entry taken out, or given the
    /// status. Every inline transition used to rebuild the whole list from the whole queue, and a
    /// batch did that once an entry, so its own bookkeeping grew with the square of the queue:
    /// 2,000 snapshots were 3.7 seconds and 8.8 GB of garbage beside the applying. Taking an entry
    /// out of a list that is in order leaves it in order, and no other entry is touched, so there
    /// is nothing for a rebuild to find.
    /// </para>
    /// </summary>
    static SessionState RecordInline(SessionState state, IReadOnlyList<QueueEntry> entries, IReadOnlyList<InlineApplyResult> results)
    {
        if (state.Batch is not { } batch)
        {
            return state;
        }

        var tally = batch.Tally;
        var queue = state.Queue.ToList();
        for (var claim = 0; claim < entries.Count; claim++)
        {
            var entry = entries[claim];
            // By its variants, which is how the batch finds what it started on. A snapshot's
            // only: every move and delete has none, and may well share the one empty list.
            var index = queue.FindIndex(_ => _.Kind == QueueEntryKind.Inline && ReferenceEquals(_.Variants, entry.Variants));
            if (index < 0)
            {
                continue;
            }

            var held = queue[index];
            var outcome = InlineQueue
                .From([new(held.Variants, held.Status)])
                .AcceptInBatch(new(entry.Variants, entry.Status), results[claim], ref tally);
            if (outcome.Count == 0)
            {
                queue.RemoveAt(index);
            }
            else
            {
                queue[index] = held with { Status = outcome.Items[0].Status };
            }
        }

        // Once every outcome is in, since a claimed entry is found by its variants and one that
        // has moved has others. What is left of the file - a conflict, a snapshot that was not
        // written, one that arrived since - goes to where its call site now is
        var (rebased, moved) = Rebased(queue, entries[0].Patch!.SourceFile, results);
        if (moved is not null)
        {
            // The batch names its entries by key: what it has still to do, where a conflict is
            // passed over when its turn comes and something else of this file may now stand
            // under the key it had, and a group's members, which is how its conflicts are
            // counted at the end
            batch = batch with
            {
                Remaining = batch
                    .Remaining
                    .Select(_ => moved.GetValueOrDefault(_, _))
                    .ToList(),
                Only = batch
                    .Only?
                    .Select(_ => moved.GetValueOrDefault(_, _))
                    .ToHashSet()
            };
        }

        return Remove(
            state with
            {
                Batch = batch with
                {
                    Tally = tally,
                    Current = null,
                    Together = []
                }
            },
            rebased,
            state.Message,
            moved);
    }

    /// <summary>
    /// The list with the snapshots of <paramref name="sourceFile"/> taken to the lines their
    /// call sites are on after the edits <paramref name="results"/> report, and the keys that
    /// changed, old to new, or null when none did. See <see cref="InlineQueue.Rebased"/>, which
    /// decides it.
    /// <para>
    /// Done to the list where it stands and not by rebuilding it from the queue: an entry at
    /// another line is the same texts and the same diff under another key, and a list ordered
    /// by solution and test is still in order. Rebuilt, each of them was diffed again, a file's
    /// worth for every accept in it.
    /// </para>
    /// </summary>
    static (IReadOnlyList<QueueEntry> Queue, Dictionary<string, string>? Moved) Rebased(
        IReadOnlyList<QueueEntry> queue,
        string sourceFile,
        IReadOnlyList<InlineApplyResult> results)
    {
        if (!results.Any(_ => _.MovedBy != 0))
        {
            return (queue, null);
        }

        var indexes = new List<int>();
        for (var index = 0; index < queue.Count; index++)
        {
            if (queue[index] is { Kind: QueueEntryKind.Inline, Patch: { } patch } &&
                InlineKey.SamePath(patch.SourceFile, sourceFile))
            {
                indexes.Add(index);
            }
        }

        if (indexes.Count == 0)
        {
            return (queue, null);
        }

        var before = InlineQueue.From(indexes.Select(_ => new PendingInline(queue[_].Variants, queue[_].Status)));
        var after = before.Rebased(sourceFile, results);
        if (ReferenceEquals(after, before))
        {
            return (queue, null);
        }

        var list = queue.ToList();
        var moved = new Dictionary<string, string>();
        for (var position = 0; position < indexes.Count; position++)
        {
            var pending = after.Items[position];
            if (ReferenceEquals(pending, before.Items[position]))
            {
                continue;
            }

            var entry = list[indexes[position]];
            moved[entry.Key] = pending.Key;
            list[indexes[position]] = entry with
            {
                Key = pending.Key,
                Name = pending.Name,
                Patch = pending.Variants[entry.SelectedVariant].Patch,
                Variants = pending.Variants
            };
        }

        return (list, moved);
    }

    /// <summary>
    /// A move or delete's outcome. Counted whether or not its entry is still there, since the
    /// watch over owned files drops a move whose received file has gone, which is exactly what
    /// carrying the move out does. Only the entry that was claimed is removed or marked, though:
    /// one a re-run staged over it since is news, and neither the file operation nor its failure
    /// was about that one.
    /// </summary>
    static SessionState RecordTracked(SessionState state, QueueEntry entry, string? failure)
    {
        if (state.Batch is not { } batch)
        {
            return state;
        }

        var index = IndexOf(state.Queue, entry.Key);
        var claimed = index >= 0 && ReferenceEquals(state.Queue[index], entry);
        if (failure is null)
        {
            return Remove(
                state with
                {
                    Batch = batch with
                    {
                        Swept = batch.Swept + 1,
                        Current = null
                    }
                },
                claimed ? Without(state.Queue, index) : state.Queue,
                state.Message);
        }

        return Remove(
            state with
            {
                Batch = batch with
                {
                    Kept = batch.Kept + 1,
                    Current = null
                }
            },
            claimed ? Replace(state.Queue, index, entry with { Status = failure }) : state.Queue,
            state.Message);
    }

    /// <summary>
    /// The batch done: the sentence a bulk accept ends with, and nothing left saying one is
    /// running. Conflicts are counted here, as whatever the queue still holds with more than one
    /// variant, which includes any a second framework made while the batch ran. A group's batch
    /// counts its own members only.
    /// </summary>
    static SessionState Finish(SessionState state, AcceptBatch batch)
    {
        // A discard says what its beginning said of the snapshots, then the files: worded the way
        // an owning tray words its own, with what stayed pending counted rather than hidden
        if (batch.Discarding)
        {
            return Remove(
                state with { Batch = null },
                state.Queue,
                WithFiles(batch.Said, batch.Swept, batch.Kept));
        }

        var conflicted = 0;
        foreach (var entry in state.Queue)
        {
            if (entry is { Kind: QueueEntryKind.Inline, Conflicted: true } &&
                batch.Covers(entry.Key))
            {
                conflicted++;
            }
        }

        return Remove(
            state with { Batch = null },
            state.Queue,
            WithFiles(batch.Tally.Message(conflicted), batch.Swept, batch.Kept));
    }

    /// <summary>
    /// Whether a command acts on the queue rather than on the view: what a window refuses while an
    /// accept-all is working through the queue, whichever process is running it.
    /// </summary>
    public static bool ChangesQueue(CommandKind kind) =>
        kind is
            CommandKind.Accept or
            CommandKind.AcceptAll or
            CommandKind.AcceptGroup or
            CommandKind.Discard or
            CommandKind.DiscardAll or
            CommandKind.DiscardGroup;

    static List<QueueEntry> Replace(IReadOnlyList<QueueEntry> queue, int index, QueueEntry entry) =>
        [..queue.Take(index), entry, ..queue.Skip(index + 1)];

    static List<QueueEntry> Without(IReadOnlyList<QueueEntry> queue, int index) =>
        [..queue.Take(index), ..queue.Skip(index + 1)];

    /// <summary>
    /// A whole discard-all in one call, for a caller that already holds the state: what
    /// <see cref="AcceptAllInline"/> is to an accept-all, and the same batch underneath.
    /// </summary>
    static SessionState DiscardAllInline(SessionState state, ViewerActions actions)
    {
        // For the reason AcceptAllInline gives
        if (state.Batch is not null)
        {
            return state;
        }

        return Carry(BeginDiscardAll(state), actions);
    }

    /// <summary>
    /// The files clause of a bulk command, after the inline summary: ", plus n files", with what
    /// stayed pending counted rather than hidden. Shared by the bulk discards and the accept-all
    /// batch, so the two cannot word the same files differently.
    /// </summary>
    static string WithFiles(string message, int swept, int kept)
    {
        if (swept == 0 &&
            kept == 0)
        {
            return message;
        }

        var clause = kept == 0 ? $"{swept} files" : $"{swept} files ({kept} kept)";
        return $"{message}, plus {clause}";
    }

    const string deleteHeld = "Held: a snapshot in this batch could not be written inline, and this file may be the only copy of it left. Accept it on its own to delete it anyway.";

    /// <summary>
    /// Accepting a tracked entry is the file operation it describes; discarding one is throwing
    /// the received file away, or, for a delete, leaving the file alone and only untracking it —
    /// which is what discarding a pending delete has always meant.
    /// <para>
    /// Returns null when it went, and the failure otherwise. A failed entry stays pending carrying
    /// the reason, so it can be retried once whatever holds the file is gone, exactly as a failed
    /// inline apply does.
    /// </para>
    /// </summary>
    static string? TryApplyTracked(QueueEntry entry, ViewerActions actions, bool discarding)
    {
        try
        {
            if (discarding)
            {
                if (entry.Kind == QueueEntryKind.Move)
                {
                    actions.DeleteFile(entry.LeftFile!);
                }

                return null;
            }

            if (entry.Kind == QueueEntryKind.Move)
            {
                actions.MoveFile(entry.LeftFile!, entry.TargetFile!);
            }
            else
            {
                actions.DeleteFile(entry.LeftFile!);
            }

            return null;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    static SessionState AcceptTracked(SessionState state, QueueEntry entry, ViewerActions actions) =>
        ApplyTracked(state, entry, actions, discarding: false, $"Accepted {entry.Name}");

    static SessionState DiscardTracked(SessionState state, QueueEntry entry, ViewerActions actions) =>
        ApplyTracked(state, entry, actions, discarding: true, $"Discarded {entry.Name}");

    static SessionState ApplyTracked(
        SessionState state,
        QueueEntry entry,
        ViewerActions actions,
        bool discarding,
        string done)
    {
        if (TryApplyTracked(entry, actions, discarding) is { } failure)
        {
            var queue = state.Queue
                .Select(_ => _.Key == entry.Key ? _ with { Status = failure } : _)
                .ToList();
            return Clamp(state with
            {
                Queue = queue,
                Message = failure,
                Menu = null
            });
        }

        return Remove(state, state.Queue.Where(_ => _.Key != entry.Key).ToList(), done);
    }

    static SessionState DiscardInline(SessionState state)
    {
        var current = state.Current;
        if (current is null)
        {
            return state;
        }

        var pending = Pending(state);
        var discarded = pending.Discard(current.Key, out var message);
        return Remove(state, Rebuild(state, pending, discarded), message);
    }

    /// <summary>
    /// Copies left over right, which is the whole of accepting in file mode.
    /// <para>
    /// The paths are known non null: a file entry only comes from
    /// <see cref="QueueEntry.ForFiles"/>, whose parameters are not nullable, and only file mode
    /// reaches here.
    /// </para>
    /// </summary>
    static SessionState AcceptFile(SessionState state, ViewerActions actions)
    {
        var current = state.Current;
        if (current is null)
        {
            return state;
        }

        try
        {
            actions.CopyFile(current.LeftFile!, current.TargetFile!);
        }
        catch (Exception exception)
        {
            // Kept so it can be retried, for example while the target is locked.
            return state with
            {
                Queue = [current with { Status = exception.Message }],
                Message = exception.Message
            };
        }

        return Remove(state, [], $"Accepted {current.Name}");
    }

    static SessionState DiscardFile(SessionState state)
    {
        var current = state.Current;
        if (current is null)
        {
            return state;
        }

        return Remove(state, [], $"Discarded {current.Name}");
    }

    /// <summary>
    /// The queue as its owner holds it. The display list is the only copy the viewer keeps, so
    /// this is rebuilt from it rather than stored beside it, which is what stops the two from
    /// disagreeing. Tracked moves and deletes belong to the tray, not the queue, so only the
    /// inline entries round-trip.
    /// </summary>
    static InlineQueue Pending(SessionState state) =>
        InlineQueue.From(state.Queue
            .Where(_ => _.Kind == QueueEntryKind.Inline)
            .Select(_ => new PendingInline(_.Variants, _.Status)));

    /// <summary>
    /// The display list after an inline transition: the queue projected back, plus the tracked
    /// moves and deletes carried over untouched.
    /// <para>
    /// Every inline command rebuilds its half of the list from <see cref="InlineQueue"/>, which is
    /// what keeps the two from disagreeing. The tracked half has to survive that. An owning viewer
    /// holds both — a delete arrives with no tray running and sits beside the snapshots — so
    /// accepting a snapshot must not take the files pending next to it with it.
    /// </para>
    /// <para>
    /// <see cref="Sync"/> is the one caller that does not use this: it is replacing the tracked
    /// entries with what the owner just reported, so carrying the old ones over would double them.
    /// </para>
    /// </summary>
    static IReadOnlyList<QueueEntry> Rebuild(SessionState state, InlineQueue queue) =>
        Rebuild(state, null, queue);

    /// <summary>
    /// <see cref="RebuildWhole"/>, by the short way round where there is one.
    /// </summary>
    /// <param name="state">The state whose list is to be rebuilt.</param>
    /// <param name="before">
    /// The queue <paramref name="after"/> was made from, when that was <see cref="Pending"/> of
    /// this state. What the change did is then read off the two (<see cref="TryRebuildChanged"/>).
    /// </param>
    /// <param name="after">The queue as the change left it.</param>
    static IReadOnlyList<QueueEntry> Rebuild(SessionState state, InlineQueue? before, InlineQueue after)
    {
        if (before is not null &&
            TryRebuildChanged(state.Queue, before, after) is { } changed)
        {
            return changed;
        }

        return RebuildWhole(state, after);
    }

    /// <summary>
    /// The list rebuilt from the whole queue: every snapshot looked up among the entries there
    /// are, and the lot put in order. What every inline transition did, and what the short way
    /// has to come to.
    /// </summary>
    internal static IReadOnlyList<QueueEntry> RebuildWhole(SessionState state, InlineQueue queue) =>
        QueueProjection.Order([..Project(state, queue), ..Tracked(state)]);

    /// <summary>
    /// The list after a change to the queue that was about one entry or a few, made from the list
    /// there is rather than from the queue: the entries the change left alone are the ones in the
    /// list already, where they already are. Null when the change is not one this can do, and the
    /// whole list is rebuilt instead.
    /// <para>
    /// An arrival, a settle, a discard and a single accept each change one entry of however many
    /// there are, and each rebuilt the list from the whole queue: a dictionary of the entries, a
    /// comparison of every entry's patches with the queue's, and an ordering of the result, under
    /// the lock the render loop takes and on the thread a test process is waiting on. At 2,000
    /// entries that was two milliseconds and nearly two megabytes a change.
    /// </para>
    /// <para>
    /// What the change did is read off the two queues. <see cref="InlineQueue"/> hands back the
    /// items it did not touch as the items it was given, so an item that is another one is the
    /// change: replaced where it stands, taken out, or added at the end.
    /// </para>
    /// <para>
    /// Only for a list in the order a rebuild leaves, which puts snapshots ahead of files
    /// (<see cref="InRebuiltOrder"/>). A file that arrived since the last rebuild can stand ahead
    /// of a snapshot, and the rebuild that follows moves it: that one is left to the long way.
    /// </para>
    /// </summary>
    /// <param name="list">The display list <paramref name="before"/> was made from.</param>
    /// <param name="before"><see cref="Pending"/> of the state the list is from.</param>
    /// <param name="after">What the change made of <paramref name="before"/>.</param>
    static IReadOnlyList<QueueEntry>? TryRebuildChanged(IReadOnlyList<QueueEntry> list, InlineQueue before, InlineQueue after)
    {
        var grew = after.Count - before.Count;
        if (grew > 1 ||
            !InRebuiltOrder(list))
        {
            return null;
        }

        var result = new List<QueueEntry>(list.Count + 1);
        var item = 0;
        var kept = 0;
        var removed = false;
        foreach (var entry in list)
        {
            if (entry.Kind != QueueEntryKind.Inline)
            {
                result.Add(entry);
                continue;
            }

            var held = before.Items[item++];
            if (kept < after.Count &&
                ReferenceEquals(after.Items[kept], held))
            {
                kept++;
                result.Add(entry);
                continue;
            }

            // Another item where this one was, when the queue is no shorter: the same call site
            // with something else to say. In a queue that is shorter, an item that is not next is
            // one that went
            if (grew < 0)
            {
                removed = true;
                continue;
            }

            if (kept == after.Count)
            {
                return null;
            }

            var replacement = Projected(entry, after.Items[kept++]);
            // Where an entry stands is decided by its solution and its test, so one that has
            // neither changed stands where it stood
            if (replacement.Solution != entry.Solution ||
                replacement.TestGroup != entry.TestGroup)
            {
                return null;
            }

            result.Add(replacement);
        }

        if (grew == 1)
        {
            if (kept != before.Count ||
                !TryInsert(result, QueueEntry.ForInline(after.Items[kept++])))
            {
                return null;
            }
        }

        // Every item of the new queue accounted for, in the order it holds them
        if (kept != after.Count)
        {
            return null;
        }

        // Taking the last snapshot out of a solution leaves its files with no snapshot ahead of
        // them, and a rebuild puts such a solution after the ones that have
        if (removed &&
            !InRebuiltOrder(result))
        {
            return null;
        }

        return result;
    }

    /// <summary>
    /// The entry for a snapshot that is in the list already and has been replaced in the queue:
    /// what <see cref="Project"/> makes of it, for the one entry.
    /// </summary>
    static QueueEntry Projected(QueueEntry entry, PendingInline pending)
    {
        // Under another key it is another line's entry now, and no entry is held for that line
        if (entry.Key != pending.Key)
        {
            return QueueEntry.ForInline(pending);
        }

        if (VariantsMatch(entry.Variants, pending.Variants))
        {
            return entry.Status == pending.Status ? entry : entry with { Status = pending.Status };
        }

        return QueueEntry.ForInline(pending, SameVariant(entry, pending));
    }

    /// <summary>
    /// Puts a snapshot that is new to the queue where ordering the whole list would: after the
    /// last entry of its own test in its solution, or with no test of its own there, after the
    /// last snapshot of its solution and ahead of that solution's files. False when its solution
    /// has no snapshot yet, which is a solution taking a place among the others rather than an
    /// entry taking one in it.
    /// </summary>
    static bool TryInsert(List<QueueEntry> list, QueueEntry entry)
    {
        var lastOfSolution = -1;
        var lastOfTest = -1;
        for (var index = 0; index < list.Count; index++)
        {
            var other = list[index];
            if (other.Kind != QueueEntryKind.Inline ||
                other.Solution != entry.Solution)
            {
                continue;
            }

            lastOfSolution = index;
            if (entry.TestGroup is not null &&
                other.TestGroup == entry.TestGroup)
            {
                lastOfTest = index;
            }
        }

        if (lastOfSolution < 0)
        {
            return false;
        }

        list.Insert((lastOfTest < 0 ? lastOfSolution : lastOfTest) + 1, entry);
        return true;
    }

    /// <summary>
    /// Whether a list is in the order <see cref="RebuildWhole"/> leaves one in, beyond what
    /// <see cref="QueueProjection.Order"/> leaves any list in: within a solution every snapshot
    /// ahead of every file, and a solution with snapshots ahead of one with only files, the
    /// entries of no solution last of all. A rebuild orders the snapshots and then the files, so a
    /// list like this is one it would hand back as it is.
    /// </summary>
    static bool InRebuiltOrder(IReadOnlyList<QueueEntry> list)
    {
        var filesOnly = false;
        var index = 0;
        while (index < list.Count)
        {
            var solution = list[index].Solution;
            var inline = list[index].Kind == QueueEntryKind.Inline;
            // A solution with snapshots after one with none. The entries of no solution are last
            // whatever they are, so they are held to nothing here
            if (solution is not null)
            {
                if (inline &&
                    filesOnly)
                {
                    return false;
                }

                filesOnly |= !inline;
            }

            var files = false;
            for (; index < list.Count && list[index].Solution == solution; index++)
            {
                if (list[index].Kind != QueueEntryKind.Inline)
                {
                    files = true;
                }
                else if (files)
                {
                    return false;
                }
            }

            // Anything after the entries of no solution is a list no ordering left
            if (solution is null &&
                index < list.Count)
            {
                return false;
            }
        }

        return true;
    }

    static IEnumerable<QueueEntry> Tracked(SessionState state) =>
        state.Queue.Where(_ => _.Kind is QueueEntryKind.Move or QueueEntryKind.Delete);

    /// <summary>
    /// And back onto the display list. Building an entry runs the diff, so an entry already built
    /// for the same variants is reused, keeping its selected variant, and only its status carried
    /// across.
    /// <para>
    /// In the queue's order, not display order: both callers put the tracked files beside these
    /// and order the whole list, and ordering here as well was the same work twice for every
    /// change to the queue, under the lock the render loop takes.
    /// </para>
    /// <para>
    /// Compared by value rather than by reference, because an attached viewer parses fresh patch
    /// instances out of every refresh and would otherwise re-diff the whole queue five times a
    /// second.
    /// </para>
    /// </summary>
    static List<QueueEntry> Project(SessionState state, InlineQueue queue)
    {
        var existing = state.Queue
            .Where(_ => _.Kind == QueueEntryKind.Inline)
            .ToDictionary(_ => _.Key);
        var entries = new List<QueueEntry>(queue.Count);
        foreach (var pending in queue.Items)
        {
            if (existing.TryGetValue(pending.Key, out var entry))
            {
                if (VariantsMatch(entry.Variants, pending.Variants))
                {
                    entries.Add(entry.Status == pending.Status ? entry : entry with { Status = pending.Status });
                    continue;
                }

                // The variants changed, so the entry rebuilds, but what the reader had cycled to
                // survives where it still exists.
                entries.Add(QueueEntry.ForInline(pending, SameVariant(entry, pending)));
                continue;
            }

            entries.Add(QueueEntry.ForInline(pending));
        }

        return entries;
    }

    static bool VariantsMatch(IReadOnlyList<InlineVariant> left, IReadOnlyList<InlineVariant> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!left[index].Patch.Matches(right[index].Patch) ||
                !left[index].Origins.SequenceEqual(right[index].Origins))
            {
                return false;
            }
        }

        return true;
    }

    static bool SameEntries(IReadOnlyList<QueueEntry> left, IReadOnlyList<QueueEntry> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!ReferenceEquals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    static int IndexOf(IReadOnlyList<QueueEntry> queue, string key)
    {
        for (var index = 0; index < queue.Count; index++)
        {
            if (queue[index].Key == key)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Where the entry for <paramref name="current"/>'s call site is once the owner has taken it to
    /// another line, by the test <see cref="InlineQueue.Enqueue"/> recognised the move with. -1
    /// when none answers to it, or more than one does.
    /// </summary>
    static int IndexOfMoved(IReadOnlyList<QueueEntry> queue, QueueEntry? current)
    {
        if (current?.Patch is not { } patch)
        {
            return -1;
        }

        var found = -1;
        for (var index = 0; index < queue.Count; index++)
        {
            if (queue[index].Patch?.IsSameCallSite(patch) != true)
            {
                continue;
            }

            if (found >= 0)
            {
                return -1;
            }

            found = index;
        }

        return found;
    }

    /// <summary>
    /// Drops whatever is no longer in the queue and leaves the reader where they were.
    /// <para>
    /// Most removals are not of the entry on screen: a settle from a test that has started
    /// passing, "Accept all in &lt;solution&gt;" from a header, a sweep that skipped this one.
    /// Holding <see cref="SessionState.Selected" /> as an index across those quietly changed what
    /// was on screen to whatever the old index now landed on, at the top of it. So the selection
    /// follows its key, the way <see cref="Sync" /> and <see cref="EnqueueInline" /> do, and only
    /// an entry that is itself gone falls back to advancing by index.
    /// </para>
    /// </summary>
    static SessionState Remove(SessionState state, IReadOnlyList<QueueEntry> queue, string? message, Dictionary<string, string>? moved = null)
    {
        var key = state.Current?.Key;
        // An entry taken to another line (Rebased) is still the entry being read
        if (key is not null &&
            moved is not null)
        {
            key = moved.GetValueOrDefault(key, key);
        }

        var selected = key is null ? -1 : IndexOf(queue, key);
        var next = state with
        {
            Queue = queue,
            Selected = selected < 0 ? state.Selected : selected,
            Message = message,
            // Nothing left to manage, so the window has no reason to stay open.
            Exit = queue.Count == 0,
            Menu = null
        };

        if (selected < 0)
        {
            return Reopen(next);
        }

        return Clamp(next);
    }

    /// <summary>
    /// Where the variant the reader had cycled to is among the rebuilt entry's. Found by what it is
    /// the output of rather than by where it was: a framework settling, or a re-run agreeing with
    /// another variant, drops or merges the ones before it, and the index then named a different
    /// framework's content - silently, and Accept applied that one. The index stands only when no
    /// variant holds any framework the old one did.
    /// </summary>
    static int SameVariant(QueueEntry entry, PendingInline pending)
    {
        var origins = entry.Variants[entry.SelectedVariant].Origins;
        for (var index = 0; index < pending.Variants.Count; index++)
        {
            if (pending.Variants[index].Origins.Any(origins.Contains))
            {
                return index;
            }
        }

        return entry.SelectedVariant;
    }

    static SessionState Select(SessionState state, int index)
    {
        if (state.Queue.Count == 0 ||
            index < 0 ||
            index >= state.Queue.Count)
        {
            return state;
        }

        // Already the entry on screen. Selecting it is what a click on it does, what a right click
        // opening its menu does, and what a focus naming it does, and none of those asks to be
        // taken back to the top of what is being read.
        if (index == state.Selected)
        {
            return Clamp(state);
        }

        // An entry's menu acts on the entry selected when an item is clicked, so one left open over
        // a move - a focus from the tray or an IDE, a re-sent snapshot - discarded or accepted an
        // entry it was never opened on. Selecting is the only way the entry changes without the
        // queue changing, and a queue change already closes the menu.
        return Open(state with { Selected = index, Menu = null });
    }

    /// <summary>
    /// Puts the current entry on screen the way a reader first meets it: scrolled to its first
    /// change rather than to its first line. Every path that changes what is being read comes
    /// through here - selecting, stepping, a re-run that rewrote the text, a variant cycled to,
    /// the entry on screen going - so an entry is met the same way however it got there.
    /// </summary>
    static SessionState Open(SessionState state) =>
        // A document opens at its opening page too, which is the first that differs, and a
        // picture opens fitted: where the last one was enlarged says nothing about this one.
        ScrollToOpening(state with { Page = null, Zoom = 0, Pan = PanPoint.Centre });

    /// <summary>
    /// The picture on screen at another <see cref="PictureZoom"/> step, kept inside the steps there
    /// are. The identical state when there is no picture to enlarge or the step is the one it is
    /// already on, which is what zooming out of a fitted picture comes to.
    /// <para>
    /// About the point already at the middle, so going in and coming back out shows what was
    /// there before. Back at fitted the whole picture shows, and where it was dragged to means
    /// nothing, so that goes.
    /// </para>
    /// </summary>
    static SessionState ZoomTo(SessionState state, int step)
    {
        step = Math.Clamp(step, 0, PictureZoom.Last);
        if (!ScreenBuilder.ShowsPicture(state) ||
            step == state.Zoom)
        {
            return state;
        }

        return state with
        {
            Zoom = step,
            Pan = step == 0 ? PanPoint.Centre : state.Pan
        };
    }

    /// <summary>
    /// The reader dragged an enlarged picture: <paramref name="x"/> and <paramref name="y"/> are
    /// the point a head now has at the middle, as fractions of the picture, already kept inside
    /// what its pane can show. Both sides follow, since the model holds one point for the two.
    /// </summary>
    public static SessionState PanTo(SessionState state, double x, double y)
    {
        var pan = new PanPoint(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
        if (state.Zoom == 0 ||
            !ScreenBuilder.ShowsPicture(state) ||
            pan == state.Pan)
        {
            return state;
        }

        // Moving a picture about is the reader moving on, as a drag across text is
        return state with
        {
            Pan = pan,
            Menu = null
        };
    }

    /// <summary>
    /// The text scrolled to its first change, leaving the page where it is: for a document whose
    /// text arrived, or whose rows on screen switched between its text and its properties, the
    /// reader is still on the same page.
    /// </summary>
    static SessionState ScrollToOpening(SessionState state)
    {
        state = Clamp(state);
        if (state.View is not { } view)
        {
            return state;
        }

        return state with { ScrollTop = view.Opening(ScreenBuilder.PaneRows(state)) };
    }

    /// <summary>
    /// Folds or unfolds one group, then keeps the selection somewhere it can be seen.
    /// </summary>
    static SessionState Toggle(SessionState state, string key)
    {
        var collapsed = new HashSet<string>(state.Collapsed);
        if (!collapsed.Add(key))
        {
            collapsed.Remove(key);
        }

        var folded = state with { Collapsed = collapsed };
        if (NearestVisible(folded) is { } visible)
        {
            return Select(folded, visible);
        }

        return Clamp(folded);
    }

    /// <summary>
    /// Where the selection goes when it is under a fold, or null when it is not. The column follows
    /// the selection, so leaving it there would leave the whole list with nothing highlighted,
    /// while the panes and Accept went on acting on an entry nobody could see. Forward first,
    /// because folding a group is usually done on the way down the queue, and an entry that goes
    /// hands the selection to the one after it.
    /// <para>
    /// For a fold, and for the entry being read going: an index kept across that can name the
    /// first entry of a folded group that follows it.
    /// </para>
    /// </summary>
    static int? NearestVisible(SessionState state)
    {
        // Nothing folded, nothing hidden: every entry has a row, the selected one among them. The
        // answer the walk below would give, without the walk, which is every entry of the queue
        // and is asked after each entry a batch takes out.
        if (state.Collapsed.Count == 0)
        {
            return null;
        }

        var visible = QueueProjection.VisibleEntries(state);
        if (visible.Count == 0 ||
            visible.Contains(state.Selected))
        {
            return null;
        }

        var before = -1;
        var after = -1;
        foreach (var index in visible)
        {
            if (index < state.Selected)
            {
                before = index;
            }
            else if (after < 0)
            {
                after = index;
            }
        }

        if (after >= 0)
        {
            return after;
        }

        return before;
    }

    /// <summary>
    /// The entry being read has gone, and the selection is left on whatever now has its index:
    /// opened there, or at the nearest entry that can be seen when that one is under a fold.
    /// </summary>
    static SessionState Reopen(SessionState state)
    {
        state = Clamp(state);
        if (NearestVisible(state) is { } visible)
        {
            return Select(state, visible);
        }

        return Open(state);
    }

    /// <summary>
    /// Tab traversal, over the entries actually on screen. Stepping into a folded group would move
    /// the selection somewhere the user cannot see it, and stepping over it is what every list
    /// with folds does.
    /// <para>
    /// Display order is queue order — <see cref="QueueProjection.Order"/> guarantees it — so with
    /// nothing folded this walks exactly what it always did.
    /// </para>
    /// </summary>
    static SessionState Step(SessionState state, int delta)
    {
        var visible = QueueProjection.VisibleEntries(state);
        if (visible.Count == 0)
        {
            return state;
        }

        var at = visible.IndexOf(state.Selected);
        if (at < 0)
        {
            // Selected but folded away, which only a reveal-less path could have produced. Step to
            // something visible rather than nowhere.
            return Select(state, visible[0]);
        }

        var next = at + delta;
        // No wrap, which is what stepping past either end has always done.
        return next < 0 || next >= visible.Count ? state : Select(state, visible[next]);
    }

    /// <summary>
    /// Unfolds whatever hides an entry, and nothing else.
    /// <para>
    /// Each folded group is probed on its own rather than the entry's groups being deduced: that
    /// deduction would have to repeat the rules about when a header exists at all, and two copies
    /// of those would drift. A folded set is a handful of strings, so probing is cheap.
    /// </para>
    /// </summary>
    static SessionState Reveal(SessionState state, int index)
    {
        if (state.Collapsed.Count == 0 ||
            QueueProjection.VisibleEntries(state).Contains(index))
        {
            return state;
        }

        var kept = new HashSet<string>();
        foreach (var candidate in state.Collapsed)
        {
            var alone = state with { Collapsed = new HashSet<string> { candidate } };
            if (QueueProjection.VisibleEntries(alone).Contains(index))
            {
                kept.Add(candidate);
            }
        }

        return state with { Collapsed = kept };
    }

    static SessionState Scroll(SessionState state, int top) =>
        Clamp(state with { ScrollTop = top });

    /// <summary>
    /// Switches between every line and only the changes, keeping the reader's place rather than
    /// sending them back to the first change: the row they were reading stays where it was on
    /// screen while what is around it folds away or opens out. A selection is held in the entry's
    /// own rows, so it carries across untouched.
    /// </summary>
    static SessionState ToggleMinimal(SessionState state, int body)
    {
        var toggled = state with { Minimal = !state.Minimal };
        if (state.View is not { } from ||
            toggled.View is not { } to ||
            ReferenceEquals(from, to))
        {
            return Clamp(toggled);
        }

        return Clamp(toggled with { ScrollTop = to.Follow(from, state.ScrollTop, body) });
    }

    static SessionState Clamp(SessionState state)
    {
        var selected = state.Queue.Count == 0
            ? -1
            : Math.Clamp(state.Selected, 0, state.Queue.Count - 1);
        state = state with { Selected = selected };
        // The rows on screen, which in the minimal view are fewer than the entry has, and for a
        // document seen as its pages are the rows describing it.
        var total = state.View?.Count ?? 0;
        var maxScroll = Math.Max(0, total - ScreenBuilder.PaneRows(state));
        return state with
        {
            ScrollTop = Math.Clamp(state.ScrollTop, 0, maxScroll)
        };
    }

    /// <summary>
    /// Text, text with the page under it, and the page alone, in turn. Between the two that show
    /// the text the rows are the same, so the reader keeps their place; to or from the page alone
    /// they are different rows, so the text opens at its first change the way an entry does.
    /// </summary>
    static SessionState ToggleDrawing(SessionState state)
    {
        if (state.Current is not { IsDocument: true })
        {
            return state;
        }

        // For this kind of document, here and wherever one comes up next
        var toggled = state.Showing(
            state.Drawing switch
            {
                DrawingView.Both => DrawingView.Picture,
                DrawingView.Picture => DrawingView.Text,
                _ => DrawingView.Both
            });

        if (state.ShowsProperties != toggled.ShowsProperties)
        {
            return ScrollToOpening(toggled);
        }

        return Clamp(toggled);
    }

    /// <summary>
    /// The next or previous page, from wherever the reader is, the opening page included. Kept
    /// inside the pages drawn so far, so turning past the last of them waits on it rather than
    /// showing nothing.
    /// </summary>
    static SessionState Turn(SessionState state, int delta)
    {
        if (state.Current is not { IsDocument: true } current ||
            state.Drawing == DrawingView.Text)
        {
            return state;
        }

        var (left, right) = DocumentPages.Of(state, current);
        var count = DocumentPages.Count(left, right);
        if (count == 0)
        {
            return state;
        }

        var page = Math.Clamp(DocumentPages.Current(state) + delta, 0, count - 1);
        return state.Page == page ? state : state with { Page = page };
    }

    /// <summary>
    /// The next or previous page that differs, which is what a change is when only the pages are on
    /// screen.
    /// </summary>
    static SessionState TurnToChange(SessionState state, bool forward)
    {
        if (state.Current is not { IsDocument: true } current)
        {
            return state;
        }

        var (left, right) = DocumentPages.Of(state, current);
        var differing = DocumentPages.Differing(current, left, right);
        var page = DocumentPages.Current(state);
        var target = forward
            ? differing.FirstOrDefault(_ => _ > page, -1)
            : differing.LastOrDefault(_ => _ < page, -1);
        return target < 0 ? state : state with { Page = target };
    }

    /// <summary>
    /// What one document draws as so far, from <see cref="DocumentWatch"/>. Dropped when no entry
    /// still holds those bytes, so a render finishing for a file that went or was rewritten in the
    /// meantime leaves nothing behind.
    /// </summary>
    /// <param name="key">What the pages are kept under: <see cref="DocumentPages.Key"/>.</param>
    public static SessionState Rendered(SessionState state, string key, Rendering rendering)
    {
        if (!Holds(state.Queue, key))
        {
            return state;
        }

        var renders = new Dictionary<string, Rendering>(state.Renders)
        {
            [key] = rendering
        };
        return state with { Renders = renders };
    }

    /// <summary>
    /// Drops what documents no longer in the queue drew as. The identical state when there is
    /// nothing to drop, which is almost every pass.
    /// </summary>
    public static SessionState Forget(SessionState state)
    {
        if (state.Renders.Keys.All(_ => Holds(state.Queue, _)))
        {
            return state;
        }

        var renders = state.Renders
            .Where(_ => Holds(state.Queue, _.Key))
            .ToDictionary(_ => _.Key, _ => _.Value);
        return state with { Renders = renders };
    }

    /// <summary>
    /// Whether any entry still has the bytes a key's pages were drawn from. By the hash in the
    /// key, so a map's pages in every projection it has been drawn in stay for as long as the map
    /// does, and switching back to one costs nothing.
    /// </summary>
    static bool Holds(IReadOnlyList<QueueEntry> queue, string key)
    {
        var hash = DocumentPages.HashOf(key);
        return queue.Any(_ => _.LeftDocument?.Hash == hash || _.RightDocument?.Hash == hash);
    }

    /// <summary>
    /// The next projection, for the map on screen and every one after it. Not in the text view,
    /// where nothing is drawn for it to change. The pages already drawn in the others are kept, so
    /// going round to one again shows it at once.
    /// </summary>
    static SessionState NextProjection(SessionState state)
    {
        if (!ScreenBuilder.DrawsMap(state))
        {
            return state;
        }

        return state with { Projection = MapProjections.Next(state.Projection) };
    }

    /// <summary>
    /// A document's text has been read, so its entry is replaced by one built with it. By the entry
    /// that was read rather than by key, and skipped when that one has gone since: whatever replaced
    /// it is newer than this. An entry on screen opens its text at the first change, on the page
    /// the reader is already on.
    /// </summary>
    public static SessionState TextRead(SessionState state, QueueEntry seen, QueueEntry fresh)
    {
        var index = -1;
        for (var position = 0; position < state.Queue.Count; position++)
        {
            if (ReferenceEquals(state.Queue[position], seen))
            {
                index = position;
                break;
            }
        }

        if (index < 0)
        {
            return state;
        }

        // Same key, same place, so the open menu's members still index what they did.
        var next = state with { Queue = Replace(state.Queue, index, fresh) };
        if (index == state.Selected)
        {
            return ScrollToOpening(next);
        }

        return Clamp(next);
    }
}
