/// <summary>
/// The open context menu as a renderer sees it: labels anchored under a visible queue row. Part
/// of the <see cref="Screen"/> rather than a toolkit popup, so all three heads draw the same
/// menu, the ASCII renderer can snapshot it, and choosing an item flows through the same input
/// loop as every other click.
/// </summary>
record MenuOverlay(int Row, IReadOnlyList<string> Labels);
