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
        var key = InlineKey.For(patch.SourceFile, patch.LineHint);
        var current = state.Current;
        var queue = Rebuild(state, Pending(state).Enqueue(patch));
        // Grouping can reorder the list, so the selection follows its key rather than its index.
        var selected = current is null ? 0 : IndexOf(queue, current.Key);
        if (selected < 0)
        {
            selected = 0;
        }

        // Start the reader at the top again only when the text under them changed. Folding into an
        // entry further down the list is not it, and neither is a re-send of what is already
        // there: Fold reports an identical patch as unchanged and Project hands back the same
        // entry, so a continuous runner re-sending the same failing snapshot every few seconds
        // used to bounce the reader to the top on every run.
        var replaced = current is not null &&
                       current.Key == key &&
                       !ReferenceEquals(queue[selected], current);

        return Clamp(state with
        {
            Queue = queue,
            Selected = selected,
            ScrollTop = replaced ? 0 : state.ScrollTop,
            // The open menu indexes the queue it was opened over, which just changed.
            Menu = null
        });
    }

    /// <summary>
    /// The single entry a file comparison shows. Nothing arrives after it, because file mode runs
    /// without a socket.
    /// </summary>
    public static SessionState EnqueueFile(SessionState state, QueueEntry entry) =>
        Clamp(state with
        {
            Queue = [..state.Queue, entry],
            Selected = state.Selected < 0 ? 0 : state.Selected
        });

    /// <summary>
    /// Drops the item for a key, used when a previously failing test starts passing — or, with an
    /// origin, just that framework's variant of it.
    /// </summary>
    public static SessionState Settle(SessionState state, string key, string? origin = null, string? member = null)
    {
        // Nothing can settle a file comparison: settles arrive over the socket, and file mode runs
        // without one. Guarded rather than assumed, because Pending would dereference the null
        // patch a file entry carries.
        if (state.Mode != ViewerMode.Inline)
        {
            return state;
        }

        var pending = Pending(state);
        var settled = pending.Settle(key, origin, member);
        if (ReferenceEquals(settled, pending))
        {
            return state;
        }

        return Remove(state, Rebuild(state, settled), null);
    }

    /// <summary>
    /// Adds a pending move or delete, or replaces the entry for the same file: a re-run stages the
    /// same received file again, and a second entry for it would be a duplicate rather than news.
    /// <para>
    /// Takes a built entry rather than paths, because building one reads both files.
    /// <see cref="TrackedEntry"/> does that on the listener thread, which is the same seam
    /// <see cref="Sync"/> takes the tray's through.
    /// </para>
    /// </summary>
    public static SessionState EnqueueTracked(SessionState state, QueueEntry entry)
    {
        var replacedCurrent = state.Current?.Key == entry.Key;
        var kept = state.Queue.Where(_ => _.Key != entry.Key);
        var queue = QueueProjection.Order([..kept, entry]);
        var currentKey = state.Current?.Key;
        var selected = currentKey is null ? 0 : IndexOf(queue, currentKey);
        return Clamp(state with
        {
            Queue = queue,
            Selected = selected < 0 ? 0 : selected,
            ScrollTop = replacedCurrent ? 0 : state.ScrollTop,
            Menu = null
        });
    }

    /// <summary>
    /// Replaces the queue with what its owner reports, for a viewer that is displaying rather
    /// than owning: the inline entries plus the owner's tracked moves and deletes, already
    /// materialized by the poller. Selection follows the key, so something accepted elsewhere in
    /// the list does not silently change what is on screen, and the scroll position is only given
    /// up when the item being read has gone.
    /// </summary>
    public static SessionState Sync(
        SessionState state,
        InlineQueue pending,
        IReadOnlyList<QueueEntry> changes,
        string? message)
    {
        var entries = new List<QueueEntry>(Project(state, pending));
        entries.AddRange(changes);
        var queue = QueueProjection.Order(entries);
        var key = state.Current?.Key;
        var selected = key is null ? -1 : IndexOf(queue, key);
        return Clamp(state with
        {
            Queue = queue,
            Selected = selected < 0 ? state.Selected : selected,
            ScrollTop = selected < 0 ? 0 : state.ScrollTop,
            Message = message ?? state.Message,
            // Nothing left to show, and this window is not what is holding the queue.
            Exit = queue.Count == 0,
            // The open menu indexes the queue it was opened over, which was just replaced.
            Menu = null
        });
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
            Menu = new(fullRow, ContextMenu.ForEntry(selected.Queue[row.EntryIndex]), [row.EntryIndex])
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
        var body = ScreenBuilder.BodyRows(state);
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
                return Scroll(state, NextChange(Rows(state), state.ScrollTop));
            case CommandKind.PreviousChange:
                return Scroll(state, PreviousChange(Rows(state), state.ScrollTop));
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
        if (current.Conflicted &&
            current.Variants[current.SelectedVariant].Origins is { Count: > 0 } origins &&
            origins[0] is { } origin)
        {
            // The reviewer picked a side by cycling to it; accepting applies exactly what is on
            // screen and resolves the whole call site.
            accepted = pending.Accept(current.Key, origin, actions.ApplyInline, out message);
        }
        else
        {
            accepted = pending.Accept(current.Key, actions.ApplyInline, out message);
        }

        var queue = Rebuild(state, accepted);
        if (accepted.Count < pending.Count)
        {
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
    /// Accepts every member of the group a header's menu described, skipping conflicted entries
    /// the way accept-all does. By key rather than index, because each accept rebuilds the queue
    /// underneath the next.
    /// <para>
    /// A solution header spans tracked moves and deletes as well as snapshots, so the sweep does
    /// too. Skipping them would make "Accept all in ..." quietly mean "accept the snapshots in
    /// ...", which is the divergence the unqualified accept-all already avoids.
    /// </para>
    /// </summary>
    static SessionState AcceptGroup(SessionState state, MenuState menu, ViewerActions actions)
    {
        var all = Members(state, menu);
        var members = all
            .Where(_ => _.Kind == QueueEntryKind.Inline)
            .ToList();
        var queue = Pending(state);
        var accepted = 0;
        var notWritten = 0;
        var failed = 0;
        var conflicted = 0;
        string? failure = null;
        foreach (var member in members)
        {
            if (member.Conflicted)
            {
                conflicted++;
                continue;
            }

            var before = queue.Count;
            // The applier's own answer, kept as it goes past. A group accept goes one entry at a
            // time, so a stale one leaves the queue here the way a single accept does, and the
            // count of accepts would otherwise include a snapshot written nowhere - which is
            // exactly what the sweep below must not take as licence to delete anything.
            InlineApplyResult? applied = null;
            queue = queue.Accept(member.Key, patch => applied = actions.ApplyInline(patch), out var outcome);
            if (queue.Count < before)
            {
                if (applied?.Status == InlineApplyStatus.NotFound)
                {
                    notWritten++;
                    failure = outcome;
                    continue;
                }

                accepted++;
                continue;
            }

            if (outcome is not null)
            {
                failed++;
                failure = outcome;
            }
        }

        return SweepTracked(
            state,
            Rebuild(state, queue),
            // The wording accept-all uses, from where accept-all gets it, so a group sweep and a
            // full sweep cannot read differently
            InlineQueue.AcceptAllMessage(accepted, notWritten, failed, conflicted, failure),
            actions,
            discarding: false,
            TrackedKeysOf(all),
            // A group accept drops its stale entries, so the queue it hands on cannot be read for
            // them the way the full sweep's can
            refused: notWritten > 0);
    }

    static SessionState DiscardGroup(SessionState state, MenuState menu, ViewerActions actions)
    {
        var all = Members(state, menu);
        var keys = all
            .Where(_ => _.Kind == QueueEntryKind.Inline)
            .Select(_ => _.Key)
            .ToList();
        var queue = Pending(state);
        foreach (var key in keys)
        {
            queue = queue.Discard(key, out _);
        }

        return SweepTracked(
            state,
            Rebuild(state, queue),
            $"Discarded {keys.Count}",
            actions,
            discarding: true,
            TrackedKeysOf(all));
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
        return Clamp(state with
        {
            Queue = queue,
            ScrollTop = 0
        });
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
            return Clamp(state with
            {
                Queue = queue,
                ScrollTop = 0
            });
        }

        return state;
    }

    static SessionState AcceptAllInline(SessionState state, ViewerActions actions)
    {
        var accepted = Pending(state).AcceptAll(actions.ApplyInline, out var message);
        return SweepTracked(state, Rebuild(state, accepted), message, actions, discarding: false);
    }

    static SessionState DiscardAllInline(SessionState state, ViewerActions actions)
    {
        var discarded = Pending(state).DiscardAll(out var message);
        return SweepTracked(state, Rebuild(state, discarded), message, actions, discarding: true);
    }

    /// <summary>
    /// The tracked half of a bulk command, worded the way an owning tray words its own: the inline
    /// summary, then ", plus n files" with what stayed pending counted rather than hidden. Both
    /// sweeps say the same thing about the same files, whichever process is holding them.
    /// </summary>
    /// <param name="only">
    /// The keys to sweep, for a group header acting on its own members. Null sweeps every tracked
    /// entry, which is what the unqualified bulk commands mean.
    /// </param>
    static SessionState SweepTracked(
        SessionState state,
        IReadOnlyList<QueueEntry> queue,
        string message,
        ViewerActions actions,
        bool discarding,
        IReadOnlyCollection<string>? only = null,
        bool refused = false)
    {
        var remaining = new List<QueueEntry>(queue.Count);
        var swept = 0;
        var kept = 0;
        refused = refused || InlineRefused(queue, discarding);
        foreach (var entry in queue)
        {
            if (entry.Kind is not (QueueEntryKind.Move or QueueEntryKind.Delete) ||
                (only is not null && !only.Contains(entry.Key)))
            {
                remaining.Add(entry);
                continue;
            }

            if (refused &&
                entry.Kind == QueueEntryKind.Delete)
            {
                kept++;
                remaining.Add(entry with { Status = deleteHeld });
                continue;
            }

            if (TryApplyTracked(entry, actions, discarding) is not { } failure)
            {
                swept++;
                continue;
            }

            kept++;
            remaining.Add(entry with { Status = failure });
        }

        if (swept == 0 &&
            kept == 0)
        {
            return Remove(state, remaining, message);
        }

        var clause = kept == 0 ? $"{swept} files" : $"{swept} files ({kept} kept)";
        return Remove(state, remaining, $"{message}, plus {clause}");
    }

    const string deleteHeld = "Held: a snapshot in this batch could not be written inline, and this file may be the only copy of it left. Accept it on its own to delete it anyway.";

    /// <summary>
    /// Whether this sweep follows an inline accept that something refused.
    /// <para>
    /// A snapshot moving inline arrives as two unrelated entries: the patch that writes the literal
    /// into the source, and a delete of the verified file it replaces. The sweep ran the delete
    /// whether or not the patch landed, so a patch the applier would not take — a call site that
    /// cannot host a Snapshot call, a source that moved since the run — cost the snapshot both
    /// copies at once. The literal was never written and the file it was replacing was gone, which
    /// no re-run recovers: every later run reports the same new snapshot and deletes nothing,
    /// forever.
    /// </para>
    /// <para>
    /// Nothing ties a delete to the patch it belongs to, so the whole sweep of deletes waits on the
    /// whole batch of patches. Blunt, and deliberately so — the entries held are still queued,
    /// still shown, and still acceptable one at a time by anyone who knows the file is redundant.
    /// Moves are left alone: a received file promoted over a verified one is the snapshot arriving,
    /// not the last copy of it leaving.
    /// </para>
    /// <para>
    /// A status on an inline entry is the outcome of an attempt, so this reads failures only:
    /// conflicted entries that a bulk accept skips are handed back untouched and carry none.
    /// Discarding asks nothing of the patches, so it sweeps as it always did.
    /// </para>
    /// </summary>
    static bool InlineRefused(IReadOnlyList<QueueEntry> queue, bool discarding)
    {
        if (discarding)
        {
            return false;
        }

        foreach (var entry in queue)
        {
            if (entry is {Kind: QueueEntryKind.Inline, Status: not null})
            {
                return true;
            }
        }

        return false;
    }

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

        var discarded = Pending(state).Discard(current.Key, out var message);
        return Remove(state, Rebuild(state, discarded), message);
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
        QueueProjection.Order([..Project(state, queue), ..Tracked(state)]);

    static IEnumerable<QueueEntry> Tracked(SessionState state) =>
        state.Queue.Where(_ => _.Kind is QueueEntryKind.Move or QueueEntryKind.Delete);

    /// <summary>
    /// And back onto the display list, in display order. Building an entry runs the diff, so an
    /// entry already built for the same variants is reused, keeping its selected variant, and
    /// only its status carried across.
    /// <para>
    /// Compared by value rather than by reference, because an attached viewer parses fresh patch
    /// instances out of every refresh and would otherwise re-diff the whole queue five times a
    /// second.
    /// </para>
    /// </summary>
    static IReadOnlyList<QueueEntry> Project(SessionState state, InlineQueue queue)
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
                entries.Add(QueueEntry.ForInline(pending, entry.SelectedVariant));
                continue;
            }

            entries.Add(QueueEntry.ForInline(pending));
        }

        return QueueProjection.Order(entries);
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
    static SessionState Remove(SessionState state, IReadOnlyList<QueueEntry> queue, string? message)
    {
        var key = state.Current?.Key;
        var selected = key is null ? -1 : IndexOf(queue, key);
        return Clamp(state with
        {
            Queue = queue,
            Selected = selected < 0 ? state.Selected : selected,
            ScrollTop = selected < 0 ? 0 : state.ScrollTop,
            Message = message,
            // Nothing left to manage, so the window has no reason to stay open.
            Exit = queue.Count == 0,
            Menu = null
        });
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

        return Clamp(state with
        {
            Selected = index,
            ScrollTop = 0
        });
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
        var visible = QueueProjection.VisibleEntries(folded);
        if (visible.Count == 0 ||
            visible.Contains(folded.Selected))
        {
            return Clamp(folded);
        }

        // The selection went under the fold. The column follows the selection, so leaving it there
        // would leave the whole list with nothing highlighted. Forward first, because folding a
        // group is usually done on the way down the queue.
        var before = -1;
        var after = -1;
        foreach (var index in visible)
        {
            if (index < folded.Selected)
            {
                before = index;
            }
            else if (after < 0)
            {
                after = index;
            }
        }

        return Select(folded, after >= 0 ? after : before);
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

    static SessionState Clamp(SessionState state)
    {
        var selected = state.Queue.Count == 0
            ? -1
            : Math.Clamp(state.Selected, 0, state.Queue.Count - 1);
        var total = selected < 0 ? 0 : state.Queue[selected].TotalRows;
        var maxScroll = Math.Max(0, total - ScreenBuilder.BodyRows(state));
        return state with
        {
            Selected = selected,
            ScrollTop = Math.Clamp(state.ScrollTop, 0, maxScroll)
        };
    }

    static IReadOnlyList<Row> Rows(SessionState state) =>
        state.Current?.LeftRows ?? [];

    static bool IsChange(Row row) =>
        row.Kind != RowKind.Unchanged;

    static int NextChange(IReadOnlyList<Row> rows, int from)
    {
        var index = from;
        // Step off the block currently at the top of the viewport before looking for the next one.
        while (index < rows.Count &&
               IsChange(rows[index]))
        {
            index++;
        }

        while (index < rows.Count &&
               !IsChange(rows[index]))
        {
            index++;
        }

        return index < rows.Count ? index : from;
    }

    static int PreviousChange(IReadOnlyList<Row> rows, int from)
    {
        // The top row of the viewport, not the one above it. Stepping off from there took the
        // block ending immediately above the viewport for the block the viewport was in, and
        // skipped past it to the one before - or, with nothing before it, refused to move at all.
        var index = Math.Min(from, rows.Count - 1);

        // So step off only when the viewport really is sitting in a block, which is when its top
        // row is itself a change.
        while (index >= 0 &&
               IsChange(rows[index]))
        {
            index--;
        }

        while (index >= 0 &&
               !IsChange(rows[index]))
        {
            index--;
        }

        if (index < 0)
        {
            return from;
        }

        // Land on the first row of the block, not its last.
        while (index > 0 &&
               IsChange(rows[index - 1]))
        {
            index--;
        }

        return index;
    }
}
