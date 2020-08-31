using System;
// ReSharper disable CommentTypo

namespace DiffEngine
{
    public static class BuildServerDetector
    {
        static BuildServerDetector()
        {
            // Appveyor
            // https://www.appveyor.com/docs/environment-variables/
            // Travis
            // https://docs.travis-ci.com/user/environment-variables/#default-environment-variables
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
            {
                Detected = true;
                return;
            }

            // Jenkins
            // https://wiki.jenkins.io/display/JENKINS/Building+a+software+project#Buildingasoftwareproject-belowJenkinsSetEnvironmentVariables
            if (Environment.GetEnvironmentVariable("JENKINS_URL") != null)
            {
                Detected = true;
                return;
            }

            // GitHub Action
            // https://help.github.com/en/actions/automating-your-workflow-with-github-actions/using-environment-variables#default-environment-variables
            if (Environment.GetEnvironmentVariable("GITHUB_ACTION") != null)
            {
                Detected = true;
                return;
            }

            // AzureDevops
            // https://docs.microsoft.com/en-us/azure/devops/pipelines/build/variables?view=azure-devops&tabs=yaml#agent-variables
            // Variable name is 'Agent.Id' but must be referenced with an underscore
            // https://docs.microsoft.com/en-us/azure/devops/pipelines/release/variables?view=azure-devops&tabs=powershell#using-default-variables
            if (Environment.GetEnvironmentVariable("Agent_Id") != null)
            {
                Detected = true;
                return;
            }

            // TeamCity
            // https://www.jetbrains.com/help/teamcity/predefined-build-parameters.html#PredefinedBuildParameters-ServerBuildProperties
            if (Environment.GetEnvironmentVariable("TEAMCITY_VERSION") != null)
            {
                Detected = true;
                return;
            }

            // MyGet
            // https://docs.myget.org/docs/reference/build-services#Available_Environment_Variables
            if (string.Equals(Environment.GetEnvironmentVariable("BuildRunner"), "MyGet", StringComparison.OrdinalIgnoreCase))
            {
                Detected = true;
                return;
            }

            // GitLab
            // https://docs.gitlab.com/ee/ci/variables/predefined_variables.html
            if (Environment.GetEnvironmentVariable("GITLAB_CI") != null)
            {
                Detected = true;
                return;
            }

            // Bamboo
            // https://confluence.atlassian.com/bamboo/bamboo-variables-289277087.html
            // Variable name is 'bamboo.buildKey' but must be referenced with an underscore
            if (Environment.GetEnvironmentVariable("bamboo_buildKey") != null)
            {
                Detected = true;
                return;
            }
        }

        public static bool Detected { get; set; }
    }
}