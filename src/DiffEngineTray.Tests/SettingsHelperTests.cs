using System.IO;
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using Xunit.Abstractions;

[UsesVerify]
public class SettingsHelperTests :
    XunitContextBase
{
    [Fact]
    public async Task ReadWrite()
    {
        var tempFile = "ReadWrite.txt";
        File.Delete(tempFile);
        try
        {
            SettingsHelper.FilePath = tempFile;
            await SettingsHelper.Write(
                new Settings
                {
                    AcceptAllHotKey = new HotKey
                    {
                        Key = "T"
                    }
                });
            var result = await SettingsHelper.Read();
            await Verifier.Verify(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    public SettingsHelperTests(ITestOutputHelper output) :
        base(output)
    {
    }
}