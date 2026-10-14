using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GodotTools.Agents
{
    public sealed record AgentFileChange(string RelativePath, string Kind); // Kind: added|modified|deleted

    /// <summary>
    /// Snapshots project text files by hash so the dock can report what an
    /// agent run changed, with or without git. BCL only (unit-testable).
    /// </summary>
    public static class AgentChangeTracker
    {
        // Dirs never worth snapshotting (caches, build output, VCS metadata).
        public static readonly IReadOnlyList<string> SkippedDirs = new List<string>
        {
            ".git", ".godot", ".vs", "bin", "obj", ".mono", "Library", "Temp",
        };

        // Extensions that count as project files. Everything else is ignored so
        // a run that regenerates .import files or screenshots stays quiet.
        public static readonly IReadOnlyList<string> TrackedExtensions = new List<string>
        {
            ".cs", ".csproj", ".sln", ".tscn", ".scn", ".godot", ".cfg", ".json",
            ".md", ".txt", ".gdignore", ".import",
        };

        public static Dictionary<string, string> Snapshot(string rootDir)
        {
            var snap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var root = new DirectoryInfo(rootDir);
            if (!root.Exists)
                return snap;

            foreach (FileInfo file in EnumerateTracked(root, rootDir))
            {
                try
                {
                    snap[Relative(rootDir, file.FullName)] = HashFile(file.FullName);
                }
                catch (Exception)
                {
                    // Unreadable mid-run (agent rewriting it); skip.
                }
            }
            return snap;
        }

        public static IReadOnlyList<AgentFileChange> Diff(
            Dictionary<string, string> before, Dictionary<string, string> after)
        {
            var changes = new List<AgentFileChange>();
            foreach (var kv in after)
            {
                if (!before.TryGetValue(kv.Key, out string? oldHash))
                    changes.Add(new AgentFileChange(kv.Key, "added"));
                else if (!string.Equals(oldHash, kv.Value, StringComparison.Ordinal))
                    changes.Add(new AgentFileChange(kv.Key, "modified"));
            }
            foreach (var kv in before)
            {
                if (!after.ContainsKey(kv.Key))
                    changes.Add(new AgentFileChange(kv.Key, "deleted"));
            }
            changes.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
            return changes;
        }

        private static IEnumerable<FileInfo> EnumerateTracked(DirectoryInfo root, string rootDir)
        {
            var stack = new Stack<DirectoryInfo>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                DirectoryInfo dir = stack.Pop();
                DirectoryInfo[] subdirs;
                FileInfo[] files;
                try
                {
                    subdirs = dir.GetDirectories();
                    files = dir.GetFiles();
                }
                catch (Exception)
                {
                    continue;
                }
                foreach (DirectoryInfo sub in subdirs)
                {
                    if ((sub.Attributes & FileAttributes.Hidden) != 0)
                        continue;
                    if (SkippedDirs.Contains(sub.Name, StringComparer.OrdinalIgnoreCase))
                        continue;
                    stack.Push(sub);
                }
                foreach (FileInfo file in files)
                {
                    if ((file.Attributes & FileAttributes.Hidden) != 0)
                        continue;
                    if (TrackedExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                        yield return file;
                }
            }
        }

        private static string Relative(string rootDir, string fullPath)
        {
            string rel = Path.GetRelativePath(rootDir, fullPath);
            return rel.Replace(Path.DirectorySeparatorChar, '/');
        }

        private static string HashFile(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            byte[] hash = sha.ComputeHash(stream);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
