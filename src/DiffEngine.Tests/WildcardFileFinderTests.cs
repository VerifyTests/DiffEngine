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
