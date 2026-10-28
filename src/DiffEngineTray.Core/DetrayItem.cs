/// <summary>
/// DetrayItem in native/include/detray.h: three UTF-8 strings the caller allocates and frees around
/// the call, the flags, and the index of the item whose submenu this one is in, or -1.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
struct DetrayItem
{
    public IntPtr Id;
    public IntPtr Label;
    public IntPtr Icon;
    public int Flags;
    public int Parent;
}
