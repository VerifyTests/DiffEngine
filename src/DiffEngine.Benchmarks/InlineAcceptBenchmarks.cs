using System.Text;
using BenchmarkDotNet.Attributes;

namespace DiffEngine.Benchmarks;

// Accepting every inline snapshot in one test file, which is what "Accept all" does over a file
// that holds many. A patch at a time, each call reads, lexes and rewrites the whole file, and
// handed over together the file is read and written once. A member is twenty lines, so 25 call
// sites is a 500 line file of 30 KB, and 500 is 10,000 lines and 600 KB.
//
// The file is a real one, in a directory of its own under the temp folder, because the applier's
// cost is as much the file system's as the patcher's: a mutex, a read, a temporary written beside
// the source and a swap, per patch. Where that folder is scanned as it is written to, which is
// what a virus scanner does by default, the swap is most of it.
//
// Each literal accepted adds five lines, so every call site below it has moved by the time its
// own patch is applied, as it has in a real accept-all.
[MemoryDiagnoser]
public class InlineAcceptBenchmarks
{
    [Params(25, 500)]
    public int CallSites;

    string directory = "";
    string path = "";
    byte[] pristine = [];
    string source = "";
    InlinePatch[] patches = [];
    InlinePatch[] appends = [];

    [GlobalSetup]
    public void Setup()
    {
        directory = Path.Combine(Path.GetTempPath(), $"DiffEngineBenchmarks_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "GeneratedTests.cs");

        var (text, snapshotLines, verifyLines) = Generate(CallSites);
        source = text;
        pristine = new UTF8Encoding(false).GetBytes(text);
        File.WriteAllBytes(path, pristine);
        // As a source file is when a test run asks about it: written at the last save, and not
        // in the last few seconds, which is the file a probe keeps what it read of
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));
        patches = new InlinePatch[CallSites];
        appends = new InlinePatch[CallSites];
        for (var index = 0; index < CallSites; index++)
        {
            patches[index] = new(path, snapshotLines[index], $"\"old {index:D4}\"", $"first line of {index:D4}\nsecond line\nthird line")
            {
                TestName = $"GeneratedTests.Member{index:D4}",
                MemberName = $"Member{index:D4}"
            };
            // What a producer asks before it declares a verification inline: whether the verify
            // call has somewhere to hang a Snapshot call
            appends[index] = new(path, verifyLines[index], null, "content", InlinePatchMode.Append)
            {
                TestName = $"GeneratedTests.Member{index:D4}",
                MemberName = $"Member{index:D4}"
            };
        }
    }

    [GlobalCleanup]
    public void Cleanup() =>
        Directory.Delete(directory, true);

    // What the tray and the viewer do for a bulk accept: a call to Apply for each entry
    [Benchmark]
    public int AcceptEach()
    {
        File.WriteAllBytes(path, pristine);
        var applied = 0;
        foreach (var patch in patches)
        {
            if (InlineApplier.Apply(patch).Status == InlineApplyStatus.Applied)
            {
                applied++;
            }
        }

        return Every(applied);
    }

    // The same patches handed over in one call, which reads and writes the file once
    [Benchmark]
    public int AcceptTogether()
    {
        File.WriteAllBytes(path, pristine);
        var applied = 0;
        foreach (var result in InlineApplier.ApplyAll(patches))
        {
            if (result.Status == InlineApplyStatus.Applied)
            {
                applied++;
            }
        }

        return Every(applied);
    }

    // What a test run does once for each call site it has not seen before. Nothing is written,
    // so this is the read and the lexing by themselves
    [Benchmark]
    public int AnchorEach()
    {
        var anchored = 0;
        foreach (var patch in appends)
        {
            if (InlineApplier.CanAnchor(patch).Status == InlineApplyStatus.Applied)
            {
                anchored++;
            }
        }

        return Every(anchored);
    }

    // The patcher alone over the same patches, each applied to what the last one left: what
    // AcceptTogether costs with the file system, the encoding and the locks taken out
    [Benchmark]
    public int PatchInMemory()
    {
        var current = source;
        var results = new InlineApplyResult[patches.Length];
        InlineApplier.PatchInTurn(SourceLanguage.CSharp, ref current, patches, path, results);
        return Every(results.Count(_ => _.Status == InlineApplyStatus.Applied));
    }

    // The lexing every one of those starts with
    [Benchmark]
    public bool Lex()
    {
        using var scan = SourceLanguage.CSharp.Scan(source);
        return scan.IsCode(0);
    }

    // A benchmark that measured five hundred refusals would look like a very fast applier
    int Every(int count)
    {
        if (count != CallSites)
        {
            throw new($"{count} of {CallSites} generated patches applied.");
        }

        return count;
    }

    // Twenty lines a member: an attribute, a declaration, fourteen statements with a string and a
    // comment each, and a verify call with its snapshot on a line of its own.
    static (string source, int[] snapshotLines, int[] verifyLines) Generate(int members)
    {
        var builder = new StringBuilder();
        var snapshotLines = new int[members];
        var verifyLines = new int[members];
        var line = 0;

        void Add(string text)
        {
            builder.Append(text);
            builder.Append("\r\n");
            line++;
        }

        Add("public class GeneratedTests");
        Add("{");
        for (var member = 0; member < members; member++)
        {
            Add("    [Test]");
            Add($"    public Task Member{member:D4}()");
            Add("    {");
            for (var statement = 0; statement < 14; statement++)
            {
                Add($"        var value{statement:D2} = Describe(\"input {statement:D2} of member {member:D4}\", {statement}); // read below");
            }

            Add("        return Verify(value00)");
            verifyLines[member] = line;
            Add($"            .Snapshot(\"old {member:D4}\");");
            snapshotLines[member] = line;
            Add("    }");
            Add("");
        }

        Add("}");
        return (builder.ToString(), snapshotLines, verifyLines);
    }
}
