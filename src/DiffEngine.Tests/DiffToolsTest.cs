public class DiffToolsTest :
    XunitContextBase
{
    [Fact]
    public void MaxInstancesToLaunch() =>
    #region MaxInstancesToLaunch
        DiffRunner.MaxInstancesToLaunch(10);
    #endregion

    [Fact]
    public void AddTool()
    {
        var diffToolPath = FakeDiffTool.Exe;

        #region AddTool

        var resolvedTool = DiffTools.AddTool(
            name: "MyCustomDiffTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            createNoWindow: false,
            launchArguments: new(
                Left: (tempFile, targetFile) => $"\"{targetFile}\" \"{tempFile}\"",
                Right: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\""),
            exePath: diffToolPath,
            binaryExtensions: [".jpg"])!;

        #endregion

        var resolved = DiffTools.Resolved.Select(_ => _.Name).First();
        Assert.Equal(resolvedTool.Name, resolved);
        Assert.True(DiffTools.TryFindByExtension(".jpg", out var forExtension));
        Assert.Equal(resolvedTool.Name, forExtension.Name);
    }

    [Fact]
    public void OrderShouldNotMessWithAddTool()
    {
        var diffToolPath = FakeDiffTool.Exe;
        var resolvedTool = DiffTools.AddTool(
            name: "MyCustomDiffTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            createNoWindow: false,
            launchArguments: new(
                Left: (tempFile, targetFile) => $"\"{targetFile}\" \"{tempFile}\"",
                Right: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\""),
            exePath: diffToolPath,
            binaryExtensions: [])!;
        DiffTools.UseOrder(DiffTool.VisualStudio, DiffTool.AraxisMerge);
        Assert.Equal("MyCustomDiffTool", resolvedTool.Name);
        Assert.True(DiffTools.TryFindByExtension(".txt", out var forExtension));
        Assert.Equal("MyCustomDiffTool", forExtension.Name);
    }

    [Fact]
    public void TextConvention()
    {
        var diffToolPath = FakeDiffTool.Exe;
        DiffTools.AddTool(
            name: "MyCustomDiffTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            createNoWindow: false,
            launchArguments: new(
                Left: (tempFile, targetFile) => $"\"{targetFile}\" \"{tempFile}\"",
                Right: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\""),
            exePath: diffToolPath,
            binaryExtensions: []);
        var combine = Path.Combine(SourceDirectory, "input.temp.txtConvention");
        Assert.True(DiffTools.TryFindForInputFilePath(combine, out var tool));
        Assert.Equal("MyCustomDiffTool", tool.Name);
    }

#if DEBUG
    [Fact]
    public void AddToolBasedOn()
    {
        #region AddToolBasedOn

        var resolvedTool = DiffTools.AddToolBasedOn(
            DiffTool.VisualStudio,
            name: "MyCustomDiffTool",
            launchArguments: new(
                Left: (temp, target) => $"\"custom args \"{target}\" \"{temp}\"",
                Right: (temp, target) => $"\"custom args \"{temp}\" \"{target}\""))!;

        #endregion

        Assert.Equal(resolvedTool, DiffTools.Resolved.First());
        Assert.True(DiffTools.TryFindByExtension(".txt", out var forExtension));
        Assert.Equal(resolvedTool, forExtension);
        Assert.Equal("\"custom args \"bar\" \"foo\"", resolvedTool.LaunchArguments.Left("foo", "bar"));
        Assert.Equal("\"custom args \"foo\" \"bar\"", resolvedTool.LaunchArguments.Right("foo", "bar"));
    }
#endif
    static async Task AddToolAndLaunch()
    {
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

    [Fact]
    public Task LaunchSpecificTextDiff() =>
        DiffRunner.LaunchAsync(DiffTool.VisualStudioCode,
            Path.Combine(SourceDirectory, "input.temp.txt"),
            Path.Combine(SourceDirectory, "input.target.txt"));
    /**
    [Fact]
    public Task LaunchSpecificImageDiff() =>
        DiffRunner.LaunchAsync(DiffTool.P4Merge,
            Path.Combine(SourceDirectory, "input.temp.png"),
            Path.Combine(SourceDirectory, "input.target.png"));

    [Fact]
    public async Task LaunchImageDiff()
    {
        foreach (var tool in DiffTools.Resolved)
        {
            await DiffRunner.LaunchAsync(tool,
                Path.Combine(SourceDirectory, "input.temp.png"),
                Path.Combine(SourceDirectory, "input.target.png"));
        }
    }

    [Fact]
    public async Task LaunchTextDiff()
    {
        foreach (var tool in DiffTools.Resolved)
        {
            await DiffRunner.LaunchAsync(tool,
                Path.Combine(SourceDirectory, "input.temp.txt"),
                Path.Combine(SourceDirectory, "input.target.txt"));
        }
    }

    [Fact]
    public Task LaunchSpecificTextDiff() =>
        DiffRunner.LaunchAsync(DiffTool.WinMerge,
            Path.Combine(SourceDirectory, "input.temp.txt"),
            Path.Combine(SourceDirectory, "input.target.txt"));

    [Fact]
    public Task TextFileConvention()
    {
        var tempFile = Path.Combine(SourceDirectory, "input.temp.txtConvention");
        var targetFile = Path.Combine(SourceDirectory, "input.target.txtConvention");
        return DiffRunner.LaunchAsync(tempFile, targetFile);
    }

    [Fact]
    public Task LaunchForTextAsync()
    {
        var tempFile = Path.Combine(SourceDirectory, "input.temp.txtConvention");
        var targetFile = Path.Combine(SourceDirectory, "input.target.txtConvention");
        return DiffRunner.LaunchForTextAsync(tempFile, targetFile);
    }
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

        Assert.Equal(DiffTool.VisualStudio, DiffTools.Resolved.First()
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
    public DiffToolsTest(ITestOutputHelper output) :
        base(output) =>
        DiffTools.Reset();
}