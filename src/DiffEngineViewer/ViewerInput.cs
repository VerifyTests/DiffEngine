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
    int Rows);
