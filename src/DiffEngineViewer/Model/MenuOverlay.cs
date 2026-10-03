/// <summary>
/// The open context menu as a renderer sees it: labels anchored under a visible queue row. Part
/// of the <see cref="Screen"/> rather than a toolkit popup, so all three heads draw the same
/// menu, the ASCII renderer can snapshot it, and choosing an item flows through the same input
/// loop as every other click.
/// </summary>
/// <param name="Pane">
/// Set for a menu opened over a pane's text rather than on a queue row, with
/// <paramref name="Row"/> -1. A head hangs it where the pointer was when the right click that
/// asked for it landed, which only that head knows.
/// </param>
record MenuOverlay(int Row, IReadOnlyList<string> Labels, PaneSide? Pane = null);
