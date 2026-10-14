using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using static GodotTools.Internals.Globals;

namespace GodotTools.GitHub
{
    /// <summary>
    /// Captured result of one git invocation.
    /// </summary>
    public sealed record GitResult(int ExitCode, string Stdout, string Stderr)
    {
        public bool Ok => ExitCode == 0;
    }

    /// <summary>
    /// One entry of `git status --porcelain`.
    /// </summary>
    public sealed record GitFileStatus(string Staged, string Unstaged, string Path, string? OrigPath)
    {
        public bool IsUntracked => Staged == "?" && Unstaged == "?";
        public bool IsUnmerged => "UD".Contains(Staged) && "UD".Contains(Unstaged) ||
            (Staged == "A" && Unstaged == "A") || (Staged == "U" || Unstaged == "U");

        public string Describe()
        {
            if (IsUntracked)
                return "untracked";
            if (IsUnmerged)
                return "conflict";
            if (Staged != " " && Staged != "?")
                return "staged";
            return "modified";
        }
    }

    public sealed record GitStatus(
        IReadOnlyList<GitFileStatus> Files,
        string Branch,
        string? Upstream,
        int Ahead,
        int Behind);

    /// <summary>
    /// Thin wrapper over the git CLI. No libgit dependency: git must be on
    /// PATH (or pointed at by the crete/github/git_path editor setting).
    /// All commands run with GIT_TERMINAL_PROMPT=0 so pushes/pulls fail fast
    /// instead of hanging on credential prompts.
    /// </summary>
    public static class GitCli
    {
        public const string GitPathSetting = "crete/github/git_path";

        public static string GetConfiguredGitPath()
        {
            // NOTE: must be EditorDef, not SetInitialValue(..., update_current: false):
            // the latter leaves no current value, so GetSetting comes back empty.
            return EditorDef(GitPathSetting, "git").AsString();
        }

        public static string? FindGit()
        {
            string configured = GetConfiguredGitPath();
            if (string.IsNullOrWhiteSpace(configured) || configured == "git")
                return GodotTools.Utils.OS.PathWhich("git");
            return System.IO.File.Exists(configured) ? configured : null;
        }

        public static GitResult Run(string workingDir, params string[] args)
        {
            string? git = FindGit();
            if (string.IsNullOrEmpty(git))
                return new GitResult(127, "", "git executable not found");

            var startInfo = new ProcessStartInfo(git)
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.Environment["LC_ALL"] = "C";
            foreach (string arg in args)
                startInfo.ArgumentList.Add(arg);

            try
            {
                using Process? process = Process.Start(startInfo);
                if (process == null)
                    return new GitResult(127, "", "git process failed to start");
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return new GitResult(process.ExitCode, stdout, stderr);
            }
            catch (Exception e)
            {
                return new GitResult(127, "", e.Message);
            }
        }

        public static bool IsRepository(string workingDir)
        {
            GitResult r = Run(workingDir, "rev-parse", "--is-inside-work-tree");
            return r.Ok && r.Stdout.Trim() == "true";
        }

        public static GitStatus? GetStatus(string workingDir)
        {
            GitResult r = Run(workingDir, "status", "--porcelain=v1", "--branch",
                "--untracked-files=normal", "--no-renames");
            if (!r.Ok)
                return null;

            string branch = "";
            string? upstream = null;
            int ahead = 0;
            int behind = 0;
            var files = new List<GitFileStatus>();

            foreach (string rawLine in r.Stdout.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.StartsWith("## "))
                {
                    ParseBranchLine(line.Substring(3), ref branch, ref upstream, ref ahead, ref behind);
                }
                else if (line.Length >= 4)
                {
                    string staged = line.Substring(0, 1);
                    string unstaged = line.Substring(1, 1);
                    string path = line.Substring(3);
                    string? orig = null;
                    int arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
                    if (arrow >= 0)
                    {
                        orig = path.Substring(0, arrow);
                        path = path.Substring(arrow + 4);
                    }
                    files.Add(new GitFileStatus(staged, unstaged,
                        Unquote(path), orig != null ? Unquote(orig) : null));
                }
            }

            return new GitStatus(files, branch, upstream, ahead, behind);
        }

        private static void ParseBranchLine(string header, ref string branch,
            ref string? upstream, ref int ahead, ref int behind)
        {
            // Forms: "main...origin/main [ahead 2, behind 1]", "main",
            // "No commits yet on main", "HEAD (no branch)".
            const string noCommits = "No commits yet on ";
            if (header.StartsWith(noCommits, StringComparison.Ordinal))
            {
                branch = header.Substring(noCommits.Length);
                return;
            }
            if (header.StartsWith("HEAD ", StringComparison.Ordinal))
            {
                branch = "(detached)";
                return;
            }

            int ellipsis = header.IndexOf("...", StringComparison.Ordinal);
            if (ellipsis < 0)
            {
                branch = header;
                return;
            }

            branch = header.Substring(0, ellipsis);
            string rest = header.Substring(ellipsis + 3);
            int bracket = rest.IndexOf(" [", StringComparison.Ordinal);
            if (bracket >= 0)
            {
                upstream = rest.Substring(0, bracket);
                string counts = rest.Substring(bracket + 2).TrimEnd(']');
                foreach (string part in counts.Split(','))
                {
                    string p = part.Trim();
                    if (p.StartsWith("ahead ", StringComparison.Ordinal))
                        int.TryParse(p.Substring(6), out ahead);
                    else if (p.StartsWith("behind ", StringComparison.Ordinal))
                        int.TryParse(p.Substring(7), out behind);
                }
            }
            else
            {
                upstream = rest;
            }
        }

        private static string Unquote(string path)
        {
            // C-quoted when it holds UTF-8/special chars; strip the quotes.
            if (path.Length >= 2 && path.StartsWith("\"") && path.EndsWith("\""))
                return path.Substring(1, path.Length - 2);
            return path;
        }

        public static IReadOnlyList<string> GetBranches(string workingDir, out string current)
        {
            current = "";
            var branches = new List<string>();
            GitResult r = Run(workingDir, "branch", "--format=%(refname:short)");
            if (r.Ok)
            {
                foreach (string rawLine in r.Stdout.Split('\n'))
                {
                    string name = rawLine.Trim();
                    if (name.Length > 0)
                        branches.Add(name);
                }
            }
            GitResult head = Run(workingDir, "branch", "--show-current");
            if (head.Ok)
                current = head.Stdout.Trim();
            return branches;
        }

        public static string GetDiff(string workingDir, GitFileStatus file)
        {
            if (file.IsUntracked)
            {
                try
                {
                    string full = Path.Combine(workingDir, file.Path);
                    return $"(new file: {file.Path})\n\n" + System.IO.File.ReadAllText(full);
                }
                catch (Exception e)
                {
                    return $"(new file: {file.Path}, unreadable: {e.Message})";
                }
            }
            GitResult r = Run(workingDir, "diff", "--no-color", "HEAD", "--", file.Path);
            if (!r.Ok || r.Stdout.Length == 0)
                return $"(no diff available for {file.Path})";
            return r.Stdout;
        }

        public static GitResult StageAll(string workingDir) =>
            Run(workingDir, "add", "-A");

        public static GitResult Commit(string workingDir, string message) =>
            Run(workingDir, "commit", "-m", message);

        public static GitResult Push(string workingDir) =>
            Run(workingDir, "push");

        public static GitResult Pull(string workingDir) =>
            Run(workingDir, "pull", "--ff-only");

        public static GitResult SwitchBranch(string workingDir, string branch) =>
            Run(workingDir, "switch", branch);

        public static GitResult CreateBranch(string workingDir, string branch) =>
            Run(workingDir, "switch", "-c", branch);

        public static GitResult MergeBranch(string workingDir, string branch) =>
            Run(workingDir, "merge", "--no-edit", branch);

        public static GitResult RevertFiles(string workingDir, IReadOnlyList<GitFileStatus> files)
        {
            var tracked = new List<string>();
            var untracked = new List<string>();
            foreach (GitFileStatus f in files)
            {
                if (f.IsUntracked)
                    untracked.Add(f.Path);
                else
                    tracked.Add(f.Path);
            }

            if (untracked.Count > 0)
            {
                var args = new List<string> { "clean", "-f", "--" };
                args.AddRange(untracked);
                GitResult cleaned = Run(workingDir, args.ToArray());
                if (!cleaned.Ok)
                    return cleaned;
            }
            if (tracked.Count > 0)
            {
                var args = new List<string> { "checkout", "--" };
                args.AddRange(tracked);
                return Run(workingDir, args.ToArray());
            }
            return new GitResult(0, "", "");
        }
    }
}
