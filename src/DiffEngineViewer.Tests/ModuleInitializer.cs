public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // Tighter than Verify's 0.98 default, which cannot see a real defect on these screens: they
        // are mostly flat background, so dropping a whole row of body text still scores about
        // 0.998. Looser than the WinForms suites' 0.9999, because these baselines come from CI
        // rasterisers rather than from this machine, and macOS glyph drift is expected to surface
        // as a diff to re-accept rather than as a run that cannot be reproduced locally at all.
        VerifierSettings.UseSsimForPng(0.999);
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
