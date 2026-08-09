static class Program
{
    static int Main(string[] args)
    {
        // Registered before anything can trigger a load. Only the heads that P/Invoke need this,
        // so it lives with the renderer choice rather than in ViewerProgram.
        NativeResolver.Register();
        return ViewerProgram.Run(args, NativeViewerWindow.Open);
    }
}
