using System;
using Xunit;
using Xunit.Abstractions;

public class EnvironmentExTest :
    XunitContextBase
{
    [Fact]
    public void NotFound()
    {
        Assert.Null(EnvironmentEx.GetEnvironmentVariable("Foo"));
        Assert.Null(EnvironmentEx.GetEnvironmentVariable("Foo.Bar"));
        Assert.Null(EnvironmentEx.GetEnvironmentVariable("Foo_Bar"));
    }

    [Fact]
    public void Found()
    {
        Environment.SetEnvironmentVariable("AB", "Value1");
        Environment.SetEnvironmentVariable("A.B", "Value2");
        Environment.SetEnvironmentVariable("A_C", "Value3");
        Assert.Equal("Value1", EnvironmentEx.GetEnvironmentVariable("AB"));
        Assert.Equal("Value2", EnvironmentEx.GetEnvironmentVariable("A_B"));
        Assert.Equal("Value3", EnvironmentEx.GetEnvironmentVariable("A_C"));
    }

    public EnvironmentExTest(ITestOutputHelper output) :
        base(output)
    {
    }
}