#if NETFRAMEWORK

public class VersionTests
{
    // work around https://github.com/orgs/VerifyTests/discussions/1366
    [Fact]
    public void Immutable()
    {
        var assemblyName = typeof(ImmutableDictionary).Assembly.GetName();
        Assert.Equal(new Version(8, 0, 0, 0), assemblyName.Version);
    }

    [Fact]
    public void Management()
    {
        var assemblyName = typeof(ManagementQuery).Assembly.GetName();
        Assert.Equal(new Version(8, 0, 0, 0), assemblyName.Version);
    }
}

#endif