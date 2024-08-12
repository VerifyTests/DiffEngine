[TestFixture]
public class DiffToolsTest
{
    [Test]
    public void MaxInstancesToLaunch() =>
    #region MaxInstancesToLaunch
        DiffRunner.MaxInstancesToLaunch(10);
    #endregion

    [Test]
    public void AddTool()
    {
        DiffTools.Reset();
        var diffToolPath = FakeDiffTool.Exe;

        #region AddTool

        var resolvedTool = DiffTools.AddTool(
            name: "MyCustomDiffTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            launchArguments: new(
                Left: (tempFile, targetFile) => $"\"{targetFile}\" \"{tempFile}\"",
                Right: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\""),
            exePath: diffToolPath,
            binaryExtensions: [".jpg"])!;

        #endregion

        AreEqual(resolvedTool.Name, DiffTools.Resolved.First()
            .Name);
        True(DiffTools.TryFindByExtension(".jpg", out var forExtension));
        AreEqual(resolvedTool.Name, forExtension!.Name);
    }

    [Test]
    public void OrderShouldNotMessWithAddTool()
    {
        DiffTools.Reset();
        var diffToolPath = FakeDiffTool.Exe;
        var resolvedTool = DiffTools.AddTool(
            name: "MyCustomDiffTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            launchArguments: new(
                Left: (tempFile, targetFile) => $"\"{targetFile}\" \"{tempFile}\"",
                Right: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\""),
            exePath: diffToolPath,
            binaryExtensions: [])!;
        DiffTools.UseOrder(DiffTool.VisualStudio, DiffTool.AraxisMerge);
        AreEqual("MyCustomDiffTool", resolvedTool.Name);
        True(DiffTools.TryFindByExtension(".txt", out var forExtension));
        AreEqual("MyCustomDiffTool", forExtension!.Name);
    }

#if DEBUG
    [Test]
    public void AddToolBasedOn()
    {
        DiffTools.Reset();
        #region AddToolBasedOn

        var resolvedTool = DiffTools.AddToolBasedOn(
            DiffTool.VisualStudio,
            name: "MyCustomDiffTool",
            launchArguments: new(
                Left: (temp, target) => $"\"custom args \"{target}\" \"{temp}\"",
                Right: (temp, target) => $"\"custom args \"{temp}\" \"{target}\""))!;

        #endregion

        AreEqual(resolvedTool, DiffTools.Resolved.First());
        True(DiffTools.TryFindByExtension(".txt", out var forExtension));
        AreEqual(resolvedTool, forExtension);
        AreEqual("\"custom args \"bar\" \"foo\"", resolvedTool.LaunchArguments.Left("foo", "bar"));
        AreEqual("\"custom args \"foo\" \"bar\"", resolvedTool.LaunchArguments.Right("foo", "bar"));
    }
#endif
    static async Task AddToolAndLaunch()
    {
        DiffTools.Reset();
        #region AddToolAndLaunch

        var resolvedTool = DiffTools.AddToolBasedOn(
            DiffTool.VisualStudio,
            name: "MyCustomDiffTool",
            launchArguments: new(
                Left: (temp, target) => $"\"custom args \"{target}\" \"{temp}\"",
                Right: (temp, target) => $"\"custom args \"{temp}\" \"{target}\""));

        await DiffRunner.LaunchAsync(resolvedTool!, "PathToTempFile", "PathToTargetFile");

        #endregion
    }

    /**
    [Fact]
    public Task LaunchSpecificImageDiff() =>
        DiffRunner.LaunchAsync(DiffTool.P4Merge,
            Path.Combine(SourceDirectory, "input.temp.png"),
            Path.Combine(SourceDirectory, "input.target.png"));
    **/
    //[Fact]
    //public async Task LaunchImageDiff()
    //{
    //    foreach (var tool in DiffTools.Resolved)
    //    {
    //        await DiffRunner.LaunchAsync(tool,
    //            Path.Combine(SourceDirectory, "input.temp.png"),
    //            Path.Combine(SourceDirectory, "input.target.png"));
    //    }
    //}

    //[Fact]
    //public async Task LaunchTextDiff()
    //{
    //    foreach (var tool in DiffTools.Resolved)
    //    {
    //        await DiffRunner.LaunchAsync(tool,
    //            Path.Combine(SourceDirectory, "input.temp.txt"),
    //            Path.Combine(SourceDirectory, "input.target.txt"));
    //    }
    //}
    /**
    [Fact]
    public Task LaunchSpecificTextDiff() =>
        DiffRunner.LaunchAsync(DiffTool.WinMerge,
            Path.Combine(SourceDirectory, "input.temp.txt"),
            Path.Combine(SourceDirectory, "input.target.txt"));
    **/

    //todo: re enable tests with fake diff tool.

    /**
#if DEBUG
    [Fact]
    public void ChangeOrder()
    {
        #region UseOrder

        DiffTools.UseOrder(DiffTool.VisualStudio, DiffTool.AraxisMerge);

        #endregion

        AreEqual(DiffTool.VisualStudio, DiffTools.Resolved.First()
            .Tool);
    }

    [Fact]
    public void TryFind()
    {
        Assert.True(DiffTools.TryFindByExtension(".txt", out var resolved));
        Assert.NotNull(resolved);

        Assert.False(DiffTools.TryFindByExtension(".notFound", out resolved));
        Assert.Null(resolved);
    }

    [Fact]
    public void TryFindByName()
    {
        Assert.True(DiffTools.TryFindByName(DiffTool.VisualStudio, out var resolved));
        Assert.NotNull(resolved);

        Assert.True(DiffTools.TryFindByName(resolved.Name, out resolved));
        Assert.NotNull(resolved);
    }
#endif
**/
}