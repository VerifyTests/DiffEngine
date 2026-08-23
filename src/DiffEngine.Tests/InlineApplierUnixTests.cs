#if NET10_0
/// <summary>
/// What the applier does to the file itself, rather than to the text in it. The patch is written
/// through a temporary and swapped in, and on Linux and macOS that swap is a rename - so what
/// survives it is the temporary, with the temporary's identity.
/// </summary>
public class InlineApplierUnixTests :
    IDisposable
{
    const string source = "class C\n{\n    void M() => Verify(value).Snapshot(\"old\");\n}";

    /// <summary>
    /// A source file reached through a symlink - a worktree, a vendored copy, a checkout shared
    /// between two trees. The rename replaced the link with a regular file: the link stopped being
    /// one, and the file it pointed at still held the old literal, so the next run reported the
    /// same snapshot again and the patched copy was invisible to the compiler.
    /// </summary>
    [Test]
    // A symlink on Windows needs elevation or developer mode, so this cannot be arranged there.
    [RunOn(TUnit.Core.Enums.OS.Linux | TUnit.Core.Enums.OS.MacOs)]
    public async Task A_symlinked_source_is_followed_to_the_file_it_names()
    {
        var real = Path.Combine(directory, "Real.cs");
        File.WriteAllText(real, source);
        var link = Path.Combine(directory, "Link.cs");
        File.CreateSymbolicLink(link, real);

        var result = InlineApplier.Apply(Patch(link));

        await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
        await Assert.That(File.ReadAllText(real)).Contains("\"new\"");
        // Still a link, rather than a regular file holding the patch while the real one holds the
        // snapshot that failed
        await Assert.That(new FileInfo(link).LinkTarget).IsNotNull();
    }

    /// <summary>
    /// The temporary is created with this process's umask, so without carrying the mode across, a
    /// source file that was executable - or group writable, or read only to the world - came back
    /// as whatever the umask said.
    /// </summary>
    [Test]
    // A Unix file mode is not a thing Windows has.
    [RunOn(TUnit.Core.Enums.OS.Linux | TUnit.Core.Enums.OS.MacOs)]
    public async Task The_file_keeps_the_permissions_it_had()
    {
        var path = Path.Combine(directory, "Sample.cs");
        File.WriteAllText(path, source);
        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                  UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                  UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(path, mode);

        var result = InlineApplier.Apply(Patch(path));

        await Assert.That(result.Status).IsEqualTo(InlineApplyStatus.Applied);
        await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(mode);
    }

    /// <summary>
    /// Nothing here queues a patch, so none of them has a reviewable identity.
    /// </summary>
    static InlinePatch Patch(string sourceFile) =>
        new(sourceFile, 3, "\"old\"", "new")
        {
            TestName = null
        };

    public InlineApplierUnixTests()
    {
        directory = Path.Combine(Path.GetTempPath(), $"InlineApplierUnixTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
    }

    public void Dispose() =>
        Directory.Delete(directory, true);

    readonly string directory;
}
#endif
