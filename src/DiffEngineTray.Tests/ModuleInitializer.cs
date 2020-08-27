using VerifyTests;

public static class ModuleInitializer
{
    public static void Initialize()
    {
        VerifyWinForms.Enable();
        VerifyPhash.RegisterComparer("png", .999f);
    }
}