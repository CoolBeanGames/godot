using Godot;
using System;
using System.Text;

namespace GodotTools.Agents
{
    public sealed record AgentEditorContext(
        string? ScriptPath, string? ScriptText,
        string? Selection, int SelectionLine,
        string? ScenePath, string? SceneDump,
        string? GitStatus);

    /// <summary>
    /// Gathers editor context for an agent run: open script + selection,
    /// edited-scene tree dump, git status. Each section is best-effort and
    /// independent: one failing section never blocks the others.
    /// NOTE: build-error context is deliberately absent — the problems list
    /// lives in a private Tree view with no accessor, and the agent runs in
    /// the project dir with shell access, so it can run dotnet build itself.
    /// </summary>
    public static class AgentContext
    {
        private const int MaxSceneNodes = 500;
        private const int MaxSceneDepth = 40;
        private const int MaxGitLines = 30;

        public static AgentEditorContext Gather(bool wantScript, bool wantScene, bool wantGit)
        {
            string? scriptPath = null;
            string? scriptText = null;
            string? selection = null;
            int selectionLine = -1;
            string? scenePath = null;
            string? sceneDump = null;
            string? gitStatus = null;

            if (wantScript)
            {
                try
                {
                    (scriptPath, scriptText, selection, selectionLine) = GatherScript();
                }
                catch (Exception e)
                {
                    GD.PushError("Agents dock: script context failed: " + e.Message);
                }
            }

            if (wantScene)
            {
                try
                {
                    (scenePath, sceneDump) = GatherScene();
                }
                catch (Exception e)
                {
                    GD.PushError("Agents dock: scene context failed: " + e.Message);
                }
            }

            if (wantGit)
            {
                try
                {
                    gitStatus = GatherGit();
                }
                catch (Exception e)
                {
                    GD.PushError("Agents dock: git context failed: " + e.Message);
                }
            }

            return new AgentEditorContext(scriptPath, scriptText, selection, selectionLine,
                scenePath, sceneDump, gitStatus);
        }

        private static (string?, string?, string?, int) GatherScript()
        {
            ScriptEditor? se = EditorInterface.Singleton.GetScriptEditor();
            Script? script = se?.GetCurrentScript();
            if (script == null)
                return (null, null, null, -1);

            string? text = null;
            string? sel = null;
            int line = -1;
            if (se?.GetCurrentEditor()?.GetBaseEditor() is TextEdit textEdit)
            {
                text = textEdit.GetText();
                if (textEdit.HasSelection())
                {
                    sel = textEdit.GetSelectedText();
                    line = textEdit.GetCaretLine();
                }
            }
            return (script.ResourcePath, text, sel, line);
        }

        private static (string?, string?) GatherScene()
        {
            Node? root = EditorInterface.Singleton.GetEditedSceneRoot();
            if (root == null)
                return (null, null);

            var sb = new StringBuilder();
            int count = 0;
            WalkScene(root, 0, sb, ref count);
            string? path = string.IsNullOrEmpty(root.SceneFilePath) ? null : root.SceneFilePath;
            return (path, sb.ToString());
        }

        private static void WalkScene(Node node, int depth, StringBuilder sb, ref int count)
        {
            if (count >= MaxSceneNodes || depth > MaxSceneDepth)
            {
                if (count == MaxSceneNodes)
                {
                    sb.AppendLine("... (scene dump capped)");
                    count++;
                }
                return;
            }
            count++;

            sb.Append(' ', depth * 2);
            sb.Append(node.GetClass());
            sb.Append(" \"");
            sb.Append(node.Name);
            sb.Append('"');
            if (node.GetScript().AsGodotObject() is Script attached)
            {
                sb.Append(" [script: ");
                sb.Append(attached.ResourcePath);
                sb.Append(']');
            }
            sb.AppendLine();

            foreach (Node child in node.GetChildren())
                WalkScene(child, depth + 1, sb, ref count);
        }

        private static string? GatherGit()
        {
            string dir = ProjectSettings.GlobalizePath("res://");
            if (!GitHub.GitCli.IsRepository(dir))
                return "(not a git repository)";
            GitHub.GitResult r = GitHub.GitCli.Run(dir, "status", "--porcelain=v1", "--branch");
            if (!r.Ok)
                return null;
            string[] lines = r.Stdout.Split('\n');
            if (lines.Length <= MaxGitLines)
                return r.Stdout.TrimEnd();
            var sb = new StringBuilder();
            for (int i = 0; i < MaxGitLines; i++)
                sb.AppendLine(lines[i].TrimEnd('\r'));
            sb.Append($"[... {lines.Length - MaxGitLines} more lines ...]");
            return sb.ToString();
        }
    }
}
