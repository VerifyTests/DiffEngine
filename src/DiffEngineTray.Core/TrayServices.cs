/// <summary>
/// Platform seams that the shared core calls into. Each head (Windows Forms / Avalonia) wires its
/// own implementations at startup. Defaults are no-ops so the core can be used headless (e.g. tests).
/// </summary>
static class TrayServices
{
    // Show a yes/no prompt to the user. Return true to proceed (e.g. open a GitHub issue in the browser).
    public static Func<string, bool> Confirm = _ => false;

    // Open a directory in the platform file manager (Explorer / Finder / file manager).
    public static Action<string> OpenDirectory = _ => { };

    // Reveal (select) a file in the platform file manager.
    public static Action<string> RevealFile = _ => { };
}
