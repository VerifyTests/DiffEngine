namespace DiffEngine;

/// <summary>
/// Reads and writes the staged inline patch file. Plain text with base64 encoded
/// content fields so the format is readable without a JSON dependency.
/// </summary>
public static class InlinePatchFile
{
    public static void Write(string path, InlinePatch patch)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Build(patch), new UTF8Encoding(false));
    }

    public static string Build(InlinePatch patch)
    {
        var expression = patch.OriginalExpression is null
            ? ""
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(patch.OriginalExpression));
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes(patch.NewContent));
        // Test names are caller supplied and can contain anything, so base64 like the content
        // fields. Frameworks are short monikers and stay readable, like mode.
        var testName = patch.TestName is null
            ? ""
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(patch.TestName));
        return $"version: 2\nsourceFile: {patch.SourceFile}\nlineHint: {patch.LineHint}\nmode: {patch.Mode}\noriginalExpression: {expression}\nnewContent: {content}\ntestName: {testName}\nframework: {patch.Framework}\n";
    }

    public static bool TryRead(string path, [NotNullWhen(true)] out InlinePatch? patch)
    {
        patch = null;
        string text;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            text = File.ReadAllText(path);
        }
        catch
        {
            return false;
        }

        return TryParse(text, out patch);
    }

    public static bool TryParse(string text, [NotNullWhen(true)] out InlinePatch? patch)
    {
        patch = null;
        var lines = text
            // A writer that encodes with a preamble prefixes the text with a BOM. Tolerated here
            // so the payload is judged on its content, wherever it came from.
            .TrimStart('\uFEFF')
            .Replace("\r\n", "\n")
            .Split('\n');
        if (lines.Length < 6 ||
            !TryValue(lines[0], "version", out var version) ||
            version != "2" ||
            !TryValue(lines[1], "sourceFile", out var sourceFile) ||
            sourceFile.Length == 0 ||
            !TryValue(lines[2], "lineHint", out var lineText) ||
            !int.TryParse(lineText, out var lineHint) ||
            !TryValue(lines[3], "mode", out var modeText) ||
            !Enum.TryParse<InlinePatchMode>(modeText, out var mode) ||
            !TryValue(lines[4], "originalExpression", out var expressionBase64) ||
            !TryValue(lines[5], "newContent", out var contentBase64))
        {
            return false;
        }

        string? expression;
        string content;
        string? testName = null;
        string? framework = null;
        try
        {
            expression = expressionBase64.Length == 0
                ? null
                : Encoding.UTF8.GetString(Convert.FromBase64String(expressionBase64));
            content = Encoding.UTF8.GetString(Convert.FromBase64String(contentBase64));

            // Read tolerantly past the six fixed lines: order agnostic, absent means null, and
            // unknown lines are skipped so a payload written by a newer sender still parses.
            for (var index = 6; index < lines.Length; index++)
            {
                if (TryValue(lines[index], "testName", out var testNameBase64))
                {
                    testName = testNameBase64.Length == 0
                        ? null
                        : Encoding.UTF8.GetString(Convert.FromBase64String(testNameBase64));
                    continue;
                }

                if (TryValue(lines[index], "framework", out var frameworkValue))
                {
                    framework = frameworkValue.Length == 0 ? null : frameworkValue;
                }
            }
        }
        catch (FormatException)
        {
            return false;
        }

        patch = new(sourceFile, lineHint, expression, content, mode)
        {
            TestName = testName,
            Framework = framework
        };
        return true;
    }

    static bool TryValue(string line, string key, out string value)
    {
        value = "";
        var prefix = key + ": ";
        if (line.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = line.Substring(prefix.Length);
            return true;
        }

        // Empty value: "key:" with no trailing space
        if (line == key + ":")
        {
            return true;
        }

        return false;
    }
}
