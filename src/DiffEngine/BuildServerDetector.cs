// ReSharper disable CommentTypo

namespace DiffEngine;

public static class BuildServerDetector
{
    static BuildServerDetector()
    {
        var variables = Environment.GetEnvironmentVariables();
        // Jenkins
        // https://wiki.jenkins.io/display/JENKINS/Building+a+software+project#Buildingasoftwareproject-belowJenkinsSetEnvironmentVariables
        IsJenkins = variables.Contains("JENKINS_URL");

        // GitHub Action
        // https://help.github.com/en/actions/automating-your-workflow-with-github-actions/using-environment-variables#default-environment-variables
        IsGithubAction = variables.Contains("GITHUB_ACTION");

        // TeamCity
        // https://www.jetbrains.com/help/teamcity/predefined-build-parameters.html#PredefinedBuildParameters-ServerBuildProperties
        IsTeamCity = variables.Contains("TEAMCITY_VERSION");

        // MyGet
        // https://docs.myget.org/docs/reference/build-services#Available_Environment_Variables
        IsMyGet = ValueEquals(variables, "BuildRunner", "MyGet");

        // GitLab
        // https://docs.gitlab.com/ee/ci/variables/predefined_variables.html
        IsGitLab = variables.Contains("GITLAB_CI");

        // GoDC
        // https://docs.gocd.org/current/faq/dev_use_current_revision_in_build.html
        IsGoDc = variables.Contains("GO_SERVER_URL");

        // Travis
        // https://docs.travis-ci.com/user/environment-variables/#default-environment-variables
        IsTravis = variables.Contains("TRAVIS_BUILD_ID");

        // Docker
        // https://www.hanselman.com/blog/detecting-that-a-net-core-app-is-running-in-a-docker-container-and-skippablefacts-in-xunit
        IsDocker = ValueEquals(variables, "DOTNET_RUNNING_IN_CONTAINER", "true");

        // AppVeyor
        // https://www.appveyor.com/docs/environment-variables/
        IsAppVeyor = variables.Contains("APPVEYOR");

        // AzureDevops
        // https://docs.microsoft.com/en-us/azure/devops/pipelines/build/variables?view=azure-devops&tabs=yaml#agent-variables
        // Variable name is 'Agent.Id' to detect if this is a Azure Pipelines agent.
        // Note that variables are upper-cased and '.' is replaced with '_' on Azure Pipelines.
        // https://docs.microsoft.com/en-us/azure/devops/pipelines/process/variables?view=azure-devops&tabs=yaml%2Cbatch#access-variables-through-the-environment
        IsAzureDevops = !IsTravis &&
                        !IsJenkins &&
                        !IsGithubAction &&
                        !IsTeamCity &&
                        !IsGitLab &&
                        !IsMyGet &&
                        !IsGoDc &&
                        !IsDocker &&
                        !IsAppVeyor &&
                        variables.Contains("AGENT_ID");

        Detected = IsTravis ||
                   IsJenkins ||
                   IsGithubAction ||
                   IsAzureDevops ||
                   IsTeamCity ||
                   IsGitLab ||
                   IsMyGet ||
                   IsGoDc ||
                   IsDocker ||
                   IsAppVeyor;
    }

    static bool ValueEquals(IDictionary variables, string key, string value)
    {
        var variable = variables[key];
        if(variable == null)
        {
            return false;
        }

        return string.Equals((string)variable, value, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAppVeyor { get; }

    public static bool IsTravis { get; }

    public static bool IsDocker { get; }

    public static bool IsAzureDevops { get; }

    public static bool IsGitLab { get; }

    public static bool IsGoDc { get; }

    public static bool IsMyGet { get; }

    public static bool IsTeamCity { get; }

    public static bool IsGithubAction { get; }

    public static bool IsJenkins { get; }

    public static bool Detected { get; set; }
}