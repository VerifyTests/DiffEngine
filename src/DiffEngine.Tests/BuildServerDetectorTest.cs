public class BuildServerDetectorTest
{
    [Fact]
    public void Props()
    {
        // ReSharper disable UnusedVariable

        #region BuildServerDetectorProps

        var isWsl = BuildServerDetector.IsWsl;
        var isTravis = BuildServerDetector.IsTravis;
        var isJenkins = BuildServerDetector.IsJenkins;
        var isGithubAction = BuildServerDetector.IsGithubAction;
        var isAzureDevops = BuildServerDetector.IsAzureDevops;
        var isTeamCity = BuildServerDetector.IsTeamCity;
        var isGitLab = BuildServerDetector.IsGitLab;
        var isMyGet = BuildServerDetector.IsMyGet;
        var isGoDc = BuildServerDetector.IsGoDc;
        var isDocker = BuildServerDetector.IsDocker;
        var isAppVeyor = BuildServerDetector.IsAppVeyor;

        #endregion

        // ReSharper restore UnusedVariable
    }

    #region BuildServerDetectorDetectedOverride

    [Fact]
    public async Task SetDetectedPersistsInAsyncContext()
    {
        var original = BuildServerDetector.Detected;
        try
        {
            BuildServerDetector.Detected = true;
            Assert.True(BuildServerDetector.Detected);

            await Task.Delay(1);

            Assert.True(BuildServerDetector.Detected);
        }
        finally
        {
            BuildServerDetector.Detected = original;
        }
    }

    [Fact]
    public async Task SetDetectedDoesNotLeakToOtherContexts()
    {
        var parentValue = BuildServerDetector.Detected;

        await Task.Run(() =>
        {
            BuildServerDetector.Detected = true;
            Assert.True(BuildServerDetector.Detected);
        });

        Assert.Equal(parentValue, BuildServerDetector.Detected);
    }

    #endregion
}
