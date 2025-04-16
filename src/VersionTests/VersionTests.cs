public class VersionTests
{
    // work around https://github.com/orgs/VerifyTests/discussions/1366
#if !NET9_0_OR_GREATER
    [Fact]
    public void Immutable()
    {
        var assemblyName = typeof(FrozenSet).Assembly.GetName();
        Assert.Equal(new(8, 0, 0, 0), assemblyName.Version);
    }
#endif

    [Fact]
    public void Management()
    {
        var assemblyName = typeof(ManagementQuery).Assembly.GetName();
#if NETFRAMEWORK
        Assert.Equal(new(4, 0, 0, 0), assemblyName.Version);
#else
        Assert.Equal(new(8, 0, 0, 0), assemblyName.Version);
#endif
    }
}