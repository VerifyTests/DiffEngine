/// <summary>
/// What the user did during the last frame, already translated out of native terms.
/// </summary>
readonly record struct ViewerInput(
    CommandKind Key,
    int ClickedButton,
    int ClickedQueueItem,
    int ScrollDelta,
    bool CloseRequested,
    int Columns,
    int Rows,
    // A right-click on a visible queue row, or -1. Opens the context menu.
    int RightClickedQueueItem = -1,
    // A click on an item of the open context menu, or -1.
    int ClickedMenuItem = -1,
    // The head's own menu was dismissed by the user rather than by a command. Only a head that
    // realises the menu as a real popup sets this; the ones that draw the overlay have nothing to
    // dismiss.
    bool MenuClosed = false,
    // An absolute row to scroll to, or -1. What a scrollbar thumb reports.
    int ScrollTo = -1);
