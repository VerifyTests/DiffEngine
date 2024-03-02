static class Extensions
{
    // cant use UnsafeAccessor since _window is a NotifyIconNativeWindow which is not public
    public static IntPtr Handle(this NotifyIcon target)
    {
        var windowField = typeof(NotifyIcon)
            .GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var window = (NativeWindow) windowField.GetValue(target)!;
        return window.Handle;
    }

    public static void Add(this ToolStripSplitButton target, params ToolStripItem[] items) =>
        target.DropDownItems.AddRange(items);
}