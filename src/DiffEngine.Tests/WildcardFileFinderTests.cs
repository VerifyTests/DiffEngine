public class WildcardFileFinderTests
{
    static string SourceFile { get; } = GetSourceFile();
    static string SourceDirectory { get; } = Path.GetDirectoryName(GetSourceFile())!;
    static string GetSourceFile([CallerFilePath] string path = "") => path;

    [Test]
    public async Task MultiMatchDir_order1()
    {
        var dir1 = Path.Combine(SourceDirectory, "DirForSearch", "dir1");
        var dir2 = Path.Combine(SourceDirectory, "DirForSearch", "dir2");
        Directory.SetLastWriteTime(dir2, DateTime.Now.AddDays(-1));
        Directory.SetLastWriteTime(dir1, DateTime.Now);
        var path = Path.Combine(SourceDirectory, "DirForSearch", "*", "TextFile1.txt");
        await Assert.That(WildcardFileFinder.TryFind(path, out var result)).IsTrue();
        await Assert.That(File.Exists(result)).IsTrue();
    }

    [Test]
    public async Task MultiMatchDir_order2()
    {
        var dir1 = Path.Combine(SourceDirectory, "DirForSearch", "dir1");
        var dir2 = Path.Combine(SourceDirectory, "DirForSearch", "dir2");
        Directory.SetLastWriteTime(dir1, DateTime.Now.AddDays(-1));
        Directory.SetLastWriteTime(dir2, DateTime.Now);
        var path = Path.Combine(SourceDirectory, "DirForSearch", "*", "TextFile1.txt");
        await Assert.That(WildcardFileFinder.TryFind(path, out var result)).IsTrue();
        await Assert.That(File.Exists(result)).IsTrue();
    }

    /// <summary>
    /// The NuGet cache holds a folder for each version of DiffEngine any project on the machine
    /// has referenced, and the viewer bundled in each. Taken most recently written first, the one
    /// found was whichever had been restored last, so restoring an old project put a viewer from
    /// before the arguments this library passes ahead of every newer one. As numbers, too: 20.10.0
    /// is above 20.6.0.
    /// </summary>
    [Test]
    public async Task VersionFoldersAreTakenHighestFirst() =>
        await Assert.That(First("20.10.0", "20.6.0", "20.3.1")).IsEqualTo("20.10.0");

    /// <summary>
    /// A release is above every prerelease of it, whichever was written last.
    /// </summary>
    [Test]
    public async Task AReleaseIsAboveItsPrereleases() =>
        await Assert.That(First("21.0.0", "21.0.0-beta.10", "21.0.0-beta.2")).IsEqualTo("21.0.0");

    /// <summary>
    /// And prereleases part by part, numbers as numbers: beta.10 is after beta.2, and both are
    /// after any alpha.
    /// </summary>
    [Test]
    public async Task PrereleasesAreOrderedByTheirLabels() =>
        await Assert.That(First("21.0.0-beta.10", "21.0.0-beta.2", "21.0.0-alpha.11")).IsEqualTo("21.0.0-beta.10");

    /// <summary>
    /// Everything else is still taken most recently written first. A tool's install folder is
    /// named for the tool, and a number on its own is not a version: Visual Studio 2022 installs
    /// to 2022 and its successor to 18.
    /// </summary>
    [Test]
    [Arguments("Beyond Compare 5", "Beyond Compare 4")]
    [Arguments("2022", "18")]
    [Arguments("20.6.0", "current")]
    public async Task OtherFoldersAreTakenMostRecentlyWrittenFirst(string older, string newer) =>
        await Assert.That(First(older, newer)).IsEqualTo(newer);

    /// <summary>
    /// The folder a wildcard resolves to first, of ones holding the same file and written in the
    /// order given, a day apart, so the last named is the most recently written.
    /// </summary>
    static string First(params string[] folders)
    {
        var root = Path.Combine(Path.GetTempPath(), $"DiffEngine.Wildcard.{Guid.NewGuid():N}");
        try
        {
            for (var index = 0; index < folders.Length; index++)
            {
                var directory = Path.Combine(root, folders[index]);
                Directory.CreateDirectory(Path.Combine(directory, "tools"));
                File.WriteAllText(Path.Combine(directory, "tools", "viewer.txt"), "");
                Directory.SetLastWriteTime(directory, DateTime.Now.AddDays(index - folders.Length));
            }

            if (!WildcardFileFinder.TryFind(Path.Combine(root, "*", "tools", "viewer.txt"), out var found))
            {
                throw new("Nothing was found.");
            }

            return Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(found))!);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task FullFilePath()
    {
        await Assert.That(WildcardFileFinder.TryFind(SourceFile, out var result)).IsTrue();
        await Assert.That(File.Exists(result)).IsTrue();
    }

    [Test]
    public async Task FullFilePath_missing()
    {
        await Assert.That(WildcardFileFinder.TryFind(SourceFile.Replace(".cs", ".foo"), out var result)).IsFalse();
        await Assert.That(result).IsNull();
    }

    //[Fact]
    //public void WildCardInFile()
    //{
    //    var path = Path.Combine(SourceDirectory, "WildcardFileFinder*.cs");
    //    Assert.True(WildcardFileFinder.TryFind(path, out var result));
    //    Assert.True(File.Exists(result));
    //}

    //[Fact]
    //public void WildCardInFile_missing()
    //{
    //    var path = Path.Combine(SourceDirectory, "WildcardFileFinder*.foo");
    //    Assert.False(WildcardFileFinder.TryFind(path, out var result));
    //    Assert.Null(result);
    //}

    [Test]
    public async Task WildCardInDir()
    {
        var directory = SourceDirectory.Replace("Tests", "Test*");
        var path = Path.Combine(directory, "WildcardFileFinderTests.cs");
        await Assert.That(WildcardFileFinder.TryFind(path, out var result)).IsTrue();
        await Assert.That(File.Exists(result)).IsTrue();
    }

    [Test]
    public async Task WildCardInDir_missing()
    {
        var directory = SourceDirectory.Replace("Tests", "Test*.Foo");
        var path = Path.Combine(directory, "WildcardFileFinderTests.cs");
        await Assert.That(WildcardFileFinder.TryFind(path, out var result)).IsFalse();
        await Assert.That(result).IsNull();
    }

    /// <summary>
    /// ExamDiff's search directory, <c>%ProgramFiles%\ExamDiff Pro*\</c>, as ExpandProgramFiles
    /// rewrites it for <c>%ProgramW6432%</c> and <c>%ProgramFiles(x86)%</c>. A variable nothing
    /// defines stands in for one of those on a machine that lacks it: ExpandEnvironmentVariables
    /// leaves it as written, and the wildcard segment right after it is then enumerated under a
    /// relative directory that does not exist.
    /// </summary>
    [Test]
    public async Task AnUndefinedVariableBeforeAWildcardIsNotFoundRatherThanThrown()
    {
        var path = Path.Combine("%DiffEngine_TestUndefined%", "ExamDiff Pro*", "ExamDiff.exe");

        await Assert.That(WildcardFileFinder.TryFind(path, out var result)).IsFalse();
        await Assert.That(result).IsNull();
    }

    /// <summary>
    /// The same thing one level up, through the call DiffTools' static constructor makes for every
    /// definition. Thrown there, it is a TypeInitializationException for every later use of
    /// DiffTools in the process.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task ResolvingATwoLevelWildcardUnderAnUndefinedVariableIsNotFound()
    {
        var windows = Definitions.Tools
            .Single(_ => _.Tool == DiffTool.ExamDiff)
            .OsSupport
            .Windows!;
        var support = new OsSupport(
            Windows: windows with
            {
                SearchDirectories = [@"%DiffEngine_TestUndefined%\ExamDiff Pro*\"]
            });

        await Assert.That(OsSettingsResolver.Resolve("UndefinedExamDiff", support, out var path, out _)).IsFalse();
        await Assert.That(path).IsNull();
    }
}
