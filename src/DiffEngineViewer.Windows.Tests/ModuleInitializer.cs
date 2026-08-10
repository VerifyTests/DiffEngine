public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyWinForms.Initialize();
        VerifierSettings.UseSsimForPng();
        // Program.Main does this for the app, and a test host never runs Main. Without visual
        // styles the buttons render as the classic control, which would be a baseline that does
        // not describe what a user sees.
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }
}
