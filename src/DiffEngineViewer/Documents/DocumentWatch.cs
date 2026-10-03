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
    /// How long one document may take to read or draw before it is reported as failed and left
    /// behind. Code in this process cannot be stopped, so this does not stop it: it only keeps the
    /// documents after it moving.
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
    /// Set once a PDF has been left behind. PDFium is serialized behind one lock, which the call left
    /// behind still holds, so every later PDF would wait on it: they fail at once instead.
    /// </summary>
    bool pdfiumHeld;

    public void Run(Cancel cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            bool worked;
            try
            {
                worked = !Hidden && Pump();
            }
            catch (Exception exception)
            {
                // As TrackedWatch: on a task nothing awaits, so a fault is said out loud rather than
                // leaving documents silently never read.
                host.Mutate(_ => _ with
                {
                    Message = $"Could not read the documents: {exception.Message}"
                });
                return;
            }

            // Straight on while there is work, back to the interval once there is none.
            if (!worked)
            {
                cancel.WaitHandle.WaitOne(Interval);
            }
        }
    }

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
            if (side.Hash is { } hash &&
                !state.Renders.ContainsKey(hash))
            {
                Render(side, hash);
                return true;
            }
        }

        return false;
    }

    void Render(DocumentFile side, string hash)
    {
        host.Mutate(_ => ViewerSession.Rendered(_, hash, Rendering.Started));
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

            host.Mutate(_ => ViewerSession.Rendered(_, hash, new(landed, false)));
        }

        string? failure;
        if (Copy(side) is not { } source)
        {
            failure = "the file changed while it was being drawn.";
        }
        else
        {
            var directory = Path.GetDirectoryName(source)!;
            failure = Run(side.Format, () => documents.Render(source, directory, Landed));
        }

        RenderedPage[] complete;
        lock (pages)
        {
            complete = pages.ToArray();
        }

        host.Mutate(_ => ViewerSession.Rendered(_, hash, new(complete, true, failure)));
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
            when (exception is IOException or UnauthorizedAccessException)
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
    /// One call into the documents assembly, bounded by <see cref="Timeout"/>. Null when it
    /// finished, otherwise why it did not.
    /// </summary>
    string? Run(DocumentFormat format, Action job)
    {
        if (format == DocumentFormat.Pdf &&
            pdfiumHeld)
        {
            return "an earlier PDF is still being read, and PDFium reads one at a time. Restart the viewer to read PDFs again.";
        }

        // On the pool, whose threads are background ones, so a call left behind cannot keep the
        // process alive once the window has closed.
        var task = Task.Run(job);
        try
        {
            if (task.Wait(Timeout))
            {
                return null;
            }
        }
        catch (AggregateException exception)
        {
            return (exception.InnerException ?? exception).Message;
        }

        Interlocked.Increment(ref generation);
        if (format == DocumentFormat.Pdf)
        {
            pdfiumHeld = true;
        }

        return $"gave up after {Timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} seconds.";
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
