using VerifyTests;

public static class ModuleInitializer
{
    public static void Initialize()
    {
        VerifierSettings.DisableNewLineEscaping();
        VerifyWinForms.Enable();
        VerifyPhash.RegisterComparer("png", .999f);
    }
}