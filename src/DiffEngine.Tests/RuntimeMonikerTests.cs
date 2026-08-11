public class RuntimeMonikerTests
{
    [Test]
    [Arguments(".NETCoreApp,Version=v9.0", "net9.0")]
    [Arguments(".NETCoreApp,Version=v10.0", "net10.0")]
    [Arguments(".NETCoreApp,Version=v3.1", "netcoreapp3.1")]
    [Arguments(".NETFramework,Version=v4.8", "net48")]
    [Arguments(".NETFramework,Version=v4.7.2", "net472")]
    [Arguments(".NETFramework,Version=v4.6.2", "net462")]
    [Arguments(".NETStandard,Version=v2.0", "netstandard2.0")]
    public async Task Maps(string frameworkName, string expected) =>
        await Assert.That(RuntimeMoniker.Map(frameworkName)).IsEqualTo(expected);

    /// <summary>
    /// Unknown is null rather than a guess: null degrades to last-writer-wins queue semantics,
    /// which is the safe direction.
    /// </summary>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("garbage")]
    [Arguments("Silverlight,Version=v5.0")]
    [Arguments(".NETCoreApp,Version=vNext")]
    public async Task UnknownIsNull(string? frameworkName) =>
        await Assert.That(RuntimeMoniker.Map(frameworkName)).IsNull();

    /// <summary>
    /// Runs on both test frameworks, so both socket-era families pin their own moniker. The
    /// .NET Framework side only asserts the family: the exact minor is the test host's business.
    /// </summary>
    [Test]
    public async Task CurrentMatchesThisRuntime() =>
#if NETFRAMEWORK
        await Assert.That(RuntimeMoniker.Current!).StartsWith("net4");
#else
        await Assert.That(RuntimeMoniker.Current).IsEqualTo("net10.0");
#endif
}
