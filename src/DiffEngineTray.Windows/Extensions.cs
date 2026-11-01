static class Extensions
{
    public static void Add(this ToolStripSplitButton target, params ToolStripItem[] items) =>
        target.DropDownItems.AddRange(items);
}
