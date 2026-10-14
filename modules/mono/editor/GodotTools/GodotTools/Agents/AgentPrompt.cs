using System.Text;

namespace GodotTools.Agents
{
    /// <summary>
    /// Assembles the prompt sent to an agent CLI: task plus optional context
    /// sections. Pure string logic (BCL only) so it can be unit-tested outside
    /// the engine. Sizes are capped per section and in total: prompts travel
    /// on argv, which has OS command-line limits (~32k on Windows).
    /// </summary>
    public static class AgentPrompt
    {
        public const int MaxScriptChars = 8000;
        public const int MaxSelectionChars = 2000;
        public const int MaxSceneChars = 6000;
        public const int MaxGitChars = 2000;
        public const int MaxTotalChars = 24000;

        public static string Build(string task, string? scriptPath = null, string? scriptText = null,
            string? selection = null, int selectionLine = -1, string? scenePath = null,
            string? sceneDump = null, string? gitStatus = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are helping with a Godot 4 C# game project (C# only, no GDScript).");
            sb.AppendLine("Work in the project directory. You may read, edit, and create files;");
            sb.AppendLine("you may run dotnet build/test. When finished, print a short summary of what you changed.");
            sb.AppendLine();
            sb.AppendLine("TASK:");
            sb.AppendLine(task.Trim());
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(scriptPath) || !string.IsNullOrWhiteSpace(scriptText))
            {
                sb.AppendLine("OPEN SCRIPT: " + (scriptPath ?? "(none)"));
                sb.AppendLine(Truncate(scriptText ?? "(none)", MaxScriptChars));
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(selection))
            {
                string where = selectionLine >= 0 ? $" (line {selectionLine + 1})" : "";
                sb.AppendLine("SELECTION" + where + ":");
                sb.AppendLine(Truncate(selection, MaxSelectionChars));
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(sceneDump))
            {
                sb.AppendLine("SCENE: " + (scenePath ?? "(unsaved)"));
                sb.AppendLine(Truncate(sceneDump, MaxSceneChars));
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(gitStatus))
            {
                sb.AppendLine("GIT STATUS:");
                sb.AppendLine(Truncate(gitStatus, MaxGitChars));
                sb.AppendLine();
            }

            string prompt = sb.ToString();
            if (prompt.Length > MaxTotalChars)
                prompt = prompt.Substring(0, MaxTotalChars) + "\n[...prompt truncated to size cap...]";
            return prompt;
        }

        public static string Truncate(string text, int maxChars)
        {
            if (text.Length <= maxChars)
                return text;
            return text.Substring(0, maxChars) + $"\n[...truncated, {text.Length - maxChars} more chars...]";
        }
    }
}
