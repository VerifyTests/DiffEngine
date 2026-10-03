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
        // Content only a regular literal can hold that is also in the layout shape: a blank first
        // line and nothing but indentation after the last newline. The reader strips a value in
        // that shape whatever literal held it, so it has to be written wrapped in layout of its own
        "\nx = \"\"\"\n",
        "\n  x = \"\"\"\n  ",
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

    /// <summary>
    /// The call as each mode meets it: with a Snapshot call to put the literal in, and with none.
    /// </summary>
    static readonly (InlinePatchMode Mode, string Call)[] midLineCalls =
    [
        (InlinePatchMode.Set, "Verify(\"x\").Snapshot().ToTask()"),
        (InlinePatchMode.Append, "Verify(\"x\").ToTask()")
    ];

    /// <summary>
    /// Comments F# reads as one comment each, which the scanner has to read the same way or the
    /// call below is taken to be inside a comment or a string. What F# does inside a comment is
    /// lex it: a string is a string, a char literal is a char literal, and <c>(*)</c> is the
    /// operator. Each is here because fsi was asked, and is asked again by
    /// <see cref="CommentsAndNamesAreReadAsTheCompilerReadsThem" />.
    /// </summary>
    internal static readonly string[] Comments =
    [
        "(* returns \"*)\" when closed *)",
        "(* see \"(*\" *)",
        "(* the (*) operator *)",
        "(* (*)*)",
        // Verbatim, where a backslash escapes nothing and the string ends at the quote after it
        "(* path @\"c:\\\" *)",
        "(* @\"a\"\"*)\" *)",
        // Regular, where it does
        "(* \"a\\\"*)\" *)",
        "(* \"a\\\\\" *)",
        // Not interpolated: a dollar, and then whichever string follows it
        "(* $\"{1}*)\" *)",
        "(* $@\"c:\\\" *)",
        "(* \"\"\" a \" *) \" b \"\"\" *)",
        // A quote that opens nothing
        "(* char '\"' *)",
        "(* char '\\\"' *)",
        "(* '\"' \"*)\" *)",
        "(* a'\"' *)",
        "(* 'a'\"'*)\" *)",
        // And a tick that is no char literal, so the string after it is one
        "(* it's \"*)\" *)",
        "(* 'a \"*)\" *)",
        "(* a (* \"*)\" *) c *)",
        "(*\"*)\"*)",
        "(* \"a\n*)\nb\" *)",
        "(* // *)",
        "(**)",
        "(***)"
    ];

    /// <summary>
    /// Double backticked names, which hold anything: none of these opens a string or a comment.
    /// </summary>
    internal static readonly string[] QuotedNames =
    [
        "returns \"x",
        "a (* b",
        "a // b",
        "it's '\"' b",
        "a ` b"
    ];

    /// <summary>
    /// Lines of code F# compiles, each holding a tick or an interpolated string the scanner has
    /// to step over as the compiler does, or the call under it is taken to be inside a literal.
    /// A tick after a name or a type parameter is part of it, and anywhere else opens a char
    /// literal, which is where a quote that opens no string is written. A hole of
    /// <c>$"..."</c> holds char literals and comments and no string; one of a triple quoted
    /// literal holds strings too, a verbatim one that ends in a doubled quote among them. Each is
    /// here because fsi was asked, and is asked again by
    /// <see cref="TicksAndHolesAreReadAsTheCompilerReadsThem" />.
    /// </summary>
    internal static readonly string[] Code =
    [
        "let a' = 1 in ignore a'",
        "let a'b = 1 in ignore a'b",
        "let a'' = 1 in ignore a''",
        "ignore '\"'",
        "ignore ['a';'\"';'\\'']",
        "ignore ('\"', '\\\\', '\\\"')",
        "ignore (1,'\"')",
        "ignore (id<char>'\"')",
        "ignore 'a'B",
        "ignore ('\\065', '\\u0041', '\\U00000041', '\\x41')",
        "let f (x: 'a) (y: 'a) = x in ignore (f '\"' '\"')",
        "let x' = '\"' in ignore x'",
        "let x = 1 in ignore (x,'\"')",
        "ignore $\"{1}\"",
        "ignore $\"{{literal}}\"",
        "ignore $\"{ {| A = 1 |}.A }\"",
        "ignore $\"{'}'}\"",
        "ignore $\"{'{'}\"",
        "ignore $\"{'\"'}\"",
        "ignore $\"{1:N2}\"",
        "ignore $\"%d{1}\"",
        "ignore $@\"c:\\{1}\\\"",
        "ignore @$\"c:\\{1}\\\"",
        "ignore $@\"a\"\"{1}\"\"b\"",
        "ignore $\"a\\\"{1}\\\"\"",
        "ignore $\"{1 (* } *)}\"",
        "ignore $\"{1 (* { *)}\"",
        "ignore $\"{ [ for c in ['}'] -> c ] }\"",
        "ignore $\"}}\"",
        "ignore $\"{{\"",
        "ignore $\"{1}}}\"",
        "ignore $\"\\{1}\"",
        "ignore $\"\"\"{\"a\"}\"\"\"",
        "ignore $\"\"\"{ \"}\" }\"\"\"",
        "ignore $\"\"\"{ \"{\" }\"\"\"",
        "ignore $\"\"\"{ '\"' }\"\"\"",
        "ignore $\"\"\"{ '{' }\"\"\"",
        "ignore $\"\"\"{ $\"{1}\" }\"\"\"",
        "ignore $\"\"\"{ @\"a\"\"\" }\"\"\"",
        "ignore $\"\"\"{ @\"a\"\"b\" }\"\"\"",
        "ignore $\"\"\"{ \"a\\\"\" }\"\"\"",
        "ignore $\"\"\"{1}\"\" \"\"\"",
        "ignore $\"\"\"{ (1, \"}}\") }\"\"\"",
        "ignore $\"\"\"{ {| A = \"}\" |}.A }\"\"\"",
        // A backslash escapes no brace, so this is a backslash and then a hole
        "ignore $\"\\{'\"'}\"",
        "ignore $\"{1}{'\"'}\"",
        "let x' = 1 in ignore $\"{x'}{'}'}\"",
        "let ``a}b`` = 1 in ignore $\"{ ``a}b`` }\"",
        // A line comment in a hole runs to the end of its line, whatever it holds
        "ignore $\"{1 // }\n    }\"",
        "ignore $\"\"\"{ 1 // } \"\"\"\n    }\"\"\"",
        // Doubled braces are text with one dollar, and what opens a hole with two
        "ignore $\"\"\"{{ {1} }}\"\"\"",
        "ignore $\"\"\"{{{'\"'}}}\"\"\"",
        "ignore $$\"\"\"{{1}} {literal}\"\"\"",
        "ignore $$\"\"\"{{ \"}}\" }}\"\"\"",
        "ignore $$\"\"\"{ {{'\"'}} }\"\"\"",
        "ignore $$\"\"\"{{{'\"'}}}\"\"\"",
        "ignore $$$\"\"\"{{ {{{'\"'}}} }}\"\"\"",
        "ignore \"a\"B",
        "ignore \"\"\"a\"\"\"B"
    ];

    [Test]
    [RequiresDotnet]
    public async Task TicksAndHolesAreReadAsTheCompilerReadsThem()
    {
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("found"));
        var builder = new StringBuilder(prelude);
        // Every line the scanner lost the call under, rather than the first, since one reading
        // put right is as likely as not to be the reason for the next
        var lost = new List<string>();
        for (var index = 0; index < Code.Length; index++)
        {
            // The line is the first thing in the body, so a scanner that reads a literal in it as
            // longer or shorter than it is loses the call under it, and a compiler that does not
            // take the line has no function to call
            var snippet = $"let code{index} () =\n    {Code[index]}\n    Verify(\"x\").Snapshot().ToTask()\n";
            var status = InlinePatcher.TryApply(SourceLanguage.FSharp, snippet, 3, InlinePatchMode.Set, null, null, null, null, false, "found", out var patched, out _);
            if (status != PatchStatus.Applied)
            {
                lost.Add(Code[index]);
                continue;
            }

            builder.Append(patched);
            builder.Append($"check \"code{index}\" (code{index} ()) \"{expected}\"\n\n");
        }

        await Assert.That(lost).IsEmpty();
        builder.Append(footer);
        var path = Path.Combine(Path.GetTempPath(), $"DiffEngineFsCode_{Guid.NewGuid():N}.fsx");
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false));
        try
        {
            var (exitCode, output) = RunFsi(path);

            await Assert.That(output).Contains("ALL OK");
            await Assert.That(exitCode).IsEqualTo(0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    [RequiresDotnet]
    public async Task CommentsAndNamesAreReadAsTheCompilerReadsThem()
    {
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("found"));
        var builder = new StringBuilder(prelude);
        for (var index = 0; index < Comments.Length; index++)
        {
            // The comment is the first thing in the body, so a scanner that ends it early or not
            // at all loses the call under it, and a compiler that reads it as anything but one
            // comment has no function to call
            builder.Append(Patch($"let comment{index} () =\n    {Comments[index]}\n    Verify(\"x\").Snapshot().ToTask()\n", 3, InlinePatchMode.Set, "found"));
            builder.Append($"check \"comment{index}\" (comment{index} ()) \"{expected}\"\n\n");
        }

        for (var index = 0; index < QuotedNames.Length; index++)
        {
            var name = $"``{QuotedNames[index]} {index}``";
            builder.Append(Patch($"let {name} () =\n    Verify(\"x\").Snapshot().ToTask()\n", 2, InlinePatchMode.Set, "found"));
            builder.Append($"check \"name{index}\" ({name} ()) \"{expected}\"\n\n");
        }

        builder.Append(footer);
        var path = Path.Combine(Path.GetTempPath(), $"DiffEngineFsComments_{Guid.NewGuid():N}.fsx");
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false));
        try
        {
            var (exitCode, output) = RunFsi(path);

            await Assert.That(output).Contains("ALL OK");
            await Assert.That(exitCode).IsEqualTo(0);
        }
        finally
        {
            File.Delete(path);
        }
    }

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

    const string prelude =
            """
            type Chain(value: string) =
                member _.Snapshot(expected: string) = Chain(expected)
                member _.ToTask() = value

            let Verify (value: string) = Chain(value)

            // Somewhere for do!, let! and return! to be written. Where the offside line falls is
            // decided by those keywords and not by the builder behind them, so this one hands back
            // what was bound, and the literal can be read out of a computation expression by the
            // same check as out of anything else
            type Capture() =
                member _.Bind(value: string, continuation: unit -> string) = value + continuation ()
                member _.Zero() = ""
                member _.ReturnFrom(value: string) = value

            let capture = Capture()

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


            """;

    const string footer =
        """
        if failures = 0 then printfn "ALL OK" else printfn "%d FAILURES" failures
        exit failures

        """;

    static string BuildScript()
    {
        var builder = new StringBuilder(prelude);

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
                    $"""
                      let deep{index} () =
                          let inner () =
                              Verify("x").Snapshot().ToTask()
                          inner ()

                      """,
                    3,
                    InlinePatchMode.Set,
                    content));
            builder.Append($"check \"deep{index}\" (deep{index} ()) \"{expected}\"\n\n");

            // A chain across lines, where the call after the literal is on the line below it
            builder.Append(
                Patch(
                    $"""
                      let chain{index} () =
                          Verify("x")
                              .Snapshot()
                              .ToTask()

                      """,
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

            // A call with something in front of it on its line. The column its expression starts
            // at is then past the line's indentation, and that column is what a new line has to
            // clear: the appended call, and a literal given a line of its own. Measured from the
            // line instead, only the return! survived both, and the do! a literal alone
            foreach (var (mode, call) in midLineCalls)
            {
                Add($"doBang{mode}", $"let doBang{mode}{index} () =\n    capture {{\n        do! {call}\n    }}\n", 3, mode);
                Add($"letBang{mode}", $"let letBang{mode}{index} () =\n    capture {{\n        let! _ = {call}\n        return! \"\"\n    }}\n", 3, mode);
                Add($"returnBang{mode}", $"let returnBang{mode}{index} () =\n    capture {{\n        return! {call}\n    }}\n", 3, mode);
                Add($"local{mode}", $"let local{mode}{index} () =\n    let result = {call}\n    result\n", 2, mode);
                Add($"oneLine{mode}", $"let oneLine{mode}{index} () = {call}\n", 1, mode);
            }

            // The line the chain ends on is not one of its calls here, and sits at the column the
            // expression starts at: a closing paren where a formatter puts it, and an argument
            // that starts its own line. An appended call lined up with either is on the offside
            // line, whether or not the call starts its line
            Add("underParen", $"let underParen{index} () =\n    Verify(\n        \"x\"\n    ).ToTask()\n", 2, InlinePatchMode.Append);
            Add("underArgument", $"let underArgument{index} () =\n    capture {{\n        do! Verify(\n            \"x\").ToTask()\n    }}\n", 3, InlinePatchMode.Append);

            void Add(string name, string snippet, int lineHint, InlinePatchMode mode)
            {
                builder.Append(Patch(snippet, lineHint, mode, content));
                builder.Append($"check \"{name}{index}\" ({name}{index} ()) \"{expected}\"\n\n");
            }
        }

        // A Remove of a call that has a line to itself, over a line holding a Snapshot call, leaves
        // its line empty, so the next apply of the same patch does not find that call where its
        // own was. The empty line is between two statements of a computation expression here,
        // and inside one chain, which has to still be one expression
        builder.Append(Patch("let removedBang () =\n    capture {\n        do! Verify(\"x\")\n                .Snapshot(\"dup\").ToTask()\n        do! Verify(\"y\").Snapshot(\"dup\").ToTask()\n    }\n", 4, InlinePatchMode.Remove, "", "dup"));
        builder.Append($"check \"removedBang\" (removedBang ()) \"{Convert.ToBase64String(Encoding.UTF8.GetBytes("xdup"))}\"\n\n");
        builder.Append(Patch("let removedChain () =\n    Verify(\"x\")\n        .Snapshot(\"dup\")\n        .Snapshot(\"kept\").ToTask()\n", 3, InlinePatchMode.Remove, "", "dup"));
        builder.Append($"check \"removedChain\" (removedChain ()) \"{Convert.ToBase64String(Encoding.UTF8.GetBytes("kept"))}\"\n\n");
        // And where a statement was, taken whole for being a call on a name and nothing else
        builder.Append(Patch("let removedStatement () =\n    let first = Verify(\"x\")\n    first.Snapshot(\"dup\")\n    first.Snapshot(\"dup\")\n    first.ToTask()\n", 3, InlinePatchMode.Remove, "", "dup"));
        builder.Append($"check \"removedStatement\" (removedStatement ()) \"{Convert.ToBase64String(Encoding.UTF8.GetBytes("x"))}\"\n\n");

        builder.Append(footer);
        return builder.ToString();
    }

    static string Patch(string snippet, int lineHint, InlinePatchMode mode, string content, string? originalValue = null)
    {
        var status = InlinePatcher.TryApply(SourceLanguage.FSharp, snippet, lineHint, mode, null, originalValue, null, null, false, content, out var patched, out var reason);
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
