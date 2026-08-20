/// <summary>
/// Patches F# the way a real accept does, then hands the result to the F# compiler and asks what
/// the literal is worth at runtime.
/// <para>
/// Everything else about F# rendering is asserted against what this repo believes F# means -
/// verbatim triple-quoted content, no indent stripping, which escapes exist. That belief is the
/// thing most likely to be wrong, and being wrong about it produces a snapshot that compiles and
/// silently differs, or a file that no longer compiles at all. So it is checked against fsi rather
/// than against another copy of the belief.
/// </para>
/// </summary>
public class FsCompilerRoundTripTests
{
    static readonly string[] cases =
    [
        "",
        " ",
        "abc",
        "a\nb",
        "\nabc",
        "abc\n",
        "\nabc\n",
        "a\n\n\nb",
        "line1\n    indented\nline3",
        "a\n   \nb",
        "trailing space  \nnext",
        "has \"quotes\" inside",
        "has \"quotes\"\nover lines",
        "back\\slash",
        "back\\slash\nover lines",
        "tab\there",
        "tab\there\nover lines",
        "bell\a and vertical\v tab",
        "esc null\0 del",
        "emoji 🎈 and unicode ☂",
        "emoji 🎈\nover lines",
        "$ {value} {{x}} %d",
        "{ \"json\": true }\n{ \"more\": 1 }",
        "\"",
        "\"\"",
        "\"\"\"",
        "\"\"\"\n\"\"\"",
        "\"starts with a quote\nsecond",
        "ends with a quote\nsecond\"",
        "has \"\"\" inside\nsecond",
        "(* not a comment *)\nsecond",
        "// not a comment\nsecond",
        "'ticked'\nsecond",
        // Line terminators, which are rendered as escapes rather than written into a
        // triple-quoted literal. What is being asked of fsi is that it reads the escape back as
        // the one character, since nothing on this side can tell whether it did
        "next line" + (char) 0x85 + "inside",
        "separator" + (char) 0x2028 + "inside",
        "paragraph" + (char) 0x2029 + "inside",
        "a\nb" + (char) 0x2028 + "c"
    ];

    [Test]
    [RequiresDotnet]
    public async Task PatchedSourceCompilesAndReadsBack()
    {
        var script = BuildScript();
        var path = Path.Combine(Path.GetTempPath(), $"DiffEngineFsRoundTrip_{Guid.NewGuid():N}.fsx");
        await File.WriteAllTextAsync(path, script, new UTF8Encoding(false));
        try
        {
            var (exitCode, output) = RunFsi(path);

            // Names the case and prints both values when one differs, so the output is the report
            await Assert.That(output).Contains("ALL OK");
            await Assert.That(exitCode).IsEqualTo(0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static string BuildScript()
    {
        var builder = new StringBuilder();
        builder.Append(
            """
            type Chain(value: string) =
                member _.Snapshot(expected: string) = Chain(expected)
                member _.ToTask() = value

            let Verify (value: string) = Chain(value)

            let mutable failures = 0

            // The reader's half of the convention, written out in F# rather than called into
            // DiffEngine: what a test library has to do with what the compiler handed it, and the
            // only way this checks the agreement rather than one side of it twice
            let strip (value: string) =
                let normalized = value.Replace("\r\n", "\n")
                let lines = normalized.Split('\n')
                if lines.Length < 2 then
                    normalized
                else
                    let closeIndent = lines.[lines.Length - 1]
                    let middle = lines.[1 .. lines.Length - 2]
                    let malformed =
                        middle
                        |> Array.exists (fun line ->
                            line.Length > 0 && not (line.StartsWith closeIndent) && line.Trim().Length > 0)
                    if lines.[0].Trim().Length > 0 || closeIndent.Trim().Length > 0 || malformed then
                        normalized
                    else
                        middle
                        |> Array.map (fun line ->
                            if line.StartsWith closeIndent then line.Substring closeIndent.Length else "")
                        |> String.concat "\n"

            let check (name: string) (literal: string) (expectedBase64: string) =
                let expected = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String expectedBase64)
                let actual = strip literal
                if actual <> expected then
                    failures <- failures + 1
                    printfn "FAIL %s" name
                    printfn "  literal  %A" literal
                    printfn "  actual   %A" actual
                    printfn "  expected %A" expected


            """);

        for (var index = 0; index < cases.Length; index++)
        {
            var content = cases[index];
            var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));

            // Set: the literal goes into a Snapshot call that is already there
            builder.Append(Patch($"let set{index} () =\n    Verify(\"x\").Snapshot().ToTask()\n", 2, InlinePatchMode.Set, content));
            builder.Append($"check \"set{index}\" (set{index} ()) \"{expected}\"\n\n");

            // Append: there is no Snapshot call yet, so one is written in front of ToTask
            builder.Append(Patch($"let append{index} () =\n    Verify(\"x\").ToTask()\n", 2, InlinePatchMode.Append, content));
            builder.Append($"check \"append{index}\" (append{index} ()) \"{expected}\"\n\n");

            // And a call site indented further in, where a multi-line literal's closing delimiter
            // would land left of the statement and the layout would not survive it
            builder.Append(
                Patch(
                    $"let deep{index} () =\n    let inner () =\n        Verify(\"x\").Snapshot().ToTask()\n    inner ()\n",
                    3,
                    InlinePatchMode.Set,
                    content));
            builder.Append($"check \"deep{index}\" (deep{index} ()) \"{expected}\"\n\n");

            // A chain across lines, where the call after the literal is on the line below it
            builder.Append(
                Patch(
                    $"let chain{index} () =\n    Verify(\"x\")\n        .Snapshot()\n        .ToTask()\n",
                    3,
                    InlinePatchMode.Set,
                    content));
            builder.Append($"check \"chain{index}\" (chain{index} ()) \"{expected}\"\n\n");

            // The shape an F# formatter writes: the literal on its own line with the closing paren
            // below it, where the verbatim form is kept whatever the content's last line is
            builder.Append(
                Patch(
                    $"let formatted{index} () =\n    Verify(\"x\")\n        .Snapshot(\n            \"\"\"placeholder\"\"\"\n        )\n        .ToTask()\n",
                    4,
                    InlinePatchMode.Set,
                    content));
            builder.Append($"check \"formatted{index}\" (formatted{index} ()) \"{expected}\"\n\n");
        }

        builder.Append(
            """
            if failures = 0 then printfn "ALL OK" else printfn "%d FAILURES" failures
            exit failures

            """);
        return builder.ToString();
    }

    static string Patch(string snippet, int lineHint, InlinePatchMode mode, string content)
    {
        var status = InlinePatcher.TryApply(SourceLanguage.FSharp, snippet, lineHint, mode, null, null, null, content, out var patched, out var reason);
        if (status != PatchStatus.Applied)
        {
            throw new($"{mode} patch was not applied: {reason}");
        }

        return patched;
    }

    static (int exitCode, string output) RunFsi(string path)
    {
        var startInfo = new ProcessStartInfo(RequiresDotnetAttribute.DotnetPath!)
        {
            Arguments = $"fsi --nologo \"{path}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit(120000))
        {
            process.Kill();
            throw new("fsi did not exit within two minutes.");
        }

        return (process.ExitCode, output);
    }
}
