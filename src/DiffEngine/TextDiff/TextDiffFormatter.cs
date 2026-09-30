using System.Globalization;

/// <summary>
/// Renders a <see cref="LineDiff"/> in the three shapes Verify.DiffPlex offered, so a failure
/// message reads the same after moving off it. Each line's span is appended as is, so formatting
/// copies nothing but the output.
/// <para>
/// Only trailing line breaks are trimmed, where Verify.DiffPlex trimmed all trailing whitespace.
/// Lines compare exactly here, so a trailing space can be the difference being reported.
/// </para>
/// </summary>
static class TextDiffFormatter
{
    public static string Format(LineDiff diff, TextDiffFormat format)
    {
        var builder = new StringBuilder();
        switch (format)
        {
            case TextDiffFormat.Full:
                Full(diff, builder, includeUnchanged: true);
                break;
            case TextDiffFormat.Minimal:
                Full(diff, builder, includeUnchanged: false);
                break;
            case TextDiffFormat.Compact:
                Compact(diff, builder);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, null);
        }

        var length = builder.Length;
        while (length > 0 &&
               builder[length - 1] == '\n')
        {
            length--;
        }

        builder.Length = length;
        return builder.ToString();
    }

    static void Full(LineDiff diff, StringBuilder builder, bool includeUnchanged)
    {
        foreach (var entry in diff.Entries)
        {
            if (entry.Kind == DiffLineKind.Unchanged)
            {
                if (!includeUnchanged)
                {
                    continue;
                }

                builder.Append("  ");
            }
            else
            {
                builder.Append(Symbol(entry.Kind));
                builder.Append(' ');
            }

            builder.Append(diff.Text(entry));
            builder.Append('\n');
        }
    }

    /// <summary>
    /// A changed line is its symbol, an unchanged line next to a change is its received line
    /// number, and the rest are left out. The numbers are zero padded to the widest one, and the
    /// symbols right aligned under them.
    /// </summary>
    static void Compact(LineDiff diff, StringBuilder builder)
    {
        var entries = diff.Entries;
        var width = Math.Max(diff.ReceivedLines.Length, 1).ToString(CultureInfo.InvariantCulture).Length;
        var numberFormat = "D" + width;
        var padding = new string(' ', width - 1);
        var last = entries.Count - 1;

        for (var index = 0; index <= last; index++)
        {
            var entry = entries[index];
            var nextChanged = index < last && IsChanged(entries[index + 1]);
            if (IsChanged(entry))
            {
                if (index == 0)
                {
                    AppendMarker(builder, padding, "[BOF]");
                }

                builder.Append(padding);
                builder.Append(Symbol(entry.Kind));
                builder.Append(' ');
                builder.Append(diff.Text(entry));
                builder.Append('\n');

                if (index == last)
                {
                    AppendMarker(builder, padding, "[EOF]");
                }

                continue;
            }

            var previousChanged = index > 0 && IsChanged(entries[index - 1]);
            if (!previousChanged &&
                !nextChanged)
            {
                continue;
            }

            builder.Append((entry.Received + 1).ToString(numberFormat, CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(diff.Text(entry));
            builder.Append('\n');
            if (!nextChanged)
            {
                builder.Append('\n');
            }
        }
    }

    static void AppendMarker(StringBuilder builder, string padding, string marker)
    {
        builder.Append(padding);
        builder.Append("  ");
        builder.Append(marker);
        builder.Append('\n');
    }

    static bool IsChanged(LineEntry entry) =>
        entry.Kind != DiffLineKind.Unchanged;

    static char Symbol(DiffLineKind kind)
    {
        if (kind == DiffLineKind.Added)
        {
            return '+';
        }

        return '-';
    }
}
