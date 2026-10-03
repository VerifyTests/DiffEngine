/// <summary>
/// What the viewer remembers from one run to the next: how the reader left the window, and how
/// they chose to look at things. A <c>key=value</c> line each, in a file beside the tray's
/// settings.
/// <para>
/// Handed to <see cref="ViewerProgram"/>'s loop rather than found by whatever wants one, as
/// <see cref="DocumentPlugin"/> is: a test process then remembers nothing unless a test gives it
/// somewhere to, and never reads how the person running the tests left their own viewer.
/// </para>
/// <para>
/// Best effort in both directions. A file that cannot be read is no preferences, and one that
/// cannot be written is a setting not remembered: neither is worth a window that will not open,
/// or one that will not close.
/// </para>
/// </summary>
sealed class ViewerPreferences
{
    readonly string? path;
    readonly Lock gate = new();
    Dictionary<string, string> values;

    /// <param name="path">The file to keep them in, or null to keep them for this process only.</param>
    public ViewerPreferences(string? path = null)
    {
        this.path = path;
        values = Read();
    }

    /// <summary>
    /// The file every viewer of this user shares: the tool, the copy inside the tray and the one
    /// bundled in DiffEngine are the same window to the person looking at it.
    /// </summary>
    public static ViewerPreferences ForUser()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        // Empty where there is no home directory to put one in, which a service account may not have
        if (root.Length == 0)
        {
            return new();
        }

        return new(Path.Combine(root, "DiffEngine", "viewer.settings"));
    }

    public string? Get(string key)
    {
        lock (gate)
        {
            return values.GetValueOrDefault(key);
        }
    }

    /// <summary>
    /// Null forgets the key. Nothing is written when the value is the one already held, which is
    /// every close of a window nobody moved.
    /// </summary>
    public void Set(string key, string? value)
    {
        lock (gate)
        {
            if (values.GetValueOrDefault(key) == value)
            {
                return;
            }

            // Read again first, so what another viewer wrote since this one started is kept: two
            // can be open at once, and each writes only the keys it changed.
            var merged = Read();
            foreach (var (held, heldValue) in values)
            {
                merged.TryAdd(held, heldValue);
            }

            if (value is null)
            {
                merged.Remove(key);
            }
            else
            {
                merged[key] = value;
            }

            values = merged;
            Write();
        }
    }

    /// <summary>
    /// How the window was left, or null when nothing has been remembered or what was is not a
    /// placement.
    /// </summary>
    public WindowPlacement? Window
    {
        get => WindowPlacement.Parse(Get("window"));
        set => Set("window", value?.ToString());
    }

    /// <summary>
    /// The projection maps were last drawn in. <see cref="MapProjection.Auto"/> is not written
    /// down: it is what there is with nothing remembered.
    /// </summary>
    public MapProjection Projection
    {
        get => Named<MapProjection>(Get("projection")) ?? MapProjection.Auto;
        set => Set("projection", value == MapProjection.Auto ? null : value.ToString());
    }

    /// <summary>
    /// How each kind of document was last shown, for the kinds the reader chose a view for.
    /// <see cref="DrawingView.Both"/> is not written down, for the reason
    /// <see cref="MapProjection.Auto"/> is not.
    /// </summary>
    public IReadOnlyDictionary<DocumentFormat, DrawingView> Drawings
    {
        get
        {
            var drawings = new Dictionary<DocumentFormat, DrawingView>();
            foreach (var format in Enum.GetValues<DocumentFormat>())
            {
                if (Named<DrawingView>(Get(DrawingKey(format))) is { } view and not DrawingView.Both)
                {
                    drawings[format] = view;
                }
            }

            return drawings;
        }
        set
        {
            foreach (var format in Enum.GetValues<DocumentFormat>())
            {
                var chosen = value.TryGetValue(format, out var view) && view != DrawingView.Both;
                Set(DrawingKey(format), chosen ? view.ToString() : null);
            }
        }
    }

    static string DrawingKey(DocumentFormat format) =>
        $"drawing.{format}";

    /// <summary>
    /// A state with what was remembered about how to look at things, for a window that is opening.
    /// </summary>
    public SessionState Apply(SessionState state)
    {
        lock (gate)
        {
            remembered = Drawings;
            return state with
            {
                Projection = Projection,
                Drawings = remembered
            };
        }
    }

    /// <summary>
    /// What a state says about how to look at things, kept for the next window. Asked after every
    /// frame that did something, which is cheap: nothing is written unless something changed.
    /// </summary>
    public void Remember(SessionState state)
    {
        lock (gate)
        {
            Projection = state.Projection;
            // By reference, which is how a state that changed nothing about them says so
            if (ReferenceEquals(state.Drawings, remembered))
            {
                return;
            }

            remembered = state.Drawings;
            Drawings = state.Drawings;
        }
    }

    /// <summary>
    /// The views last read from or written to here, so asking again with the same ones is free.
    /// </summary>
    IReadOnlyDictionary<DocumentFormat, DrawingView>? remembered;

    /// <summary>
    /// By name only. A number parses as any enum, and the file is one a person can edit.
    /// </summary>
    static T? Named<T>(string? value)
        where T : struct, Enum
    {
        if (value is not null &&
            Enum.TryParse<T>(value, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed) &&
            !int.TryParse(value, out _))
        {
            return parsed;
        }

        return null;
    }

    Dictionary<string, string> Read()
    {
        var read = new Dictionary<string, string>(StringComparer.Ordinal);
        if (path is null)
        {
            return read;
        }

        string[] lines;
        try
        {
            if (!File.Exists(path))
            {
                return read;
            }

            lines = File.ReadAllLines(path);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return read;
        }

        foreach (var line in lines)
        {
            var separator = line.IndexOf('=');
            if (separator > 0)
            {
                read[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        return read;
    }

    /// <summary>
    /// Beside the file and moved over it, so a viewer killed part way through leaves the last
    /// whole file rather than half of this one.
    /// </summary>
    void Write()
    {
        if (path is null)
        {
            return;
        }

        // Named for the process, so two viewers writing at once do not write the same one
        var temp = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(
                temp,
                values
                    .OrderBy(_ => _.Key, StringComparer.Ordinal)
                    .Select(_ => $"{_.Key}={_.Value}"));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup)
                when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
