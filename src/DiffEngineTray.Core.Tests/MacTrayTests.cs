/// <summary>
/// The macOS tray's committed library. Its ABI and its binding are PlatformHeads', which tests the
/// two against a library of its own; this is the one the Mac head ships, which has the ABI only
/// because SwiftPM links PlatformHeadsMacTray into it.
/// </summary>
public class MacTrayTests
{
    /// <summary>
    /// The library exports the tray's ABI at the version PlatformHeads.Mac was built for, and hands
    /// a menu back as it was handed it. Needs no status bar, so it runs on the macOS CI job.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.MacOs)]
    public async Task TheLibraryRebuildsTheMenuItWasHanded()
    {
        StatusItemLibrary.Use("diffengine_tray");
        await Assert.That(StatusItemLibrary.Version()).IsEqualTo(StatusItemLibrary.ExpectedVersion);

        await Assert.That(StatusItemLibrary.Describe(Menu)).IsEqualTo(
            """
            Exit  [id: exit]  [glyph: exit]
            Run at startup  [id: startup]  [checked]
            ----
            Sample.Test (txt)  [id: move:a]
              Accept move  [id: move:a:accept]  [glyph: accept]

            """);
    }

    static readonly IReadOnlyList<TrayMenuItem> Menu =
    [
        new("exit", "Exit", IconName: "exit"),
        new("startup", "Run at startup", Checked: true),
        new("separator:1", "", Separator: true),
        new(
            "move:a",
            "Sample.Test (txt)",
            Children:
            [
                new("move:a:accept", "Accept move", IconName: "accept")
            ])
    ];
}
