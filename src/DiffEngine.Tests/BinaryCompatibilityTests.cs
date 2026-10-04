/// <summary>
/// Public signatures that shipped and have callers compiled against them.
/// <para>
/// Adding an optional parameter is source compatible and binary breaking: the old signature is
/// gone from the assembly, and a caller compiled against it fails at runtime with
/// MissingMethodException. 20.5.0 did exactly that to SettleInline, so every Verify up to 33.1.2
/// broke on any inline snapshot the moment a project referenced DiffEngine 20.5.0.
/// </para>
/// </summary>
public class BinaryCompatibilityTests
{
    [Test]
    public async Task SettleInlineFrom20_4()
    {
        var method = typeof(DiffRunner).GetMethod(
            nameof(DiffRunner.SettleInline),
            [typeof(string), typeof(int), typeof(string)]);
        await Assert.That(method).IsNotNull();
    }

    [Test]
    public async Task InlineQueueSettleFrom20_4()
    {
        var method = typeof(InlineQueue).GetMethod(
            nameof(InlineQueue.Settle),
            [typeof(string), typeof(string), typeof(string)]);
        await Assert.That(method).IsNotNull();
    }

    [Test]
    public async Task InlineStagingClearFrom20_4()
    {
        var method = typeof(InlineStaging).GetMethod(
            nameof(InlineStaging.Clear),
            [typeof(string), typeof(int), typeof(string), typeof(string), typeof(string)]);
        await Assert.That(method).IsNotNull();
    }

    /// <summary>
    /// What every consumer launches and tracks a pending file through. The launches for a file
    /// derived from another were added beside these, under names of their own, precisely so that
    /// none of these gained a parameter.
    /// </summary>
    [Test]
    [Arguments(nameof(DiffRunner.Launch))]
    [Arguments(nameof(DiffRunner.LaunchAsync))]
    [Arguments(nameof(DiffRunner.LaunchForText))]
    [Arguments(nameof(DiffRunner.LaunchForTextAsync))]
    public async Task LaunchByPath(string name)
    {
        var method = typeof(DiffRunner).GetMethod(
            name,
            [typeof(string), typeof(string), typeof(Encoding)]);
        await Assert.That(method).IsNotNull();
    }

    [Test]
    [Arguments(nameof(DiffRunner.AddDelete))]
    [Arguments(nameof(DiffRunner.AddDeleteAsync))]
    [Arguments(nameof(DiffRunner.SettleDelete))]
    public async Task ByFile(string name)
    {
        var method = typeof(DiffRunner).GetMethod(
            name,
            [typeof(string)]);
        await Assert.That(method).IsNotNull();
    }

    [Test]
    public async Task Kill()
    {
        var method = typeof(DiffRunner).GetMethod(
            nameof(DiffRunner.Kill),
            [typeof(string), typeof(string)]);
        await Assert.That(method).IsNotNull();
    }

    /// <summary>
    /// The launches a snapshot library makes for a file derived from another, from the release
    /// they shipped in. With no optional parameter among them, so a parameter one of them needs
    /// later is an overload beside it and not a change to it.
    /// </summary>
    [Test]
    [Arguments(nameof(DiffRunner.LaunchDerived))]
    [Arguments(nameof(DiffRunner.LaunchDerivedAsync))]
    [Arguments(nameof(DiffRunner.LaunchDerivedForText))]
    [Arguments(nameof(DiffRunner.LaunchDerivedForTextAsync))]
    public async Task LaunchDerived(string name)
    {
        var method = typeof(DiffRunner).GetMethod(
            name,
            [typeof(string), typeof(string), typeof(string), typeof(Encoding)]);
        await Assert.That(method).IsNotNull();
        await Assert.That(method!.GetParameters().Any(_ => _.IsOptional)).IsFalse();
    }

    [Test]
    [Arguments(nameof(DiffRunner.AddDerivedDelete))]
    [Arguments(nameof(DiffRunner.AddDerivedDeleteAsync))]
    public async Task AddDerivedDelete(string name)
    {
        var method = typeof(DiffRunner).GetMethod(
            name,
            [typeof(string), typeof(string)]);
        await Assert.That(method).IsNotNull();
        await Assert.That(method!.GetParameters().Any(_ => _.IsOptional)).IsFalse();
    }

    /// <summary>
    /// The obsolete shim is public too, and compiled against by whatever predates
    /// <see cref="DiffRunner" /> tracking for itself.
    /// </summary>
    [Test]
#pragma warning disable CS0618 // Type or member is obsolete
    public async Task TheTrayShim()
    {
        var addMove = typeof(DiffEngineTray).GetMethod(
            nameof(DiffEngineTray.AddMove),
            [typeof(string), typeof(string), typeof(string), typeof(string), typeof(bool), typeof(int?)]);
        var addDelete = typeof(DiffEngineTray).GetMethod(
            nameof(DiffEngineTray.AddDelete),
            [typeof(string)]);
#pragma warning restore CS0618
        await Assert.That(addMove).IsNotNull();
        await Assert.That(addDelete).IsNotNull();
    }
}
