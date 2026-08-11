/// <summary>
/// Renders a <see cref="Screen"/> as a fixed width character grid. Pure ASCII so the verified
/// files review as ordinary text diffs, and deterministic on every platform, which is what lets
/// the screen tests run everywhere rather than only where a GPU exists.
/// </summary>
static class AsciiRenderer
{
    const int queueWidth = 22;

    /// <summary>
    /// Marker, space, four digit line number, two spaces.
    /// </summary>
    const int gutterWidth = 8;

    public static string Render(Screen screen)
    {
        var columns = Math.Max(40, screen.Columns);
        var queue = screen.Queue.Count > 0 ? queueWidth : 0;
        var (left, right) = SplitPanes(columns, queue);
        var widths = queue > 0 ? new[] { queue, left, right } : [left, right];

        var builder = new StringBuilder();
        builder.Append('+').Append('-', columns - 2).Append("+\n");
        builder.Append(Full(Justify(screen.Title, screen.Subtitle, columns - 4), columns)).Append('\n');
        builder.Append(Separator(widths)).Append('\n');
        builder.Append(Headers(screen, widths)).Append('\n');
        builder.Append(Separator(widths)).Append('\n');

        var body = Math.Max(1, screen.Rows - ScreenBuilder.Chrome);
        for (var index = 0; index < body; index++)
        {
            builder.Append(Body(screen, widths, index)).Append('\n');
        }

        builder.Append(Separator(widths)).Append('\n');
        builder.Append(Full(Justify(Buttons(screen), screen.Status, columns - 4), columns)).Append('\n');
        builder.Append('+').Append('-', columns - 2).Append('+');
        return Overlay(builder.ToString(), screen);
    }

    /// <summary>
    /// The open context menu, drawn over the finished grid the way the pixel heads float theirs
    /// over the frame. Anchored one line under its queue row, inset into the queue column.
    /// </summary>
    static string Overlay(string text, Screen screen)
    {
        if (screen.Menu is not { } menu ||
            menu.Labels.Count == 0)
        {
            return text;
        }

        var lines = text.Split('\n').Select(_ => _.ToCharArray()).ToList();
        var width = menu.Labels.Max(_ => _.Length) + 2;
        // Border, title, separator, headers, separator: five lines sit above the first body row.
        var top = 5 + menu.Row + 1;
        var left = 3;

        void Write(int line, string content)
        {
            if (line < 0 ||
                line >= lines.Count)
            {
                return;
            }

            var row = lines[line];
            for (var index = 0; index < content.Length && left + index < row.Length; index++)
            {
                row[left + index] = content[index];
            }
        }

        var border = $"+{new string('-', width)}+";
        Write(top, border);
        for (var index = 0; index < menu.Labels.Count; index++)
        {
            Write(top + 1 + index, $"| {menu.Labels[index].PadRight(width - 2)} |");
        }

        Write(top + 1 + menu.Labels.Count, border);
        return string.Join("\n", lines.Select(_ => new string(_)));
    }

    static (int left, int right) SplitPanes(int columns, int queue)
    {
        var bars = queue > 0 ? 4 : 3;
        var remaining = Math.Max(4, columns - bars - queue);
        var left = remaining / 2;
        return (left, remaining - left);
    }

    static string Headers(Screen screen, IReadOnlyList<int> widths)
    {
        if (widths.Count == 3)
        {
            return Bordered(
            [
                Cell($"Pending ({screen.PendingCount})", widths[0]),
                Cell(screen.Left.Header, widths[1]),
                Cell(screen.Right.Header, widths[2])
            ]);
        }

        return Bordered(
        [
            Cell(screen.Left.Header, widths[0]),
            Cell(screen.Right.Header, widths[1])
        ]);
    }

    static string Body(Screen screen, IReadOnlyList<int> widths, int index)
    {
        var cells = new List<string>(widths.Count);
        if (widths.Count == 3)
        {
            cells.Add(Cell(QueueCell(screen, index), widths[0]));
        }

        cells.Add(Cell(RowCell(screen.Left, index, widths[^2]), widths[^2]));
        cells.Add(Cell(RowCell(screen.Right, index, widths[^1]), widths[^1]));
        return Bordered(cells);
    }

    static string QueueCell(Screen screen, int index)
    {
        if (index >= screen.Queue.Count)
        {
            return "";
        }

        var item = screen.Queue[index];
        if (item.Kind == QueueRowKind.Header)
        {
            // Flush left with no marker column, which is what reads as a heading in plain text.
            return item.Label;
        }

        var marker = item.Selected ? '>' : ' ';
        var failed = item.Status is null ? "" : " !";
        return $"{marker} {item.Label}{failed}";
    }

    static string RowCell(Pane pane, int index, int width)
    {
        if (index >= pane.Rows.Count)
        {
            return "";
        }

        var row = pane.Rows[index];
        if (row.Kind == RowKind.Filler)
        {
            return "";
        }

        var text = Fit(row.Text, Math.Max(1, width - 2 - gutterWidth));
        return $"{Marker(row.Kind)} {row.LineNumber,4}  {text}";
    }

    static char Marker(RowKind kind) =>
        kind switch
        {
            RowKind.Added => '+',
            RowKind.Removed => '-',
            RowKind.Modified => '~',
            _ => ' '
        };

    static string Buttons(Screen screen)
    {
        var builder = new StringBuilder();
        foreach (var button in screen.Buttons)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            // Disabled buttons keep their slot so the footer does not reflow as the queue drains.
            builder.Append(button.Enabled ? '[' : '(');
            builder.Append(button.Label);
            builder.Append(button.Enabled ? ']' : ')');
        }

        return builder.ToString();
    }

    static string Bordered(IReadOnlyList<string> cells) =>
        $"|{string.Join("|", cells)}|";

    static string Separator(IReadOnlyList<int> widths) =>
        $"+{string.Join("+", widths.Select(_ => new string('-', _)))}+";

    static string Full(string content, int columns) =>
        $"| {Fit(content, columns - 4)} |";

    static string Cell(string content, int width) =>
        $" {Fit(content, Math.Max(1, width - 2))} ";

    static string Justify(string left, string right, int width)
    {
        if (right.Length == 0)
        {
            return Fit(left, width);
        }

        var gap = width - right.Length - left.Length;
        if (gap < 1)
        {
            return Fit($"{left} {right}", width);
        }

        return $"{left}{new string(' ', gap)}{right}";
    }

    static string Fit(string text, int width)
    {
        // Tabs and stray newlines would break the grid, so flatten them before measuring.
        var flat = RowText.Flatten(text);
        if (flat.Length == width)
        {
            return flat;
        }

        if (flat.Length < width)
        {
            return flat.PadRight(width);
        }

        return $"{flat.AsSpan(0, width - 1)}>";
    }
}
