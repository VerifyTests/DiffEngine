using System.Runtime.Loader;

/// <summary>
/// The documents folder beside the viewer, when there is one: how PDFs and Office files become
/// text and pages, and SVGs pictures. The dotnet tool packages and the copy inside DiffEngineTray
/// carry it; the copy bundled in DiffEngine does not, because Skia, PDFium and the OpenXml SDK are
/// tens of MB per RID against the one to three the viewer itself is.
/// <para>
/// Found by the file being there, and loaded on first use rather than at startup: on the thread
/// that reads documents, never the one that has to bind the port within
/// <c>ViewerLaunchGate</c>'s five seconds. With no folder there is no instance at all, and every
/// document reads exactly as it did before there were any.
/// </para>
/// <para>
/// Handed explicitly to everything that reads a file, from <see cref="ViewerProgram"/>, rather than
/// found by each: a static would make every reader in a test process see whatever folder happened
/// to sit beside the test assembly.
/// </para>
/// </summary>
sealed class DocumentPlugin(Func<string, string> text, Func<string, string, Action<string>, int> render) :
    IDisposable
{
    const string assemblyName = "DiffEngineViewer.Documents";
    const string typeName = "DiffEngineViewer.Documents.DocumentRenderer";

    /// <summary>
    /// One load context per assembly per process. PDFium is serialized behind a lock that is a
    /// static of the assembly holding it, so a second context would carry a second lock, and two
    /// threads would be inside PDFium at once.
    /// </summary>
    static ConcurrentDictionary<string, Binding> bindings = new(StringComparer.OrdinalIgnoreCase);

    readonly ConcurrentDictionary<string, Extraction> extracted = new();
    readonly Lock cacheGate = new();
    RenderCache? cache;

    /// <summary>
    /// Beside the viewer, which is where a dotnet tool package carries it, or one directory up,
    /// which is where DiffEngineTray does: one folder for the copies it bundles for each RID, since
    /// the natives are already per RID inside it and the rest would be the same files twice.
    /// </summary>
    public static DocumentPlugin? Find() =>
        Find(Path.Combine(AppContext.BaseDirectory, "documents")) ??
        Find(Path.Combine(AppContext.BaseDirectory, "..", "documents"));

    public static DocumentPlugin? Find(string directory)
    {
        var assembly = Path.GetFullPath(Path.Combine(directory, $"{assemblyName}.dll"));
        if (!File.Exists(assembly))
        {
            return null;
        }

        var binding = bindings.GetOrAdd(assembly, _ => new(_));
        return new(binding.Text, binding.Render);
    }

    /// <summary>
    /// A PDF's text, or an Office document as Markdown. Throws when it cannot be read.
    /// </summary>
    public string Text(string path) =>
        text(path);

    /// <summary>
    /// Every page as a png in <paramref name="directory"/>, announced through
    /// <paramref name="landed"/> as each is complete. Throws when it cannot be drawn.
    /// </summary>
    public int Render(string path, string directory, Action<string> landed) =>
        render(path, directory, landed);

    /// <summary>
    /// Where text and pages are drawn to, made on first use so a viewer that never meets a document
    /// never makes one.
    /// </summary>
    public RenderCache Cache
    {
        get
        {
            lock (cacheGate)
            {
                return cache ??= RenderCache.Create();
            }
        }
    }

    /// <summary>
    /// What reading a document's text came to, by content hash: the text, or why there is none.
    /// Kept here rather than in the state so a reader on any thread - the listener building an
    /// entry for a pair, an owner link re-reading the tray's files - gets text already read
    /// without waiting on it, and a document that cannot be read is not tried again every pass.
    /// </summary>
    public bool TryGetText(string hash, out Extraction extraction) =>
        extracted.TryGetValue(hash, out extraction);

    public void Remember(string hash, Extraction extraction) =>
        extracted[hash] = extraction;

    /// <summary>
    /// Drops the text, the copies and the pages of documents no longer in the queue.
    /// </summary>
    public void Keep(IReadOnlySet<string> hashes)
    {
        foreach (var hash in extracted.Keys)
        {
            if (!hashes.Contains(hash))
            {
                extracted.TryRemove(hash, out _);
            }
        }

        RenderCache? existing;
        lock (cacheGate)
        {
            existing = cache;
        }

        if (existing is null)
        {
            return;
        }

        foreach (var hash in existing.Hashes().ToList())
        {
            if (!hashes.Contains(hash))
            {
                existing.Forget(hash);
            }
        }
    }

    public void Dispose()
    {
        lock (cacheGate)
        {
            cache?.Dispose();
            cache = null;
        }
    }

    /// <summary>
    /// The renderer's two methods, bound the first time either is called. A Lazy, so a load that
    /// fails is remembered and reported on every document rather than retried on each.
    /// </summary>
    sealed class Binding(string assembly)
    {
        readonly Lazy<(Func<string, string> Text, Func<string, string, Action<string>, int> Render)> methods = new(() => Bind(assembly));

        public string Text(string path) =>
            methods.Value.Text(path);

        public int Render(string path, string directory, Action<string> landed) =>
            methods.Value.Render(path, directory, landed);
    }

    /// <summary>
    /// By name, with BCL types only on the boundary, so nothing in the documents assembly is the
    /// same type in two load contexts and it needs no reference back to this one.
    /// </summary>
    static (Func<string, string> Text, Func<string, string, Action<string>, int> Render) Bind(string assembly)
    {
        var context = new DocumentLoadContext(assembly);
        var type = context
            .LoadFromAssemblyPath(assembly)
            .GetType(typeName, throwOnError: true)!;
        return (
            type.GetMethod("Text")!.CreateDelegate<Func<string, string>>(),
            type.GetMethod("Render")!.CreateDelegate<Func<string, string, Action<string>, int>>());
    }

    /// <summary>
    /// Resolves the documents assembly's dependencies from its own deps.json: managed ones from the
    /// folder, natives from runtimes/{rid}/native for the running RID. Anything it does not name -
    /// the framework - falls back to the default context.
    /// </summary>
    sealed class DocumentLoadContext(string assembly) :
        AssemblyLoadContext(assemblyName)
    {
        readonly AssemblyDependencyResolver resolver = new(assembly);

        protected override Assembly? Load(AssemblyName name)
        {
            if (resolver.ResolveAssemblyToPath(name) is { } path)
            {
                return LoadFromAssemblyPath(path);
            }

            return null;
        }

        protected override nint LoadUnmanagedDll(string name)
        {
            if (resolver.ResolveUnmanagedDllToPath(name) is { } path)
            {
                return LoadUnmanagedDllFromPath(path);
            }

            return nint.Zero;
        }
    }
}

/// <summary>
/// What reading one document's text came to.
/// </summary>
/// <param name="Text">The text, or null when it could not be read.</param>
/// <param name="Failure">Why it could not be read.</param>
readonly record struct Extraction(string? Text, string? Failure);
