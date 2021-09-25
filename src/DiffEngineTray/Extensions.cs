using System.Windows.Forms;

static class Extensions
{
    public static IntPtr Handle(this NotifyIcon target)
    {
        var windowField = typeof(NotifyIcon)
            .GetField("window", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var window = (NativeWindow) windowField.GetValue(target)!;
        return window.Handle;
    }

    public static void Add(this ToolStripSplitButton target, params ToolStripItem[] items)
    {
        target.DropDownItems.AddRange(items);
    }
}