using System.IO;
using System.Threading.Tasks;
using VerifyTests;
using VerifyXunit;
using Xunit;
using Xunit.Abstractions;

[UsesVerify]
public class MenuBuilderTest :
    XunitContextBase
{
    [Fact]
    public async Task Empty()
    {
        await using var tracker = new RecordingTracker();
        var menu = MenuBuilder.Build(() => { }, tracker);
        await Verifier.Verify(menu, settings);
    }

    [Fact]
    public async Task Full()
    {
        await using var tracker = new RecordingTracker();
        tracker.AddDelete(file1);
        tracker.AddMove(file2, file2, true, null, null);
        var menu = MenuBuilder.Build(() => { }, tracker);
        await Verifier.Verify(menu, settings);
    }

    public MenuBuilderTest(ITestOutputHelper output) :
        base(output)
    {
        settings = new VerifySettings();
        settings.AutoVerify();
        file1 = "file1.txt";
        file2 = "file2.txt";
        File.WriteAllText(file1, "");
        File.WriteAllText(file2, "");
    }

    public override void Dispose()
    {
        File.Delete(file1);
        File.Delete(file2);
        base.Dispose();
    }

    string file1;
    string file2;
    VerifySettings settings;
}