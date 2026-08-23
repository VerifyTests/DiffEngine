public class CommandLineTests
{
    [Test]
    public Task Files() =>
        Verify(CommandLine.Parse(["left.txt", "right.txt"]));

    [Test]
    public Task Inline() =>
        Verify(CommandLine.Parse(["--inline", "--source", "Tests.cs", "--line", "42"]));

    [Test]
    public Task InlineArgumentsReordered() =>
        Verify(CommandLine.Parse(["--inline", "--line", "42", "--source", "Tests.cs"]));

    [Test]
    public Task Attach() =>
        Verify(CommandLine.Parse(["--attach"]));

    /// <summary>
    /// Two paths as well, but queue mode rather than file mode: this is the pair DiffEngine sends
    /// when the viewer is the diff tool, and every later pair has to be able to join it.
    /// </summary>
    [Test]
    public Task Diff() =>
        Verify(CommandLine.Parse(["--diff", "received.txt", "target.txt"]));

    [Test]
    [Arguments("NoArguments")]
    [Arguments("AttachWithMore", "--attach", "--source", "Tests.cs")]
    [Arguments("OneFile", "only.txt")]
    [Arguments("ThreeFiles", "a.txt", "b.txt", "c.txt")]
    [Arguments("MissingSource", "--inline", "--line", "42")]
    [Arguments("MissingLine", "--inline", "--source", "Tests.cs")]
    [Arguments("LineNotANumber", "--inline", "--source", "Tests.cs", "--line", "abc")]
    [Arguments("LineIsZero", "--inline", "--source", "Tests.cs", "--line", "0")]
    [Arguments("UnknownArgument", "--inline", "--wat", "1")]
    [Arguments("MissingValue", "--inline", "--source")]
    [Arguments("DiffWithoutTarget", "--diff", "received.txt")]
    public async Task Rejected(string name, params string[] args)
    {
        var request = CommandLine.Parse(args);

        await Assert.That(request.Error).IsNotNull();
        // The usage block is appended to every error, so assert only the leading explanation.
        await Verify(request.Error!.Split("\n\n")[0]).UseTextForParameters(name);
    }
}
