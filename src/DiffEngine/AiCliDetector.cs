namespace DiffEngine;

public static class AiCliDetector
{
    static AiCliDetector()
    {
        var variables = Environment.GetEnvironmentVariables();

        // GitHub Copilot
        // https://docs.github.com/en/copilot/using-github-copilot/using-github-copilot-in-the-command-line
        IsCopilot = variables.Contains("GITHUB_COPILOT_RUNTIME");

        // Aider
        // https://aider.chat/docs/config/dotenv.html
        IsAider = variables.Contains("AIDER_GIT_DNAME") || variables.Contains("AIDER");

        // Claude Code
        // https://docs.anthropic.com/en/docs/build-with-claude/claude-cli
        IsClaudeCode = variables.Contains("CLAUDE_CODE") || variables.Contains("ANTHROPIC_CLI");

        Detected = IsCopilot ||
                   IsAider ||
                   IsClaudeCode;
    }

    public static bool IsCopilot { get; }

    public static bool IsAider { get; }

    public static bool IsClaudeCode { get; }

    public static bool Detected { get; set; }
}
