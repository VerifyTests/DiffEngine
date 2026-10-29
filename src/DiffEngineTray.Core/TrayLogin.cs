/// <summary>
/// How the tray is named in the desktop's autostart, which "Run at startup" writes.
/// </summary>
static class TrayLogin
{
    public static LoginItem Item { get; } = new(
        "DiffEngineTray",
        "Accepts and discards what test runs leave pending",
        "diffenginetray",
        "org.verifytests.diffenginetray",
        Command);

    /// <summary>
    /// This executable wherever it is, as the Windows head's Run key names it, rather than a
    /// guess at where a global install puts the tool, which a <c>--tool-path</c> install does not.
    /// </summary>
    static string Command() =>
        Environment.ProcessPath!;
}
