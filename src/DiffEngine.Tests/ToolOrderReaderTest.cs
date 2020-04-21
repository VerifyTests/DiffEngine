using System;
using System.Linq;
using DiffEngine;
using Xunit;
using Xunit.Abstractions;

public class ToolOrderReaderTest :
    XunitContextBase
{
    [Fact]
    public void ParseEnvironmentVariable()
    {
        var diffTools = ToolOrderReader.ParseEnvironmentVariable("VisualStudio,Meld").ToList();
        Assert.Equal(DiffTool.VisualStudio, diffTools[0]);
        Assert.Equal(DiffTool.Meld, diffTools[1]);
    }

    [Fact]
    public void BadEnvironmentVariable()
    {
        var exception = Assert.Throws<Exception>(() => ToolOrderReader.ParseEnvironmentVariable("Foo").ToList());
        Assert.Equal("Unable to parse tool from `DiffEngine.DiffToolOrder` environment variable: Foo", exception.Message);
    }

    public ToolOrderReaderTest(ITestOutputHelper output) : base(output)
    {
    }
}