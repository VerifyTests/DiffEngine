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
        IsClaudeCode = variables.Contains("CLAUDECODE") || variables.Contains("CLAUDE_CODE_ENTRYPOINT");

        // Cursor
        // https://cursor.com/docs/agent/terminal
        IsCursor = variables.Contains("CURSOR_AGENT");

        // Gemini CLI
        // https://google-gemini.github.io/gemini-cli/docs/tools/shell.html
        IsGeminiCli = variables.Contains("GEMINI_CLI");

        // OpenAI Codex CLI
        IsCodex = variables.Contains("CODEX_SANDBOX");

        // Amazon Q Developer CLI
        // https://docs.aws.amazon.com/amazonq/latest/qdeveloper-ug/command-line.html
        IsAmazonQ = variables.Contains("Q_TERM");

        // OpenCode
        IsOpenCode = variables.Contains("OPENCODE_CLIENT");

        // Cline
        IsCline = variables.Contains("CLINE_ACTIVE");

        // Augment Code
        IsAugment = variables.Contains("AUGMENT_AGENT");

        // TRAE AI
        IsTraeAi = variables.Contains("TRAE_AI_SHELL_ID");

        // Goose / Amp share the generic AGENT variable, distinguished by value
        var agent = Environment.GetEnvironmentVariable("AGENT");
        IsGoose = string.Equals(agent, "goose", StringComparison.OrdinalIgnoreCase);
        IsAmp = string.Equals(agent, "amp", StringComparison.OrdinalIgnoreCase);

        Detected = IsCopilot ||
                   IsAider ||
                   IsClaudeCode ||
                   IsCursor ||
                   IsGeminiCli ||
                   IsCodex ||
                   IsAmazonQ ||
                   IsOpenCode ||
                   IsCline ||
                   IsAugment ||
                   IsTraeAi ||
                   IsGoose ||
                   IsAmp;
    }

    public static bool IsCursor { get; set; }

    public static bool IsCopilot { get; }

    public static bool IsAider { get; }

    public static bool IsClaudeCode { get; }

    public static bool IsGeminiCli { get; }

    public static bool IsCodex { get; }

    public static bool IsAmazonQ { get; }

    public static bool IsOpenCode { get; }

    public static bool IsCline { get; }

    public static bool IsAugment { get; }

    public static bool IsTraeAi { get; }

    public static bool IsGoose { get; }

    public static bool IsAmp { get; }

    public static bool Detected { get; set; }
}
