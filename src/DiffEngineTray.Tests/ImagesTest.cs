public class ImagesTest :
    XunitContextBase
{
    [Fact]
    public void AllImagesLoaded()
    {
        // Verify all icon resources load correctly
        Assert.NotNull(Images.Active);
        Assert.NotNull(Images.Default);

        // Verify all image resources load correctly
        Assert.NotNull(Images.Exit);
        Assert.NotNull(Images.Delete);
        Assert.NotNull(Images.AcceptAll);
        Assert.NotNull(Images.Accept);
        Assert.NotNull(Images.Discard);
        Assert.NotNull(Images.VisualStudio);
        Assert.NotNull(Images.Folder);
        Assert.NotNull(Images.Options);
        Assert.NotNull(Images.Link);
    }

    [Fact]
    public void IconsHaveValidSize()
    {
        Assert.True(Images.Active.Width > 0);
        Assert.True(Images.Active.Height > 0);
        Assert.True(Images.Default.Width > 0);
        Assert.True(Images.Default.Height > 0);
    }

    [Fact]
    public void ImagesHaveValidSize()
    {
        Assert.True(Images.Exit.Width > 0);
        Assert.True(Images.Exit.Height > 0);
        Assert.True(Images.Delete.Width > 0);
        Assert.True(Images.Delete.Height > 0);
        Assert.True(Images.AcceptAll.Width > 0);
        Assert.True(Images.AcceptAll.Height > 0);
        Assert.True(Images.Accept.Width > 0);
        Assert.True(Images.Accept.Height > 0);
        Assert.True(Images.Discard.Width > 0);
        Assert.True(Images.Discard.Height > 0);
        Assert.True(Images.VisualStudio.Width > 0);
        Assert.True(Images.VisualStudio.Height > 0);
        Assert.True(Images.Folder.Width > 0);
        Assert.True(Images.Folder.Height > 0);
        Assert.True(Images.Options.Width > 0);
        Assert.True(Images.Options.Height > 0);
        Assert.True(Images.Link.Width > 0);
        Assert.True(Images.Link.Height > 0);
    }

    public ImagesTest(ITestOutputHelper output) :
        base(output)
    {
    }
}
