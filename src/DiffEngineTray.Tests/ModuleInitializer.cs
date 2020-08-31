using VerifyTests;

public static class ModuleInitializer
{
    public static void Initialize()
    {
        VerifierSettings.DisableNewLineEscaping();
        VerifyWinForms.Enable();
        VerifyImageMagick.Initialize();
        VerifyImageMagick.RegisterComparers();
    }
}