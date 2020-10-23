using VerifyTests;

public static class ModuleInitializer
{
    public static void Initialize()
    {
        VerifierSettings.DisableNewLineEscaping();
        VerifyImageMagick.Initialize();
        VerifyImageMagick.RegisterComparers();
    }
}