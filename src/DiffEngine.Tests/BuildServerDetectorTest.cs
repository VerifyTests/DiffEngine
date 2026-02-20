public class BuildServerDetectorTest
{
    [Test]
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

    [Test]
    public async Task SetDetectedPersistsInAsyncContext()
    {
        var original = BuildServerDetector.Detected;
        try
        {
            BuildServerDetector.Detected = true;
            await Assert.That(BuildServerDetector.Detected).IsTrue();

            await Task.Delay(1);

            await Assert.That(BuildServerDetector.Detected).IsTrue();
        }
        finally
        {
            BuildServerDetector.Detected = original;
        }
    }

    [Test]
    public async Task SetDetectedDoesNotLeakToOtherContexts()
    {
        var parentValue = BuildServerDetector.Detected;

        await Task.Run(async () =>
        {
            BuildServerDetector.Detected = true;
            await Assert.That(BuildServerDetector.Detected).IsTrue();
        });

        await Assert.That(BuildServerDetector.Detected).IsEqualTo(parentValue);
    }

    #endregion
}
