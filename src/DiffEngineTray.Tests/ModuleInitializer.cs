using VerifyTests;

public static class ModuleInitializer
{
    public static void Initialize()
    {
        VerifyWinForms.Enable();
        VerifyImageMagick.Initialize();
        VerifyImageMagick.RegisterComparers();
    }
}