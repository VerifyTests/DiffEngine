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
}
