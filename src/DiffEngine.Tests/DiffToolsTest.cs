public class DiffToolsTest
{
    static string SourceDirectory { get; } = Path.GetDirectoryName(GetSourceFile())!;
    static string GetSourceFile([CallerFilePath] string path = "") => path;

    [Test]
    public void MaxInstancesToLaunch() =>
    #region MaxInstancesToLaunch
        DiffRunner.MaxInstancesToLaunch(10);
    #endregion

    [Test]
    public async Task AddTool()
    {
        var diffToolPath = FakeDiffTool.Exe;

        #region AddTool

        var resolvedTool = DiffTools.AddTool(
            name: "MyCustomDiffTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            useShellExecute: true,
            launchArguments: new(
                Left: (tempFile, targetFile) => $"\"{targetFile}\" \"{tempFile}\"",
                Right: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\""),
            exePath: diffToolPath,
            binaryExtensions: [".jpg"])!;

        #endregion

        var resolved = DiffTools.Resolved.Select(_ => _.Name).First();
        await Assert.That(resolved).IsEqualTo(resolvedTool.Name);
        await Assert.That(DiffTools.TryFindByExtension(".jpg", out var forExtension)).IsTrue();
        await Assert.That(forExtension!.Name).IsEqualTo(resolvedTool.Name);
    }

    [Test]
    public async Task OrderShouldNotMessWithAddTool()
    {
        var diffToolPath = FakeDiffTool.Exe;
        var resolvedTool = DiffTools.AddTool(
            name: "MyCustomDiffTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            useShellExecute: true,
            launchArguments: new(
                Left: (tempFile, targetFile) => $"\"{targetFile}\" \"{tempFile}\"",
                Right: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\""),
            exePath: diffToolPath,
            binaryExtensions: [])!;
        DiffTools.UseOrder(DiffTool.VisualStudio, DiffTool.AraxisMerge);
        await Assert.That(resolvedTool.Name).IsEqualTo("MyCustomDiffTool");
        await Assert.That(DiffTools.TryFindByExtension(".txt", out var forExtension)).IsTrue();
        await Assert.That(forExtension!.Name).IsEqualTo("MyCustomDiffTool");
    }

    [Test]
    public async Task TextConvention()
    {
        var diffToolPath = FakeDiffTool.Exe;
        DiffTools.AddTool(
            name: "MyCustomDiffTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            useShellExecute: true,
            launchArguments: new(
                Left: (tempFile, targetFile) => $"\"{targetFile}\" \"{tempFile}\"",
                Right: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\""),
            exePath: diffToolPath,
            binaryExtensions: []);
        var combine = Path.Combine(SourceDirectory, "input.temp.txtConvention");
        await Assert.That(DiffTools.TryFindForInputFilePath(combine, out var tool)).IsTrue();
        await Assert.That(tool!.Name).IsEqualTo("MyCustomDiffTool");
    }

#if DEBUG
     [Test]
     public void AddToolBasedOn()
     {
         // ReSharper disable once UnusedVariable
         #region AddToolBasedOn

         var resolvedTool = DiffTools.AddToolBasedOn(
             DiffTool.VisualStudio,
             name: "MyCustomDiffTool",
             launchArguments: new(
                 Left: (temp, target) => $"\"custom args \"{target}\" \"{temp}\"",
                 Right: (temp, target) => $"\"custom args \"{temp}\" \"{target}\""))!;

         #endregion
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
    /*

    [Fact]
    public Task LaunchSpecificDocxDiff() =>
        DiffRunner.LaunchAsync(DiffTool.MsWordDiff,
            Path.Combine(SourceDirectory, "input.temp.docx"),
            Path.Combine(SourceDirectory, "input.target.docx"));

    [Fact]
    public Task LaunchSpecificBinaryDiff() =>
        DiffRunner.LaunchAsync(DiffTool.VisualStudioCode,
            Path.Combine(SourceDirectory, "input.temp.bin"),
            Path.Combine(SourceDirectory, "input.target.bin"));
    [Fact]
    public Task LaunchSpecificTextDiff() =>
        DiffRunner.LaunchAsync(DiffTool.VisualStudioCode,
            Path.Combine(SourceDirectory, "input.temp.txt"),
            Path.Combine(SourceDirectory, "input.target.txt"));

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
    */
    //todo: re enable tests with fake diff tool.

    /*
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
*/
    [Test]
    public async Task TryFindByName_IsCaseInsensitive()
    {
        var diffToolPath = FakeDiffTool.Exe;

        DiffTools.AddTool(
            name: "MyCaseSensitiveTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            useShellExecute: true,
            launchArguments: new(
                Left: (tempFile, targetFile) => $"\"{targetFile}\" \"{tempFile}\"",
                Right: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\""),
            exePath: diffToolPath,
            binaryExtensions: []);

        // Exact case
        await Assert.That(DiffTools.TryFindByName("MyCaseSensitiveTool", out var tool1)).IsTrue();
        await Assert.That(tool1!.Name).IsEqualTo("MyCaseSensitiveTool");

        // All lowercase
        await Assert.That(DiffTools.TryFindByName("mycasesensitivetool", out var tool2)).IsTrue();
        await Assert.That(tool2!.Name).IsEqualTo("MyCaseSensitiveTool");

        // All uppercase
        await Assert.That(DiffTools.TryFindByName("MYCASESENSITIVETOOL", out var tool3)).IsTrue();
        await Assert.That(tool3!.Name).IsEqualTo("MyCaseSensitiveTool");

        // Mixed case
        await Assert.That(DiffTools.TryFindByName("myCASEsensitiveTOOL", out var tool4)).IsTrue();
        await Assert.That(tool4!.Name).IsEqualTo("MyCaseSensitiveTool");
    }

    [Test]
    public async Task AddTool_RejectsDuplicateNameCaseInsensitive()
    {
        var diffToolPath = FakeDiffTool.Exe;

        DiffTools.AddTool(
            name: "UniqueTool",
            autoRefresh: true,
            isMdi: false,
            supportsText: true,
            requiresTarget: true,
            useShellExecute: true,
            launchArguments: new(
                Left: (tempFile, targetFile) => $"\"{targetFile}\" \"{tempFile}\"",
                Right: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\""),
            exePath: diffToolPath,
            binaryExtensions: []);

        // Adding with different case should throw
        var exception = Assert.Throws<ArgumentException>(() =>
            DiffTools.AddTool(
                name: "UNIQUETOOL",
                autoRefresh: true,
                isMdi: false,
                supportsText: true,
                requiresTarget: true,
                useShellExecute: true,
                launchArguments: new(
                    Left: (tempFile, targetFile) => $"\"{targetFile}\" \"{tempFile}\"",
                    Right: (tempFile, targetFile) => $"\"{tempFile}\" \"{targetFile}\""),
                exePath: diffToolPath,
                binaryExtensions: []));

        await Assert.That(exception.Message).Contains("Tool with name already exists");
    }

    public DiffToolsTest() =>
        DiffTools.Reset();
}
