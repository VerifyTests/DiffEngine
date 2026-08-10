/// <summary>
/// A parsed command line. <paramref name="Error"/> is non null when the arguments were not
/// understood, in which case nothing else is meaningful.
/// </summary>
/// <param name="Attach">
/// Display someone else's queue rather than owning one. Set by whoever launched this viewer
/// knowing it already holds the queue itself, which today means DiffEngineTray.
/// </param>
record ViewerRequest(
    ViewerMode Mode,
    string? Left,
    string? Right,
    string? Source,
    int Line,
    string? Error,
    bool Attach = false);
