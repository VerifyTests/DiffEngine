public class ImagesTest
{
    [Test]
    public async Task AllImagesLoaded()
    {
        // Verify all icon resources load correctly
        await Assert.That(Images.Active).IsNotNull();
        await Assert.That(Images.Default).IsNotNull();

        // Verify all image resources load correctly
        await Assert.That(Images.Exit).IsNotNull();
        await Assert.That(Images.Delete).IsNotNull();
        await Assert.That(Images.AcceptAll).IsNotNull();
        await Assert.That(Images.Accept).IsNotNull();
        await Assert.That(Images.Discard).IsNotNull();
        await Assert.That(Images.VisualStudio).IsNotNull();
        await Assert.That(Images.Folder).IsNotNull();
        await Assert.That(Images.Options).IsNotNull();
        await Assert.That(Images.Link).IsNotNull();
    }

    [Test]
    public async Task IconsHaveValidSize()
    {
        await Assert.That(Images.Active.Width > 0).IsTrue();
        await Assert.That(Images.Active.Height > 0).IsTrue();
        await Assert.That(Images.Default.Width > 0).IsTrue();
        await Assert.That(Images.Default.Height > 0).IsTrue();
    }

    [Test]
    public async Task ImagesHaveValidSize()
    {
        await Assert.That(Images.Exit.Width > 0).IsTrue();
        await Assert.That(Images.Exit.Height > 0).IsTrue();
        await Assert.That(Images.Delete.Width > 0).IsTrue();
        await Assert.That(Images.Delete.Height > 0).IsTrue();
        await Assert.That(Images.AcceptAll.Width > 0).IsTrue();
        await Assert.That(Images.AcceptAll.Height > 0).IsTrue();
        await Assert.That(Images.Accept.Width > 0).IsTrue();
        await Assert.That(Images.Accept.Height > 0).IsTrue();
        await Assert.That(Images.Discard.Width > 0).IsTrue();
        await Assert.That(Images.Discard.Height > 0).IsTrue();
        await Assert.That(Images.VisualStudio.Width > 0).IsTrue();
        await Assert.That(Images.VisualStudio.Height > 0).IsTrue();
        await Assert.That(Images.Folder.Width > 0).IsTrue();
        await Assert.That(Images.Folder.Height > 0).IsTrue();
        await Assert.That(Images.Options.Width > 0).IsTrue();
        await Assert.That(Images.Options.Height > 0).IsTrue();
        await Assert.That(Images.Link.Width > 0).IsTrue();
        await Assert.That(Images.Link.Height > 0).IsTrue();
    }
}
