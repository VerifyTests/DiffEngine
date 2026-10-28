public class ProcessExTest
{
    /// <summary>
    /// Windows only, as TryGet is: it opens the process through kernel32.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task TryGet()
    {
        using var current = Process.GetCurrentProcess();
        await Assert.That(ProcessEx.TryGet(current.Id, out var found)).IsTrue();
        found!.Dispose();
    }

    /// <inheritdoc cref="TryGet" />
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task TryGetMissing()
    {
        await Assert.That(ProcessEx.TryGet(40000, out var found)).IsFalse();
        await Assert.That(found).IsNull();
    }

    [Test]
    public async Task DescribeIsSafeForDisposedProcess()
    {
        var process = Process.GetCurrentProcess();
        process.Dispose();

        // Id/MainModule can throw on a disposed process; Describe must swallow that.
        var description = ProcessEx.Describe(process);

        await Assert.That(description).IsNotNull();
    }
}
