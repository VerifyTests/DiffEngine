public class DesktopDialogTests
{
    /// <summary>
    /// What each program is handed, which is all there is to see of them without a desktop.
    /// </summary>
    [Test]
    public async Task Arguments() =>
        await Verify(
            string.Join(
                "\n\n",
                new[] {DesktopDialog.AppleScript, DesktopDialog.Zenity, DesktopDialog.Kdialog}
                    .Select(_ => $"""
                                  {_.Program}
                                    folder: {Quoted(_.FolderArguments)}
                                    confirm: {Quoted(_.ConfirmArguments("Files found: 2, in /home/a & <b>.\nDelete files?"))}
                                  """)));

    /// <summary>
    /// kdialog opens in the home folder, which is whoever runs the tests'.
    /// </summary>
    static string Quoted(IEnumerable<string> arguments) =>
        string.Join(
            " ",
            arguments.Select(_ => _ == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                ? "[{Home}]"
                : $"[{_.Replace("\n", "\\n")}]"));

    [Test]
    [Arguments("KDE", "kdialog")]
    [Arguments("ubuntu:GNOME", "zenity")]
    [Arguments("kde:plasma", "kdialog")]
    [Arguments(null, "zenity")]
    public async Task EachDesktopsOwnFirst(string? desktop, string expected) =>
        await Assert.That(DesktopDialog.ForLinux(desktop, _ => true)!.Program).IsEqualTo(expected);

    [Test]
    public async Task EitherStandsInForTheOther()
    {
        await Assert.That(DesktopDialog.ForLinux("GNOME", _ => _ == "kdialog")!.Program).IsEqualTo("kdialog");
        await Assert.That(DesktopDialog.ForLinux("KDE", _ => _ == "zenity")!.Program).IsEqualTo("zenity");
    }

    [Test]
    public async Task NoneWhereNeitherIsInstalled() =>
        await Assert.That(DesktopDialog.ForLinux("GNOME", _ => false)).IsNull();

    [Test]
    [Arguments("/home/a/project\n", "/home/a/project")]
    [Arguments("/Users/a/project/\n", "/Users/a/project")]
    [Arguments("/\n", "/")]
    [Arguments("", null)]
    [Arguments("\n", null)]
    public async Task TheFolderAsPrinted(string output, string? expected) =>
        await Assert.That(DesktopDialog.FolderFrom(output)).IsEqualTo(expected);
}
