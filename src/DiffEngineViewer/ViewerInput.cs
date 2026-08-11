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
    int ClickedMenuItem = -1);
