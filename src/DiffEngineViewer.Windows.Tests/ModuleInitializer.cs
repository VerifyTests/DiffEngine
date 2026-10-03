public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // First, since it is refused once any window exists. WinForms answers an exception thrown
        // inside a window message with a dialog offering Continue and Quit, which on a desktop
        // somebody is working at is a test run waiting for a click nobody knows to make. Thrown
        // instead, it comes out of the DoEvents that dispatched the message and fails the test.
        // For the application: the overload without threadScope sets it for the calling thread
        // alone, and no test runs on the thread a module initializer does.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, threadScope: false);
        MachineSettings.Ignore();
        VerifyWinForms.Initialize();
        // Effectively "the same pixels", rather than Verify's 0.98 default. A viewer screen is
        // mostly flat background, so 0.98 is far looser than it sounds on one: dropping a whole row
        // of body text still scores about 0.998 and would pass. This head renders identically off
        // CI, so the remaining slack is for float dust and PNG encoder differences only.
        VerifierSettings.UseSsimForPng(0.9999);
        // Program.Main does this for the app, and a test host never runs Main. Without it the
        // buttons render as the classic light control, which would be a baseline that does not
        // describe what a user sees. Unscaled, so the captures describe the app rather than the
        // display of whoever captured them.
        ViewerApp.ConfigureUnscaled();
    }
}
