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
        // The two fields that ride the payload as themselves rather than base64, so a line break
        // in either would end its line and the rest would be read as more of the payload -
        // shifting the fixed lines, or forging one of the trailing ones. Both are public settable
        // properties, and a path may legally hold a line break off Windows. Refused rather than
        // written, because there is no shape of this format that can carry one
        AgainstLineBreak(patch.SourceFile, nameof(InlinePatch.SourceFile));
        AgainstLineBreak(patch.Framework, nameof(InlinePatch.Framework));

        var expression = patch.OriginalExpression is null
            ? ""
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(patch.OriginalExpression));
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes(patch.NewContent));
        // Test names are caller supplied and can contain anything, so base64 like the content
        // fields. Frameworks are short monikers and stay readable, like mode.
        var testName = patch.TestName is null
            ? ""
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(patch.TestName));
        // Past the six fixed lines, so a reader that predates them skips them rather than
        // rejecting the payload. That tolerance is why the version does not move for an added
        // field. Member names are identifiers, but base64 like the rest of the added fields
        var value = patch.OriginalValue is null
            ? ""
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(patch.OriginalValue));
        var memberName = patch.MemberName is null
            ? ""
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(patch.MemberName));
        return $"version: 2\nsourceFile: {patch.SourceFile}\nlineHint: {patch.LineHint}\nmode: {patch.Mode}\noriginalExpression: {expression}\nnewContent: {content}\ntestName: {testName}\nframework: {patch.Framework}\noriginalValue: {value}\nmemberName: {memberName}\n";
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
            // IsDefined as well, because TryParse takes a number as readily as a name: "mode: 7"
            // parsed to an InlinePatchMode that is none of them, and then fell through every mode
            // check in the patcher to behave as a Set
            !Enum.TryParse<InlinePatchMode>(modeText, out var mode) ||
            !Enum.IsDefined(typeof(InlinePatchMode), mode) ||
            !TryValue(lines[4], "originalExpression", out var expressionBase64) ||
            !TryValue(lines[5], "newContent", out var contentBase64))
        {
            return false;
        }

        string? expression;
        string content;
        string? testName = null;
        string? framework = null;
        string? originalValue = null;
        string? memberName = null;
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
                    continue;
                }

                if (TryValue(lines[index], "originalValue", out var valueBase64))
                {
                    originalValue = valueBase64.Length == 0
                        ? null
                        : Encoding.UTF8.GetString(Convert.FromBase64String(valueBase64));
                    continue;
                }

                if (TryValue(lines[index], "memberName", out var memberNameBase64))
                {
                    memberName = memberNameBase64.Length == 0
                        ? null
                        : Encoding.UTF8.GetString(Convert.FromBase64String(memberNameBase64));
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
            Framework = framework,
            OriginalValue = originalValue,
            MemberName = memberName
        };
        return true;
    }

    static void AgainstLineBreak(string? value, string name)
    {
        if (value is not null &&
            (value.IndexOf('\n') != -1 || value.IndexOf('\r') != -1))
        {
            throw new ArgumentException($"InlinePatch.{name} cannot contain a line break. Value: {value}");
        }
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
