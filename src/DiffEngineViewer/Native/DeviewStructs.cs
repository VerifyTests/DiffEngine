/// <summary>
/// Mirrors of the structs in native/include/deview.h. Every string is a byte offset and length
/// into one UTF-8 blob, so a frame costs one buffer rather than per string marshalling.
/// <para>
/// Field order and types must match the header exactly. DeviewStructTests guards the sizes.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
struct DeviewRow
{
    public int Kind;

    /// <summary>
    /// -1 when the row is filler and has no line number.
    /// </summary>
    public int LineNumber;

    public int TextOffset;
    public int TextLength;
}

[StructLayout(LayoutKind.Sequential)]
struct DeviewPane
{
    public int HeaderOffset;
    public int HeaderLength;
    public int RowOffset;
    public int RowCount;
    public int ScrollTop;
    public int TotalRows;
}

[StructLayout(LayoutKind.Sequential)]
struct DeviewButton
{
    public int LabelOffset;
    public int LabelLength;
    public int Flags;
}

[StructLayout(LayoutKind.Sequential)]
struct DeviewQueueItem
{
    public int LabelOffset;
    public int LabelLength;
    public int Flags;
}

[StructLayout(LayoutKind.Sequential)]
unsafe struct DeviewScreen
{
    public byte* Strings;
    public int StringsLength;
    public DeviewPane* Panes;
    public int PaneCount;
    public DeviewRow* Rows;
    public int RowCount;
    public DeviewButton* Buttons;
    public int ButtonCount;
    public DeviewQueueItem* Queue;
    public int QueueCount;
    public int TitleOffset;
    public int TitleLength;
    public int SubtitleOffset;
    public int SubtitleLength;
    public int StatusOffset;
    public int StatusLength;
}

[StructLayout(LayoutKind.Sequential)]
struct DeviewInput
{
    public int Key;
    public int ClickedButton;
    public int ClickedQueueItem;
    public int ScrollDelta;
    public int CloseRequested;
    public int Columns;
    public int Rows;
}

[Flags]
enum DeviewButtonFlags
{
    None = 0,
    Enabled = 1
}

[Flags]
enum DeviewQueueFlags
{
    None = 0,
    Selected = 1,
    Failed = 2,

    /// <summary>
    /// A group heading: drawn dimmed, flush left, and never hoverable or selectable. A shim built
    /// before the flag existed ignores it and draws a plain row, which still reads.
    /// </summary>
    Header = 4
}

/// <summary>
/// Keys the shim reports. Deliberately not the same numbering as <see cref="CommandKind"/>, which
/// carries entries the window has no key for, so the two are mapped rather than cast.
/// </summary>
enum DeviewKey
{
    None = 0,
    ScrollUp = 1,
    ScrollDown = 2,
    PageUp = 3,
    PageDown = 4,
    Home = 5,
    End = 6,
    NextChange = 7,
    PreviousChange = 8,
    NextItem = 9,
    PreviousItem = 10,
    Accept = 11,
    Discard = 12,
    AcceptAll = 13,
    Quit = 14,
    NextVariant = 15
}
