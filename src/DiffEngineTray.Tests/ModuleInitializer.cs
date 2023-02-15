public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyDiffPlex.Initialize();
        VerifyWinForms.Initialize();
        VerifyImageMagick.Initialize();
        VerifyImageMagick.RegisterComparers(.03);
    }
}