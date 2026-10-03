using System.Globalization;

/// <summary>
/// Reads the text of the queue's documents and draws their pages, on its own thread, for the entry
/// on screen and no other.
/// <para>
/// Not the entry after it as well, though stepping to it would then find it ready. A call into
/// Morph or PDFium cannot be stopped once it has started, so a document drawn ahead was one the
/// reader then waited behind whenever they picked any other entry, and a core spent on something
/// nobody had opened. One that is stepped to is read and drawn then, with the spinner the heads draw
/// for <see cref="Pane.ImagePending"/> standing in for its page meanwhile.
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
    /// is reported as failed and left behind. Code in this process cannot be stopped, so this does
    /// not stop it: it only keeps the documents after it moving.
    /// <para>
    /// Not how long the whole document may take. A long one lands a page every so often for longer
    /// than any one limit, and counted from its start it was given up on part way through, with
    /// pages still arriving.
    /// </para>
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Whether the window is hidden, set by the render loop as it hides and shows it. Nothing is read
    /// or drawn for a window nobody can see, which a tray-hidden viewer is for whole test runs.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Moved on when a job is left behind, so pages it lands afterwards are dropped rather than
    /// published over whatever replaced it.
    /// </summary>
    int generation;

    /// <summary>
    /// Set while a PDF that was left behind is still inside PDFium, which is serialized behind one
    /// lock: a PDF started now would wait on that call with nothing to bound it. Written from the
    /// pool when that call returns.
    /// </summary>
    volatile bool pdfiumHeld;

    /// <summary>
    /// When something last came of the job in hand: when it started, then as each page landed.
    /// </summary>
    long progressed;

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
        catch (NotYetException exception)
        {
            Say(exception.Message);
        }
        catch (Exception exception)
        {
            Say($"Could not read the documents: {exception.Message}");
        }

        // Longer than an idle pass waits: what was in the way is usually still there a moment
        // later, and trying again reads and hashes the document before it finds that out.
        return Interval * 5;
    }

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
    /// One job: the first thing the entry on screen wants that is not there yet. True when there was
    /// one, so the loop goes again without waiting. Public for the tests, which drive it directly
    /// rather than waiting on a thread.
    /// <para>
    /// The state is read afresh for every job, so a reader who steps on part way through a document
    /// has the next one started as soon as the job in hand is done, rather than after the rest of the
    /// one they left.
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
        var failure = Run(side.Format, () => text = documents.Text(source));
        return failure is null ? new(text, null) : new(null, failure);
    }

    bool Draw(SessionState state, QueueEntry entry)
    {
        if (state.Drawing == DrawingView.Text)
        {
            return false;
        }

        foreach (var side in Sides(entry))
        {
            if (DocumentPages.Key(side, state.Projection) is { } key &&
                !state.Renders.ContainsKey(key))
            {
                Render(side, key, state.Projection);
                return true;
            }
        }

        return false;
    }

    /// <param name="key">
    /// What the pages are kept under, which for a map is its hash and the projection it is drawn
    /// in: the same bytes are drawn again for each one the reader switches to.
    /// </param>
    void Render(DocumentFile side, string key, MapProjection projection)
    {
        // Everything that can say "not now" comes before anything is recorded. A rendering marked
        // as started is never started again, so one that then could not be - its copy not written,
        // PDFium not free - was a spinner turning for as long as its entry stayed in the queue.
        AwaitPdfium(side);
        if (Copy(side) is not { } source)
        {
            host.Mutate(_ => ViewerSession.Rendered(_, key, new([], true, "the file changed while it was being drawn.")));
            return;
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
        var token = Volatile.Read(ref generation);
        var pages = new List<RenderedPage>();

        void Landed(string file)
        {
            if (token != Volatile.Read(ref generation))
            {
                return;
            }

            var page = Page(file);
            RenderedPage[] landed;
            lock (pages)
            {
                pages.Add(page);
                landed = pages.ToArray();
            }

            // Something came of it, so the wait for the next page starts over
            Volatile.Write(ref progressed, Stopwatch.GetTimestamp());
            host.Mutate(_ => ViewerSession.Rendered(_, key, new(landed, false)));
        }

        var failure = Run(side.Format, () => documents.Render(source, directory, Landed, projection));

        RenderedPage[] complete;
        lock (pages)
        {
            complete = pages.ToArray();
        }

        host.Mutate(_ => ViewerSession.Rendered(_, key, new(complete, true, failure)));
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
    /// A PDF waits while one that was left behind is still inside PDFium, here, where the reader
    /// can be told why, rather than on PDFium's lock with nothing to bound it. It is read once that
    /// call has returned.
    /// <para>
    /// Not a failure of this document, so nothing is recorded against it. It used to be one: every
    /// PDF after a slow one failed at once, for the life of the window, PDFium long since free.
    /// </para>
    /// </summary>
    void AwaitPdfium(DocumentFile side)
    {
        if (side.Format == DocumentFormat.Pdf &&
            pdfiumHeld)
        {
            throw new NotYetException($"{Path.GetFileName(side.Path)} is waiting for an earlier PDF that is still being read, since PDFium reads one at a time. Restart the viewer if that one never finishes.");
        }
    }

    /// <summary>
    /// One call into the documents assembly, left behind once <see cref="Timeout"/> has passed with
    /// nothing coming of it. Null when it finished, otherwise why it did not.
    /// </summary>
    string? Run(DocumentFormat format, Action job)
    {
        Volatile.Write(ref progressed, Stopwatch.GetTimestamp());

        // On the pool, whose threads are background ones, so a call left behind cannot keep the
        // process alive once the window has closed.
        var task = Task.Run(job);
        try
        {
            while (true)
            {
                // Counted from the last page to land. Reading text lands nothing, so for that it
                // is counted from the start.
                var remaining = Timeout - Stopwatch.GetElapsedTime(Volatile.Read(ref progressed));
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                if (task.Wait(remaining))
                {
                    return null;
                }
            }
        }
        catch (AggregateException exception)
        {
            return (exception.InnerException ?? exception).Message;
        }

        Interlocked.Increment(ref generation);
        if (format == DocumentFormat.Pdf)
        {
            // Set first, so a call that returns in between is one the continuation still clears
            pdfiumHeld = true;
            task.ContinueWith(Released, TaskScheduler.Default);
        }

        return $"gave up after {Timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} seconds.";
    }

    /// <summary>
    /// The call that was left behind has returned, so PDFium is free and PDFs can be read again.
    /// </summary>
    void Released(Task abandoned)
    {
        // Read, so a call that ended by throwing is not left as a fault nothing observed
        _ = abandoned.Exception;
        pdfiumHeld = false;
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
