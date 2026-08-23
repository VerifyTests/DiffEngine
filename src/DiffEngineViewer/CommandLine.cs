static class CommandLine
{
    public const string Usage = """
        DiffEngineViewer <left> <right>
        DiffEngineViewer --inline --source <source file> --line <number>
        DiffEngineViewer --delete <file>
        DiffEngineViewer --diff <received> <target>
        DiffEngineViewer --attach

        Inline mode reads the patch payload from stdin.
        Delete mode takes a file that a passing test no longer produces.
        Diff mode takes a failing pair, and queues it rather than taking a window of its own.
        Attach mode reads nothing, and displays the queue of whoever owns the port.
        """;

    public static ViewerRequest Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return Error("No arguments.");
        }

        if (args[0] == "--attach")
        {
            if (args.Count != 1)
            {
                return Error("--attach takes no other arguments.");
            }

            return new(ViewerMode.Inline, null, null, null, 0, null, true);
        }

        if (args[0] == "--inline")
        {
            return ParseInline(args);
        }

        if (args[0] == "--delete")
        {
            if (args.Count != 2)
            {
                return Error("--delete takes one file.");
            }

            // Queue mode, not file mode: a delete owns the port and more can arrive after it,
            // which is the whole difference between the two modes.
            return new(ViewerMode.Inline, args[1], null, null, 0, null)
            {
                Delete = true
            };
        }

        if (args[0] == "--diff")
        {
            if (args.Count != 3)
            {
                return Error("--diff takes a received file and a target.");
            }

            // Queue mode for the same reason --delete is: DiffEngine sends these one pair at a
            // time, and every pair after the first has to join what is already on screen.
            return new(ViewerMode.Inline, args[1], args[2], null, 0, null)
            {
                Diff = true
            };
        }

        if (args.Count != 2)
        {
            return Error($"Expected two file paths, got {args.Count} arguments.");
        }

        return new(ViewerMode.File, args[0], args[1], null, 0, null);
    }

    static ViewerRequest ParseInline(IReadOnlyList<string> args)
    {
        string? source = null;
        var line = 0;
        for (var index = 1; index < args.Count; index++)
        {
            var name = args[index];
            if (name != "--source" &&
                name != "--line")
            {
                return Error($"Unknown argument: {name}");
            }

            if (index + 1 == args.Count)
            {
                return Error($"Missing value for {name}.");
            }

            var value = args[++index];
            if (name == "--source")
            {
                source = value;
                continue;
            }

            if (!int.TryParse(value, out line))
            {
                return Error($"--line must be a number, got: {value}");
            }
        }

        if (source is null)
        {
            return Error("--inline requires --source.");
        }

        if (line < 1)
        {
            return Error("--inline requires --line, of 1 or greater.");
        }

        return new(ViewerMode.Inline, null, null, source, line, null);
    }

    static ViewerRequest Error(string message) =>
        new(ViewerMode.File, null, null, null, 0, $"{message}\n\n{Usage}");
}
