public class ExeFinderTests :
    XunitContextBase
{
    [Fact]
    public void MultiMatchDir_order1()
    {
        var dir1 = Path.Combine(SourceDirectory, "DirForSearch", "dir1");
        var dir2 = Path.Combine(SourceDirectory, "DirForSearch", "dir2");
        Directory.SetLastWriteTime(dir2, DateTime.Now.AddDays(-1));
        Directory.SetLastWriteTime(dir1, DateTime.Now);
        var path = Path.Combine(SourceDirectory, "DirForSearch", "*", "TextFile1.txt");
        Assert.True(ExeFinder.TryFind(path, out var result));
        Assert.True(File.Exists(result), result);
    }

    [Fact]
    public void MultiMatchDir_order2()
    {
        var dir1 = Path.Combine(SourceDirectory, "DirForSearch", "dir1");
        var dir2 = Path.Combine(SourceDirectory, "DirForSearch", "dir2");
        Directory.SetLastWriteTime(dir1, DateTime.Now.AddDays(-1));
        Directory.SetLastWriteTime(dir2, DateTime.Now);
        var path = Path.Combine(SourceDirectory, "DirForSearch", "*", "TextFile1.txt");
        Assert.True(ExeFinder.TryFind(path, out var result));
        Assert.True(File.Exists(result), result);
    }

    [Fact]
    public void FullFilePath()
    {
        Assert.True(ExeFinder.TryFind(SourceFile, out var result));
        Assert.True(File.Exists(result), result);
    }

    [Fact]
    public void FullFilePath_missing()
    {
        Assert.False(ExeFinder.TryFind(SourceFile.Replace(".cs", ".foo"), out var result));
        Assert.Null(result);
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

    [Fact]
    public void WildCardInDir()
    {
        var directory = SourceDirectory.Replace("Tests", "Test*");
        var path = Path.Combine(directory, "ExeFinderTests.cs");
        Assert.True(ExeFinder.TryFind(path, out var result));
        Assert.True(File.Exists(result), result);
    }

    [Fact]
    public void WildCardInDir_missing()
    {
        var directory = SourceDirectory.Replace("Tests", "Test*.Foo");
        var path = Path.Combine(directory, "ExeFinderTests.cs");
        Assert.False(ExeFinder.TryFind(path, out var result));
        Assert.Null(result);
    }

    public ExeFinderTests(ITestOutputHelper output) :
        base(output)
    {
    }
}