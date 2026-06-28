namespace DiffEngineTray;

static class AvaloniaTrayServices
{
    public static void Wire()
    {
        TrayServices.OpenDirectory = ShellLauncher.OpenDirectory;
        TrayServices.RevealFile = ShellLauncher.RevealFile;
        TrayServices.Confirm = Dialogs.Confirm;
    }
}
