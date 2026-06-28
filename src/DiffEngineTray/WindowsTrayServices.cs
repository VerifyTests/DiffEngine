static class WindowsTrayServices
{
    public static void Wire()
    {
        TrayServices.OpenDirectory = ExplorerLauncher.OpenDirectory;
        TrayServices.RevealFile = ExplorerLauncher.ShowFileInExplorer;
        TrayServices.Confirm = Confirm;
    }

    static bool Confirm(string text)
    {
        var result = MessageBox.Show(
            text,
            IssueLauncher.ErrorCaption,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Error);
        return result == DialogResult.Yes;
    }
}
