public class TrayVersionFileTest
{
    [Test]
    public async Task RoundTrip()
    {
        TrayVersionFile.Write("20.1.3+abc123");
        try
        {
            var read = TrayVersionFile.TryRead(out var version);
            await Assert.That(read).IsTrue();
            await Assert.That(version).IsEqualTo(new(20, 1, 3));
        }
        finally
        {
            TrayVersionFile.Delete();
        }
    }

    [Test]
    public async Task PrereleaseSuffixStripped()
    {
        TrayVersionFile.Write("21.0.0-beta.1");
        try
        {
            var read = TrayVersionFile.TryRead(out var version);
            await Assert.That(read).IsTrue();
            await Assert.That(version).IsEqualTo(new(21, 0, 0));
        }
        finally
        {
            TrayVersionFile.Delete();
        }
    }

    [Test]
    public async Task MissingFileFails()
    {
        TrayVersionFile.Delete();
        var read = TrayVersionFile.TryRead(out _);
        await Assert.That(read).IsFalse();
    }

    [Test]
    public async Task GarbageFails()
    {
        TrayVersionFile.Write("garbage");
        try
        {
            var read = TrayVersionFile.TryRead(out _);
            await Assert.That(read).IsFalse();
        }
        finally
        {
            TrayVersionFile.Delete();
        }
    }
}
