using System.Collections.Generic;

namespace GodotTools.Agents
{
    /// <summary>
    /// One supported agent CLI. Flags confirmed at implementation time:
    /// muse (muse exec --help), claude/codex/gemini (vendor docs).
    /// Prompts go on argv for all four; AgentPrompt caps the total size.
    /// </summary>
    public sealed record AgentProvider(string Id, string DisplayName, string CliName)
    {
        public string[] BuildArgs(string prompt) => Id switch
        {
            "muse" => new[] { "exec", prompt },
            "claude" => new[] { "-p", prompt },
            "codex" => new[] { "exec", prompt },
            "gemini" => new[] { "-p", prompt },
            _ => new[] { prompt },
        };

        public static readonly IReadOnlyList<AgentProvider> All = new List<AgentProvider>
        {
            new AgentProvider("muse", "Muse", "muse"),
            new AgentProvider("claude", "Claude Code", "claude"),
            new AgentProvider("codex", "Codex", "codex"),
            new AgentProvider("gemini", "Gemini", "gemini"),
        };
    }
}
