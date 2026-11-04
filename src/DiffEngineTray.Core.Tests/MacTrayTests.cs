/// <summary>
/// The macOS tray's menu across the C ABI. A C struct cannot hold a list of itself, so the menu
/// goes over as one array with each item's parent by index, and the library makes it a tree again.
/// Both halves are PlatformHeads', which tests the shape on its own; this is the ABI in between.
/// </summary>
public class MacTrayTests
{
    /// <summary>
    /// The library's own reading of what it was handed, every field of every item, against the
    /// menu it was handed. Needs no status bar, so it runs on the macOS CI job.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.MacOs)]
    public async Task TheLibraryRebuildsTheMenuItWasHanded()
    {
        Detray.Resolve();
        await Assert.That(Detray.Version()).IsEqualTo(Detray.ExpectedVersion);

        MacTray.Send(TrayIcon.Active, "DiffEngineTray", Menu);

        await Assert.That(MacTray.Describe()).IsEqualTo(Describe(Menu, 0));
    }

    /// <summary>
    /// What <c>detray_describe</c> writes for a menu, written here from the managed items.
    /// </summary>
    static string Describe(IReadOnlyList<TrayMenuItem> items, int depth)
    {
        var builder = new StringBuilder();
        foreach (var item in items)
        {
            builder.Append(' ', depth * 2);
            if (item.Separator)
            {
                builder.Append("----\n");
                continue;
            }

            builder.Append($"{item.Label}  [id: {item.Id}]");
            if (item.IconName is { Length: > 0 } icon)
            {
                builder.Append($"  [glyph: {icon}]");
            }

            if (!item.Enabled)
            {
                builder.Append("  [disabled]");
            }

            if (item.Checked is { } on)
            {
                builder.Append(on ? "  [checked]" : "  [unchecked]");
            }

            builder.Append('\n');
            builder.Append(Describe(item.Children ?? [], depth + 1));
        }

        return builder.ToString();
    }

    /// <summary>
    /// A menu with each thing the ABI carries: a glyph, a toggle each way, a separator, a disabled
    /// heading, a submenu in a submenu, and text beyond ASCII.
    /// </summary>
    static readonly IReadOnlyList<TrayMenuItem> Menu =
    [
        new("exit", "Exit", IconName: "exit"),
        new("startup", "Run at startup", Checked: true),
        new("separator:1", "", Separator: true),
        new("group:Solution", "Solution", Enabled: false, IconName: "solution"),
        new(
            "move:a",
            "Sample.Test (txt)",
            Children:
            [
                new("move:a:accept", "Accept move"),
                new(
                    "move:a:more",
                    "More",
                    Children:
                    [
                        new("move:a:more:deeper", "Ünïcödé, 日本語", Checked: false)
                    ])
            ]),
        new(
            "move:b",
            "Other (png)",
            Children:
            [
                new("move:b:accept", "Accept move", IconName: "accept")
            ])
    ];
}
