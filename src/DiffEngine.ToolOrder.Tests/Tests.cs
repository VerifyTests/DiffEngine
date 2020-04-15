#if DEBUG
using System.Linq;
using DiffEngine;
using Xunit;

public class Tests
{
    [Fact]
    public void ChangeOrder()
    {
        #region UseOrder
        DiffTools.UseOrder(DiffTool.VisualStudio, DiffTool.AraxisMerge);
        #endregion
        Assert.Equal(DiffTool.VisualStudio, DiffTools.TextDiffTools.First().Tool);
        // Assert.Equal(DiffTool.VisualStudio, DiffTools.ExtensionLookup["jpg"].Tool);
        Assert.Equal(DiffTool.VisualStudio, DiffTools.ResolvedDiffTools.First().Tool);
    }
}
#endif