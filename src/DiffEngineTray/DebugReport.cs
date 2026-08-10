/// <summary>
/// Everything the tray is holding, as text.
/// <para>
/// The menu shows each pending item reduced to what fits on one line: a name and a few actions.
/// This is the rest of it — every field of every tracked item, plus which process owns the inline
/// queue — for when the interesting part is a path, an argument list or a process that the menu
/// deliberately hides.
/// </para>
/// <para>
/// A string rather than a control, so all of it can be pasted into an issue, and so what it says
/// can be asserted on without rendering a window.
/// </para>
/// </summary>
static class DebugReport
{
    public static string Build(Tracker tracker, DateTime now)
    {
        // Read the three lists first, and once. Snapshots is a live round trip to the queue owner
        // when a viewer holds it, so reading it twice could describe two different queues, and
        // TrackingAny reports on whatever that read left cached.
        var deletes = tracker
            .Deletes
            .OrderBy(_ => _.File)
            .ToList();

        var moves = tracker
            .Moves
            .OrderBy(_ => _.Temp)
            .ToList();

        var snapshots = tracker
            .Snapshots
            .OrderBy(_ => _.Name)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine($"DiffEngineTray {VersionReader.VersionString}");
        builder.AppendLine($"Captured: {now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Inline queue: {tracker.InlineDescription}");
        // The value that drives the icon. Redundant against the counts below, and that is the
        // point: an icon that disagrees with what is pending shows up here as the two disagreeing.
        builder.AppendLine($"Tracking: {tracker.TrackingAny}");

        AppendHeading(builder, "Deletes", deletes.Count);
        for (var index = 0; index < deletes.Count; index++)
        {
            var delete = deletes[index];
            AppendEntry(builder, index, delete.Name);
            AppendField(builder, "File", WithExistence(delete.File));
            AppendField(builder, "Group", delete.Group);
        }

        AppendHeading(builder, "Moves", moves.Count);
        for (var index = 0; index < moves.Count; index++)
        {
            var move = moves[index];
            AppendEntry(builder, index, move.Name);
            AppendField(builder, "Temp", WithExistence(move.Temp));
            AppendField(builder, "Target", WithExistence(move.Target));
            AppendField(builder, "Extension", move.Extension);
            AppendField(builder, "Group", move.Group);
            AppendField(builder, "Exe", move.Exe);
            AppendField(builder, "Arguments", move.Arguments);
            AppendField(builder, "CanKill", move.CanKill);
            AppendField(builder, "KillLockingProcess", move.KillLockingProcess);
            AppendField(builder, "Process", Describe(move.Process));
        }

        var queued = tracker.QueuedPatches;
        AppendHeading(builder, "Snapshots", snapshots.Count);
        for (var index = 0; index < snapshots.Count; index++)
        {
            var snapshot = snapshots[index];
            AppendEntry(builder, index, snapshot.Name);
            AppendField(builder, "Key", snapshot.Key);
            AppendField(builder, "Source", WithExistence(snapshot.Source));
            AppendField(builder, "Group", snapshot.Group);
            AppendField(builder, "Status", snapshot.Status);
            AppendPatch(builder, queued, snapshot.Key);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The patch behind a queued snapshot, which is everything the reviewer sees derived back to
    /// what will be written. Only when this tray owns the queue: a viewer that owns one holds the
    /// patch itself, and shows it.
    /// </summary>
    static void AppendPatch(StringBuilder builder, IReadOnlyList<PendingInline>? queued, string key)
    {
        if (queued == null)
        {
            AppendField(builder, "Patch", "held by the process that owns the queue");
            return;
        }

        var patch = queued
            .FirstOrDefault(_ => _.Key == key)
            ?.Patch;
        if (patch == null)
        {
            // The listing and the queue were read separately, so a settle in between leaves an
            // item with nothing behind it. Rare, and worth seeing rather than hiding.
            AppendField(builder, "Patch", "gone since the listing was read");
            return;
        }

        // The path the edit will be made to, in its real casing. The key above is case folded, so
        // the two differ on Windows and only this one is openable.
        AppendField(builder, "SourceFile", patch.SourceFile);
        AppendField(builder, "LineHint", patch.LineHint);
        AppendField(builder, "Mode", patch.Mode);
        AppendField(builder, "OriginalExpression", patch.OriginalExpression);
        AppendField(builder, "NewContent", patch.NewContent);
    }

    static void AppendHeading(StringBuilder builder, string name, int count)
    {
        var heading = $"{name} ({count})";
        builder.AppendLine();
        builder.AppendLine(heading);
        builder.AppendLine(new string('-', heading.Length));
        if (count == 0)
        {
            builder.AppendLine("none");
        }
    }

    static void AppendEntry(StringBuilder builder, int index, string name)
    {
        if (index != 0)
        {
            builder.AppendLine();
        }

        builder.AppendLine($"[{index + 1}] {name}");
    }

    /// <summary>
    /// One field per line, padded to the longest label so the values line up in a column and a
    /// wrong looking one is findable by eye. A value that spans lines, which the two patch texts
    /// do, goes into an indented block instead, since the column would hold only its first line.
    /// </summary>
    static void AppendField(StringBuilder builder, string name, object? value)
    {
        var text = value?.ToString();
        if (text == null)
        {
            AppendColumn(builder, name, "<null>");
            return;
        }

        if (text.Length == 0)
        {
            AppendColumn(builder, name, "<empty>");
            return;
        }

        // Patch content carries \n whatever wrote it, so split on that and drop any \r left over
        var lines = text.Split('\n');
        if (lines.Length == 1)
        {
            AppendColumn(builder, name, text);
            return;
        }

        builder.AppendLine($"    {name}:");
        foreach (var line in lines)
        {
            builder.AppendLine($"        {line.TrimEnd('\r')}");
        }
    }

    static void AppendColumn(StringBuilder builder, string name, string value) =>
        builder.AppendLine($"    {name + ":",-20}{value}");

    /// <summary>
    /// Whether the file is there right now. Not state the tray holds, but it is what the scan acts
    /// on: a move whose temp file has gone is about to be dropped, and a delete whose file has
    /// gone already has been.
    /// </summary>
    static string WithExistence(string file) =>
        File.Exists(file) ? $"{file} (exists)" : $"{file} (missing)";

    static string Describe(Process? process)
    {
        if (process == null)
        {
            return "none";
        }

        try
        {
            var state = process.HasExited ? "exited" : "running";
            return $"{process.Id} ({state})";
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Id and HasExited both throw for a disposed or inaccessible process. A debug view
            // that dies while describing one is worse than one that admits it cannot.
            return "unavailable";
        }
    }
}
