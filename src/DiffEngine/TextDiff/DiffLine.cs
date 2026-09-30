namespace DiffEngine;

/// <summary>
/// One line of a <see cref="TextDiff"/>. Line numbers are 1-based: <paramref name="ExpectedLine"/>
/// is null for an <see cref="DiffLineKind.Added"/> line and <paramref name="ReceivedLine"/> for a
/// <see cref="DiffLineKind.Removed"/> one.
/// </summary>
public record DiffLine(DiffLineKind Kind, string Text, int? ExpectedLine, int? ReceivedLine);
