namespace DiffEngine;

public static class AiCliDetector
{
    static AiCliDetector()
    {
        var variables = Environment.GetEnvironmentVariables();

        // GitHub Copilot CLI
        // https://docs.github.com/en/copilot/using-github-copilot/using-github-copilot-in-the-command-line
        IsCopilotCli = variables.Contains("GITHUB_COPILOT_CLI");

        // Aider
        // https://aider.chat/docs/config/dotenv.html
        IsAider = variables.Contains("AIDER_GIT_DNAME") || variables.Contains("AIDER");

        // Claude Code
        // https://docs.anthropic.com/en/docs/build-with-claude/claude-cli
        IsClaudeCode = variables.Contains("CLAUDECODE") || variables.Contains("CLAUDE_CODE_ENTRYPOINT");

        Detected = IsCopilotCli ||
                   IsAider ||
                   IsClaudeCode;
    }

    public static bool IsCopilotCli { get; }

    public static bool IsAider { get; }

    public static bool IsClaudeCode { get; }

    public static bool Detected { get; set; }
}
