public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyWinForms.Initialize();
        // Effectively "the same pixels", rather than Verify's 0.98 default. A viewer screen is
        // mostly flat background, so 0.98 is far looser than it sounds on one: dropping a whole row
        // of body text still scores about 0.998 and would pass. This head renders identically off
        // CI, so the remaining slack is for float dust and PNG encoder differences only.
        VerifierSettings.UseSsimForPng(0.9999);
        // Program.Main does this for the app, and a test host never runs Main. Without visual
        // styles the buttons render as the classic control, which would be a baseline that does
        // not describe what a user sees.
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }
}
