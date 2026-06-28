public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyAvalonia.Initialize();
        VerifierSettings.UseSsimForPng();
        // The informational version embeds the build commit, so scrub it for deterministic snapshots.
        VerifierSettings.AddScrubber(builder => builder.Replace(VersionReader.VersionString, "{version}"));
    }
}
