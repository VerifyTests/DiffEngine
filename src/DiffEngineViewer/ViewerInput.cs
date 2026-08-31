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
    int ScrollTo = -1,
    // The pane a drag is in progress in, or -1 for the frames - almost all of them - with no
    // button held down over one. Both ends are reported together for as long as it is held, and
    // then simply stop being reported, so a whole press-drag-release inside one frame still
    // arrives whole and a release has nothing left to say.
    int DragSide = -1,
    // Rows of the whole side rather than of the visible slice: a head knows the scroll top it drew
    // the press with, and only it can resolve a drag that spans a wheel notch.
    int DragAnchorRow = 0,
    int DragAnchorColumn = 0,
    int DragFocusRow = 0,
    int DragFocusColumn = 0);
