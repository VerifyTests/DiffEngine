public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifierSettings.UseSsimForPng();
        // Program.Main does this for the app. A test host never runs Main, so without it the
        // default probing rules would have to find the native under runtimes/{rid}/native, which
        // they only do for natives that arrived through a NuGet package.
        NativeResolver.Register();
        // Before anything can touch DiffTools, which resolves and caches on first use. Only points
        // the resolver at the local build; nothing here launches anything.
        ManualViewer.Register();
        RaiseThreadPoolFloor();
    }

    /// <summary>
    /// The socket tests need two pool threads per exchange, and a CI runner starts with about four
    /// in total.
    /// <para>
    /// <see cref="ServerFixture"/> drives the real <c>ViewerClient</c>, whose send is synchronous,
    /// so a test blocks a pool thread for the whole exchange while the server needs threads of its
    /// own to accept the connection and answer it. Run enough of those at once on a four core
    /// runner and the answer waits on the pool's hill climb, which adds roughly one thread a
    /// second, past the client's three second timeout. That surfaced as an intermittent "No
    /// response for Inline", on a different platform and a different test each run.
    /// </para>
    /// <para>
    /// Raising the floor removes the scarcity rather than the blocking. The blocking is real, but
    /// it belongs to the client the tray and an attached viewer actually use, and no process runs
    /// dozens of those at once — a test host is the only thing that does.
    /// </para>
    /// </summary>
    static void RaiseThreadPoolFloor()
    {
        ThreadPool.GetMinThreads(out var workers, out var completionPorts);
        ThreadPool.SetMinThreads(Math.Max(workers, 32), completionPorts);
    }
}
