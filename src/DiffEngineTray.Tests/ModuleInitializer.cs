public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyWinForms.Initialize();
        VerifierSettings.UseSsimForPng();
    }
}