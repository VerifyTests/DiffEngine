using System.Globalization;
using System.Runtime.ExceptionServices;

/// <summary>
/// Reads the text of the queue's documents and draws their pages, on its own thread, for the entry
/// on screen and no other.
/// <para>
/// Not the entry after it as well, though stepping to it would then find it ready. A call into
/// Morph or PDFium cannot be stopped part way through a conversion or a page, so a document drawn
/// ahead was one the reader then waited behind whenever they picked any other entry, and a core
/// spent on something nobody had opened. One that is stepped to is read and drawn then, with the
/// spinner the heads draw for <see cref="Pane.ImagePending"/> standing in for its page meanwhile.
/// </para>
/// <para>
/// Both sides of that entry are drawn at once, by a call each. Drawn one after the other, the right
/// pane was a spinner for as long as every page of the left took, and which pages differ was not
/// known until both were done. Two Office files take a core each. Two PDFs take turns at PDFium's
/// lock a page at a time, but it is held only while a page is rasterised: encoding the png is most
/// of what a page costs and is done outside it, so two PDFs also take little longer than one.
/// </para>
/// <para>
/// The other half of <see cref="FileSide"/>'s bargain. Reading a file has to stay cheap, because it
/// happens on the listener thread a test process is waiting on, so a document arrives with its
/// bytes hashed and nothing else, and this is where the slow part happens. Text replaces the entry,
/// as a re-run's would; pages go into <see cref="SessionState.Renders"/> one at a time, because an
/// entry is built once and a page landing should not rebuild it.
/// </para>
/// <para>
/// In the viewer's own process, so a native fault in PDFium or Skia ends the window, and with it a
/// queue that was only in this process's memory. Hangs are bounded by <see cref="Timeout"/>; faults
/// are not something a load context can contain.
/// </para>
/// </summary>
sealed class DocumentWatch(SessionHost host, DocumentPlugin documents)
{
    public static TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How long a document may go with nothing coming of it - its text, or its next page - before it
    /// is reported as failed and left behind. A call cannot be stopped part way through a page, so
    /// this does not stop it: it keeps the documents after it moving, and the call ends where its
    /// next page would have landed.
    /// <para>
    /// Not how long the whole document may take. A long one lands a page every so often for longer
    /// than any one limit, and counted from its start it was given up on part way through, with
    /// pages still arriving.
    /// </para>
    /// <para>
    /// Counted by each call for itself, since the two sides of an entry are drawn at once: one side
    /// landing pages says nothing about the other having stopped.
    /// </para>
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Whether the window is hidden, set by the render loop as it hides and shows it. Nothing is read
    /// or drawn for a window nobody can see, which a tray-hidden viewer is for whole test runs.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// How many PDFs that were left behind have not returned yet. PDFium is serialized behind one
    /// lock, which a call holds while it rasterises a page, so a PDF started while there is one
    /// could wait on that call with nothing to bound it. A count rather than a flag, because both
    /// sides of an entry can be left behind, and the first of them to return says nothing about
    /// the second. Taken down from the pool as each call returns.
    /// </summary>
    int pdfiumHeld;

    /// <summary>
    /// When a PDF that was left behind last returned, which is when PDFium was last given back.
    /// Written from the pool.
    /// </summary>
    long pdfiumReleased;

    /// <summary>
    /// What the last turn that could do nothing said, so that it is said once.
    /// </summary>
    string? said;

    public void Run(Cancel cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            // Straight on while there is work, back to waiting once there is none.
            if (Turn() is { } wait)
            {
                cancel.WaitHandle.WaitOne(wait);
            }
        }
    }

    /// <summary>
    /// One turn of the loop: a job, and how long to wait before the next, or null to go straight
    /// on. Never throws, whatever the job did. Public for the tests, as <see cref="Pump"/> is.
    /// <para>
    /// A turn that fails says why and is tried again. It used to end the loop, since this runs on a
    /// task nothing awaits and a fault there has to be said out loud - which it was, once, in a
    /// status line the next message replaced, and after it no document was read or drawn until the
    /// viewer was restarted. What fails a turn is mostly nothing to do with the document: a copy
    /// that could not be written because a scanner had the file or the disk was full, a cache
    /// directory something cleared away.
    /// </para>
    /// </summary>
    public TimeSpan? Turn()
    {
        try
        {
            var worked = !Hidden && Pump();
            Unsay();
            return worked ? null : Interval;
        }
        catch (Exception exception)
        {
            Say(Reason(exception));
        }

        // Longer than an idle pass waits: what was in the way is usually still there a moment
        // later, and trying again reads and hashes the document before it finds that out.
        return Interval * 5;
    }

    /// <summary>
    /// What is in the way of a job, as the status line says it. A job that has only to wait says
    /// why in its own words; anything else thrown is something that stopped it being read at all.
    /// </summary>
    static string Reason(Exception exception) =>
        exception is NotYetException
            ? exception.Message
            : $"Could not read the documents: {exception.Message}";

    /// <summary>
    /// Why a turn could do nothing, in the status line. Once for as long as it stays the reason: the
    /// turn comes round again every second, and a message set on each would take the status line
    /// from everything else that has something to say there.
    /// </summary>
    void Say(string message)
    {
        if (message == said)
        {
            return;
        }

        said = message;
        host.Mutate(_ => _ with { Message = message });
    }

    /// <summary>
    /// A turn got through, so whatever was in the way has gone and what was said about it goes
    /// too: left there, the status line went on giving a reason for a document that had since been
    /// read. Only when it is still what the status line says, since anything the reader has done
    /// in between has had its own to say.
    /// </summary>
    void Unsay()
    {
        if (said is not { } message)
        {
            return;
        }

        said = null;
        host.Mutate(_ => _.Message == message ? _ with { Message = null } : _);
    }

    /// <summary>
    /// A job that cannot be started yet, as opposed to one that failed: nothing is recorded against
    /// the document, and the next turn tries it again.
    /// </summary>
    sealed class NotYetException(string message) :
        Exception(message);

    /// <summary>
    /// One job: the first thing the entry on screen wants that is not there yet, which is its text
    /// and then its pages. True when there was one, so the loop goes again without waiting. Public
    /// for the tests, which drive it directly rather than waiting on a thread.
    /// <para>
    /// The state is read afresh for every job, so a reader who steps on part way through a document
    /// has the next one started as soon as the job in hand is done. Not before: the calls in hand
    /// cannot be stopped, and a reader stepping through the queue would leave two more drawing
    /// behind them at every entry, each taking its turn at PDFium ahead of the one on screen.
    /// </para>
    /// </summary>
    public bool Pump()
    {
        var state = host.State;
        Prune(state);
        return state.Current is { IsDocument: true } current &&
               (ReadText(state, current) ||
                Draw(state, current));
    }

    /// <summary>
    /// Both sides' text, then the entry rebuilt once with it, rather than once per side: rebuilt
    /// after one, the diff would be that side's text against nothing.
    /// </summary>
    bool ReadText(SessionState state, QueueEntry entry)
    {
        var reading = Sides(entry)
            .Where(_ => _.Reading)
            .ToList();
        if (reading.Count == 0 ||
            // Replacing an entry an accept-all has claimed would lose what it records about it.
            state.Progress is not null)
        {
            return false;
        }

        foreach (var side in reading)
        {
            if (!documents.TryGetText(side.Hash!, out _))
            {
                documents.Remember(side.Hash!, Extract(side));
            }
        }

        var fresh = Reread(entry);

        // A read that failed where the one it would replace did not - a file locked by a re-run
        // writing it - is not news worth showing over the entry. The next pass tries again.
        if (fresh.Warning is not null &&
            entry.Warning is null)
        {
            return false;
        }

        // Status is what the last accept of it said, which is about the entry rather than its text.
        host.Mutate(_ => ViewerSession.TextRead(_, entry, fresh with { Status = entry.Status }));
        return true;
    }

    Extraction Extract(DocumentFile side)
    {
        AwaitPdfium(side);
        if (Copy(side) is not { } source)
        {
            return new(null, "the file changed while it was being read.");
        }

        string? text = null;
        string? failure = null;
        // Reading text lands nothing, so its wait is counted from the start
        var call = new Call(side.Format, _ => failure = _);
        call.Start(() => text = documents.Text(source));
        Await([call]);
        return failure is null ? new(text, null) : new(null, failure);
    }

    /// <summary>
    /// The pages of both sides, a call each, waited on together. The same bytes on both sides are
    /// one drawing.
    /// <para>
    /// A side that cannot be started yet says nothing about the other, which is drawn all the same.
    /// Why it could not be is thrown once the other is done, as it would have been on its own, so
    /// the turn comes round again for it.
    /// </para>
    /// </summary>
    bool Draw(SessionState state, QueueEntry entry)
    {
        if (state.Drawing == DrawingView.Text)
        {
            return false;
        }

        var sides = new List<(DocumentFile Side, string Key)>();
        foreach (var side in Sides(entry))
        {
            if (DocumentPages.Key(side, state.Projection) is { } key &&
                !state.Renders.ContainsKey(key) &&
                sides.All(_ => _.Key != key))
            {
                sides.Add((side, key));
            }
        }

        if (sides.Count == 0)
        {
            return false;
        }

        ExceptionDispatchInfo? refused = null;

        Call? Start((DocumentFile Side, string Key) drawing)
        {
            try
            {
                return Render(drawing.Side, drawing.Key, state.Projection);
            }
            catch (Exception exception)
            {
                if (refused is null)
                {
                    refused = ExceptionDispatchInfo.Capture(exception);
                    // Said now rather than when the turn hears of it, which is after the other
                    // side's last page
                    Say(Reason(exception));
                }

                return null;
            }
        }

        if (sides is [var first, var second] &&
            first.Side.Format == DocumentFormat.Pdf &&
            second.Side.Format == DocumentFormat.Pdf)
        {
            // Two PDFs are started one behind the other, the second once the first has landed a
            // page. They share PDFium's lock, and when one of them stops inside it, which one is
            // told from whose pages stopped first: see LeaveBehind. Started together, neither
            // has a page to go by, and a second that stopped inside PDFium at once would leave
            // the first, waiting on the lock since it began, as the one that ran out of time.
            var lead = Start(first);
            Await(lead is null ? [] : [lead], () => Start(second));
        }
        else
        {
            Await([.. sides.Select(Start).OfType<Call>()]);
        }

        refused?.Throw();
        return true;
    }

    /// <summary>
    /// Starts drawing one side, and returns the call that is drawing it. Null when there was nothing
    /// to start: the file no longer holds the bytes the side describes, which is recorded as why it
    /// was not drawn.
    /// </summary>
    /// <param name="key">
    /// What the pages are kept under, which for a map is its hash and the projection it is drawn
    /// in: the same bytes are drawn again for each one the reader switches to.
    /// </param>
    Call? Render(DocumentFile side, string key, MapProjection projection)
    {
        // Everything that can say "not now" comes before anything is recorded. A rendering marked
        // as started is never started again, so one that then could not be - its copy not written,
        // PDFium not free - was a spinner turning for as long as its entry stayed in the queue.
        AwaitPdfium(side);
        if (Copy(side) is not { } source)
        {
            host.Mutate(_ => ViewerSession.Rendered(_, key, new([], true, "the file changed while it was being drawn.")));
            return null;
        }

        var directory = Path.GetDirectoryName(source)!;
        // A folder per projection the reader chose, under the document's own. A page is a path to
        // the heads, which keep what they decoded from one, so the same map drawn another way has
        // to be another file rather than the same one rewritten.
        if (key != side.Hash)
        {
            directory = Directory.CreateDirectory(Path.Combine(directory, projection.ToString())).FullName;
        }

        host.Mutate(_ => ViewerSession.Rendered(_, key, Rendering.Started));
        var pages = new List<RenderedPage>();
        var call = new Call(side.Format, Ended, Withdraw);
        call.Start(() => documents.Render(source, directory, Landed, projection));
        return call;

        void Landed(string file)
        {
            var landed = call.Land(() =>
            {
                pages.Add(Page(file));
                var soFar = pages.ToArray();
                host.Mutate(_ => ViewerSession.Rendered(_, key, new(soFar, false)));
            });
            if (!landed)
            {
                // Nothing more is wanted of a call that was left behind, and between two pages is
                // the one place it can be stopped. It used to run on to its last page, and a PDF
                // kept every other PDF waiting until it had drawn all of them.
                throw new OperationCanceledException($"{Path.GetFileName(side.Path)} was left behind, so no more of it is drawn.");
            }
        }

        // Both on the loop's thread, once the call has returned or been left behind. Nothing
        // lands after either, so the pages are no longer anyone else's to change.
        void Ended(string? failure)
        {
            var complete = pages.ToArray();
            host.Mutate(_ => ViewerSession.Rendered(_, key, new(complete, true, failure)));
        }

        void Withdraw() =>
            host.Mutate(_ => Withdrawn(_, key));
    }

    /// <summary>
    /// A drawing put back to not having been started. No rendering at all is what says a side is
    /// still to be drawn, to <see cref="Draw"/> and to the spinner alike, so the pages it had go
    /// with it and are drawn again from the first.
    /// </summary>
    static SessionState Withdrawn(SessionState state, string key)
    {
        if (!state.Renders.ContainsKey(key))
        {
            return state;
        }

        var renders = new Dictionary<string, Rendering>(state.Renders);
        renders.Remove(key);
        return state with { Renders = renders };
    }

    /// <summary>
    /// A landed page as the heads will draw it: its size from its own header, the way an image
    /// side's is, and its hash, which is how it is compared with the other side's.
    /// </summary>
    static RenderedPage Page(string file)
    {
        var bytes = File.ReadAllBytes(file);
        if (!ImageHeader.TryRead(bytes, out var header) ||
            !header.HasSize)
        {
            throw new InvalidDataException($"A page came out as something other than a png: {Path.GetFileName(file)}.");
        }

        return new(file, header.Width, header.Height, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// The viewer's own copy of a side, made once per content hash, and only when the file still
    /// holds the bytes the side describes: what is read and drawn is then exactly what the hash
    /// keys. Null when the file has changed or gone since, which the next read of it will show.
    /// <para>
    /// Throws when the copy could not be made for any other reason: the file held by something
    /// else for a moment, or the cache not written to because a scanner had the new file, the disk
    /// was full or the directory had been cleared away. None of those is about the document, so
    /// none is recorded against it. <see cref="Turn"/> says what was thrown and tries again.
    /// </para>
    /// </summary>
    string? Copy(DocumentFile side)
    {
        var directory = documents.Cache.For(side.Hash!);
        var source = Path.Combine(directory, $"source{Path.GetExtension(side.Path).ToLowerInvariant()}");
        if (File.Exists(source))
        {
            return source;
        }

        byte[] bytes;
        try
        {
            bytes = FileSide.ReadBytes(side.Path);
        }
        catch (Exception exception)
            when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        if (Convert.ToHexString(SHA256.HashData(bytes)) != side.Hash)
        {
            return null;
        }

        // Written aside and moved in, so a copy cut short is never taken for a whole one.
        var partial = $"{source}.partial";
        File.WriteAllBytes(partial, bytes);
        File.Move(partial, source, overwrite: true);
        return source;
    }

    /// <summary>
    /// A PDF waits while one that was left behind has not returned, here, where the reader can be
    /// told why, rather than on PDFium's lock with nothing to bound it. It is read once every such
    /// call has returned.
    /// <para>
    /// Not a failure of this document, so nothing is recorded against it. It used to be one: every
    /// PDF after a slow one failed at once, for the life of the window, PDFium long since free.
    /// </para>
    /// </summary>
    void AwaitPdfium(DocumentFile side)
    {
        if (side.Format == DocumentFormat.Pdf &&
            Volatile.Read(ref pdfiumHeld) > 0)
        {
            throw new NotYetException($"{Path.GetFileName(side.Path)} is waiting for an earlier PDF that is still being read, since PDFium reads one at a time. Restart the viewer if that one never finishes.");
        }
    }

    /// <summary>
    /// One call into the documents assembly, and what the loop knows of it from outside: when
    /// something last came of it, and whether it has been left behind. A call has these to itself,
    /// because the two sides of an entry are drawn at once: one side landing pages says nothing
    /// about the other having stopped, and giving up on one must not drop what the other goes on
    /// to land.
    /// </summary>
    /// <param name="ended">
    /// The call finished, with why not when it threw, or was given up on, with that as why. Called
    /// on the loop's thread.
    /// </param>
    /// <param name="withdraw">
    /// The call was left behind for no reason of its own, so what it had started is put back
    /// rather than failed. Null for a call with nothing to put back.
    /// </param>
    sealed class Call(DocumentFormat format, Action<string?> ended, Action? withdraw = null)
    {
        readonly Lock gate = new();
        readonly TaskCompletionSource landed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        long progressed;
        bool leftBehind;

        public DocumentFormat Format => format;

        public Action<string?> Ended => ended;

        public Action? Withdraw => withdraw;

        public Task Task { get; private set; } = Task.CompletedTask;

        /// <summary>
        /// Done once the call has landed its first page.
        /// </summary>
        public Task Landed => landed.Task;

        /// <summary>
        /// When something last came of it: when it started, then as each page landed.
        /// </summary>
        public long Progressed => Volatile.Read(ref progressed);

        public void Start(Action work)
        {
            Volatile.Write(ref progressed, Stopwatch.GetTimestamp());
            // On the pool, whose threads are background ones, so a call left behind cannot keep the
            // process alive once the window has closed.
            Task = Task.Run(work);
        }

        /// <summary>
        /// Something came of the call, which <paramref name="publish"/> makes known, and the wait
        /// for the next thing starts over. False, with nothing published, once the call has been
        /// left behind.
        /// <para>
        /// Under the lock <see cref="LeaveBehind"/> takes, so a page cannot reach the state after
        /// its call was reported as given up on. One that did would put the pane back to drawing
        /// a document nothing more is coming of.
        /// </para>
        /// </summary>
        public bool Land(Action publish)
        {
            lock (gate)
            {
                if (leftBehind)
                {
                    return false;
                }

                publish();
                Volatile.Write(ref progressed, Stopwatch.GetTimestamp());
            }

            landed.TrySetResult();
            return true;
        }

        public void LeaveBehind()
        {
            lock (gate)
            {
                leftBehind = true;
            }
        }
    }

    /// <summary>
    /// Waits on calls until each has returned or been left behind, and tells each how it ended as
    /// it does, so a side that is done is not held up by one that is not.
    /// </summary>
    /// <param name="follow">
    /// Starts one more call to wait on, once the first has landed a page, or has returned without
    /// one. Never, when the first is left behind before either: what it would start is a PDF, and
    /// PDFium is then not free for it. The next turn finds it still to be drawn, and says so.
    /// </param>
    void Await(IReadOnlyList<Call> calls, Func<Call?>? follow = null)
    {
        var running = calls.ToList();
        var lead = running.FirstOrDefault();
        while (true)
        {
            if (follow is not null &&
                (lead is null ||
                 lead.Landed.IsCompleted ||
                 lead.Task.IsCompleted))
            {
                if (follow() is { } followed)
                {
                    running.Add(followed);
                }

                follow = null;
            }

            if (running.Count == 0)
            {
                return;
            }

            if (running.Find(_ => _.Task.IsCompleted) is { } returned)
            {
                running.Remove(returned);
                returned.Ended(Thrown(returned.Task));
                continue;
            }

            // Asked before the clocks are read. A PDF that returns in between has noted when
            // before it is counted as gone, so a call is never found out of time against a PDFium
            // that was given back that instant.
            var pdfiumWasHeld = Volatile.Read(ref pdfiumHeld) > 0;
            var (call, remaining) = running
                .Select(_ => (Call: _, Remaining: Remaining(_)))
                .MinBy(_ => _.Remaining);
            if (remaining > TimeSpan.Zero)
            {
                // Until one of them returns, or the one nearest to running out of time would, or
                // the first lands the page another is waiting to be started behind. Any other
                // page landing does not end the wait, so how long is left is worked out afresh
                // after it.
                var wake = running
                    .Select(_ => _.Task)
                    .ToList();
                if (follow is not null)
                {
                    wake.Add(lead!.Landed);
                }

                Task.WaitAny([.. wake], remaining);
                continue;
            }

            running.Remove(call);
            LeaveBehind(call, pdfiumWasHeld);
            if (call == lead)
            {
                follow = null;
            }
        }
    }

    /// <summary>
    /// How long a call has left before it is left behind: <see cref="Timeout"/> from the last thing
    /// to come of it. For a PDF, from when PDFium was last given back if that is later, since until
    /// then it may have been waiting on the call that had it.
    /// </summary>
    TimeSpan Remaining(Call call)
    {
        var since = call.Progressed;
        if (call.Format == DocumentFormat.Pdf)
        {
            since = Math.Max(since, Volatile.Read(ref pdfiumReleased));
        }

        return Timeout - Stopwatch.GetElapsedTime(since);
    }

    /// <summary>
    /// Why a call that has returned did not finish, or null when it did.
    /// </summary>
    static string? Thrown(Task task)
    {
        try
        {
            task.Wait();
            return null;
        }
        catch (AggregateException exception)
        {
            return (exception.InnerException ?? exception).Message;
        }
    }

    /// <summary>
    /// Stops waiting on a call that has gone <see cref="Timeout"/> with nothing coming of it. It is
    /// reported as given up on, what it lands from here on is dropped, and it is stopped where its
    /// next page lands.
    /// <para>
    /// Except a PDF that ran out of time while another, left behind before it, had still not
    /// returned. The two sides of an entry take turns at PDFium's lock, so when one of them stops
    /// inside PDFium the other stops at the lock, and from here both have only stopped landing
    /// pages. The one inside is taken to be the one that ran out of time first: the other was
    /// still landing a page, or had only just been started, when the first took the lock, and
    /// came to it afterwards. So the second is not given up on. It is put back as though it had
    /// not been started, to be drawn again once PDFium is free, with nothing recorded against it,
    /// since what stopped it was not about it.
    /// </para>
    /// </summary>
    void LeaveBehind(Call call, bool pdfiumWasHeld)
    {
        call.LeaveBehind();
        var pdf = call.Format == DocumentFormat.Pdf;
        if (pdf)
        {
            // Counted first, so a call that returns in between is one the continuation still
            // takes off
            Interlocked.Increment(ref pdfiumHeld);
        }

        call.Task.ContinueWith(_ => Returned(call), TaskScheduler.Default);

        if (pdf &&
            pdfiumWasHeld &&
            call.Withdraw is { } withdraw)
        {
            withdraw();
            return;
        }

        call.Ended($"gave up after {Timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} seconds.");
    }

    /// <summary>
    /// A call that was left behind has returned. If it was a PDF, PDFium is free of it, and once
    /// every such call has returned PDFs can be read again.
    /// </summary>
    void Returned(Call call)
    {
        // Read, so a call that ended by throwing is not left as a fault nothing observed. One that
        // was stopped where its next page landed always ends that way.
        _ = call.Task.Exception;
        if (call.Format != DocumentFormat.Pdf)
        {
            return;
        }

        // Noted before the count comes down, so whoever finds PDFium free also finds since when
        Volatile.Write(ref pdfiumReleased, Stopwatch.GetTimestamp());
        Interlocked.Decrement(ref pdfiumHeld);
    }

    /// <summary>
    /// The entry built again from its files, now that its text has been read. By kind, with the same
    /// key, name and group, the way <see cref="TrackedWatch"/> rebuilds one a re-run rewrote.
    /// </summary>
    QueueEntry Reread(QueueEntry entry) =>
        entry.Kind switch
        {
            QueueEntryKind.File => QueueEntry.ForFiles(
                entry.LeftFile!,
                entry.TargetFile!,
                FileSide.Read(entry.LeftFile!, documents),
                FileSide.Read(entry.TargetFile!, documents)),
            QueueEntryKind.Move => QueueEntry.ForMove(
                entry.Key,
                entry.Name,
                entry.Solution,
                entry.LeftFile!,
                entry.TargetFile!,
                FileSide.Read(entry.LeftFile!, documents),
                FileSide.Read(entry.TargetFile!, documents)),
            QueueEntryKind.Delete => QueueEntry.ForDelete(
                entry.Key,
                entry.Name,
                entry.Solution,
                entry.LeftFile!,
                FileSide.Read(entry.LeftFile!, documents)),
            _ => entry
        };

    IReadOnlySet<string>? held;

    /// <summary>
    /// Forgets the text and pages of documents no longer in the queue, from the state and from
    /// disk. Only when what the queue holds has changed, so an idle pass does no IO.
    /// </summary>
    void Prune(SessionState state)
    {
        var hashes = state.Queue
            .SelectMany(Sides)
            .Select(_ => _.Hash)
            .OfType<string>()
            .ToHashSet();
        if (held is not null &&
            held.SetEquals(hashes))
        {
            return;
        }

        held = hashes;
        host.Mutate(ViewerSession.Forget);
        documents.Keep(hashes);
    }

    static IEnumerable<DocumentFile> Sides(QueueEntry entry)
    {
        if (entry.LeftDocument is { } left)
        {
            yield return left;
        }

        if (entry.RightDocument is { } right)
        {
            yield return right;
        }
    }
}
