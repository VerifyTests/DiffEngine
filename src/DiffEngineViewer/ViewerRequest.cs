/// <summary>
/// A parsed command line. <paramref name="Error"/> is non null when the arguments were not
/// understood, in which case nothing else is meaningful.
/// </summary>
record ViewerRequest(
    ViewerMode Mode,
    string? Left,
    string? Right,
    string? Source,
    int Line,
    string? Error);
