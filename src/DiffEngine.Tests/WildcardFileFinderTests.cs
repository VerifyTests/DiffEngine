public class WildcardFileFinderTests
{
    [Fact]
    public void MultiMatchDir_order1()
    {
        var dir1 = Path.Combine(Source.Directory, "DirForSearch", "dir1");
        var dir2 = Path.Combine(Source.Directory, "DirForSearch", "dir2");
        Directory.SetLastWriteTime(dir2, DateTime.Now.AddDays(-1));
        Directory.SetLastWriteTime(dir1, DateTime.Now);
        var path = Path.Combine(Source.Directory, "DirForSearch", "*", "TextFile1.txt");
        Assert.True(WildcardFileFinder.TryFind(path, out var result));
        Assert.True(File.Exists(result), result);
    }

    [Fact]
    public void MultiMatchDir_order2()
    {
        var dir1 = Path.Combine(Source.Directory, "DirForSearch", "dir1");
        var dir2 = Path.Combine(Source.Directory, "DirForSearch", "dir2");
        Directory.SetLastWriteTime(dir1, DateTime.Now.AddDays(-1));
        Directory.SetLastWriteTime(dir2, DateTime.Now);
        var path = Path.Combine(Source.Directory, "DirForSearch", "*", "TextFile1.txt");
        Assert.True(WildcardFileFinder.TryFind(path, out var result));
        Assert.True(File.Exists(result), result);
    }

    [Fact]
    public void FullFilePath()
    {
        Assert.True(WildcardFileFinder.TryFind(Source.File(), out var result));
        Assert.True(File.Exists(result), result);
    }

    [Fact]
    public void FullFilePath_missing()
    {
        Assert.False(WildcardFileFinder.TryFind(Source.File().Replace(".cs", ".foo"), out var result));
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
        var directory = Source.Directory.Replace("Tests", "Test*");
        var path = Path.Combine(directory, "WildcardFileFinderTests.cs");
        Assert.True(WildcardFileFinder.TryFind(path, out var result));
        Assert.True(File.Exists(result), result);
    }

    [Fact]
    public void WildCardInDir_missing()
    {
        var directory = Source.Directory.Replace("Tests", "Test*.Foo");
        var path = Path.Combine(directory, "WildcardFileFinderTests.cs");
        Assert.False(WildcardFileFinder.TryFind(path, out var result));
        Assert.Null(result);
    }
}