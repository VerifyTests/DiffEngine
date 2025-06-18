public class BuildServerDetectorTest(ITestOutputHelper output) :
    XunitContextBase(output)
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
}