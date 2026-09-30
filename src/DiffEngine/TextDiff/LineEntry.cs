/// <summary>
/// One line of a <see cref="LineDiff"/>, as indexes into the line ranges of each side. An index is
/// -1 on the side the line is not on.
/// </summary>
readonly record struct LineEntry(DiffLineKind Kind, int Expected, int Received);
