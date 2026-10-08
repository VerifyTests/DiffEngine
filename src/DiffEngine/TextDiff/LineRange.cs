namespace DiffEngine;

/// <summary>
/// One line of a text, as a range of the original string rather than a copy of it. The terminator
/// is not part of the range.
/// </summary>
readonly record struct LineRange(int Start, int Length);
