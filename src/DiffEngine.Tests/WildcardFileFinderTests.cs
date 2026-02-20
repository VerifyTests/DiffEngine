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
}
