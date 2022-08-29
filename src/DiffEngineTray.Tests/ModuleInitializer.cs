public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyDiffPlex.Initialize();
        VerifyWinForms.Enable();
        VerifyImageMagick.Initialize();
        VerifyImageMagick.RegisterComparers(.03);
    }
}