using VerifyTests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyWinForms.Enable();
        VerifyImageMagick.Initialize();
        VerifyImageMagick.RegisterComparers(.03);
    }
}