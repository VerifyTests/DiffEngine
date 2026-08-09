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
    }
}
