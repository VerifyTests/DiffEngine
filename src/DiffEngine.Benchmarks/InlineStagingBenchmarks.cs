using BenchmarkDotNet.Attributes;

namespace DiffEngine.Benchmarks;

// InlineStaging.Clear as a passing inline verification calls it: once each, to take away whatever
// an earlier failing run staged for the call site. Almost always there is nothing to take, so
// what it costs is what it costs to find that out.
//
// The project is a real directory, with an obj shaped like the one a multi-targeted test project
// builds up: two configurations of five frameworks, each with its NuGet, ref and refint folders
// and a VerifyInline with nothing in it. 151 directories in all, which is Verify.Tests' own obj.
[MemoryDiagnoser]
public class InlineStagingBenchmarks
{
    string root = "";
    string source = "";
    string intermediate = "";
    string stagedSource = "";
    string stagedIntermediate = "";

    [GlobalSetup]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), $"DiffEngineBenchmarks_{Guid.NewGuid():N}");
        (source, intermediate) = Project(Path.Combine(root, "Empty"));
        (stagedSource, stagedIntermediate) = Project(Path.Combine(root, "Staged"));

        // Five snapshots a run with no viewer left behind, none of them for the call site asked
        // about, written the way that run writes them
        var staging = Path.Combine(stagedIntermediate, InlineStaging.DirectoryName);
        for (var index = 0; index < 5; index++)
        {
            var patch = new InlinePatch(stagedSource, 100 + index, "\"old\"", "new")
            {
                TestName = $"SampleTests.Other{index}",
                MemberName = $"Other{index}",
                OriginalValue = "old"
            };
            InlinePatchFile.Write(Path.Combine(staging, $"SampleTests.Other{index}.inlinepatch"), patch);
        }
    }

    [GlobalCleanup]
    public void Cleanup() =>
        Directory.Delete(root, true);

    // Nothing staged anywhere under the project
    [Benchmark]
    public int NothingStaged() =>
        InlineStaging.Clear(source, 42, "Sample", intermediate, value: "content");

    // Other call sites have snapshots staged, and this one has none
    [Benchmark]
    public int OthersStaged() =>
        InlineStaging.Clear(stagedSource, 42, "Sample", stagedIntermediate, value: "content");

    static (string source, string intermediate) Project(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Sample.csproj"), "<Project />");
        var source = Path.Combine(directory, "SampleTests.cs");
        File.WriteAllText(source, "// sample");

        var obj = Path.Combine(directory, "obj");
        foreach (var configuration in new[] {"Debug", "Release"})
        {
            foreach (var framework in new[] {"net11.0", "net10.0", "net9.0", "net8.0", "net48"})
            {
                var modern = framework != "net48";
                var intermediate = Path.Combine(obj, configuration, framework);
                Directory.CreateDirectory(Path.Combine(intermediate, "VerifyReceived"));
                Directory.CreateDirectory(Path.Combine(intermediate, InlineStaging.DirectoryName));
                if (modern)
                {
                    Directory.CreateDirectory(Path.Combine(intermediate, "ref"));
                    Directory.CreateDirectory(Path.Combine(intermediate, "refint"));
                }

                // What restores leave behind: a folder per version of a package that ships targets
                string[] versions = modern ? ["1.0.174", "1.0.176", "1.0.180", "1.0.181"] : ["1.0.180", "1.0.181"];
                foreach (var version in versions)
                {
                    Directory.CreateDirectory(Path.Combine(intermediate, "NuGet", "629807350F552F76", "ProjectDefaults", version, "ProjectDefaults"));
                }
            }
        }

        // What a Release run of one framework stages under, and names when it clears
        return (source, Path.Combine(obj, "Release", "net10.0"));
    }
}
