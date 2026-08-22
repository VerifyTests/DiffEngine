/// <summary>
/// Extensions are matched the way file systems produce them.
/// <para>
/// ExtensionLookup, PathLookup and BinaryExtensions all compared ordinally while every
/// registration is lowercase, so .PNG, .JPG and .Docx - which is exactly what Windows and macOS
/// hand back - matched nothing. The tool resolved for foo.png and not for foo.PNG.
/// Viewer/ImageExtensions already uses OrdinalIgnoreCase for the same kind of lookup.
/// </para>
/// </summary>
[NotInParallel]
public class ExtensionCaseTests
{
    [Test]
    public async Task ExtensionsResolveWhateverTheirCasing()
    {
        var extension = $".zz{Guid.NewGuid():N}";
        var tool = DiffTools.AddTool(
            name: $"CaseProbe{Guid.NewGuid():N}",
            autoRefresh: false,
            isMdi: false,
            supportsText: false,
            requiresTarget: true,
            useShellExecute: false,
            launchArguments: new(
                Left: (temp, target) => $"\"{temp}\" \"{target}\"",
                Right: (temp, target) => $"\"{target}\" \"{temp}\""),
            exePath: Environment.ProcessPath!,
            binaryExtensions: [extension]);

        await Assert.That(tool).IsNotNull();

        await Assert.That(DiffTools.TryFindByExtension(extension, out _)).IsTrue();
        await Assert.That(DiffTools.TryFindByExtension(extension.ToUpperInvariant(), out _)).IsTrue();

        await Assert.That(DiffTools.TryFindForInputFilePath($"file{extension.ToUpperInvariant()}", out _)).IsTrue();

        await Assert.That(tool!.BinaryExtensions.Contains(extension.ToUpperInvariant())).IsTrue();
    }
}
