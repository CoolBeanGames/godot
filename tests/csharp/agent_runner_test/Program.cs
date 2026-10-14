// Standalone tests for the Agents dock pure core (no Godot types).
// Run: dotnet run --project engine/tests/csharp/agent_runner_test
// Exit code 0 = all passed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GodotTools.Agents;

static class AgentCoreTests
{
    static int s_passed = 0;
    static int s_failed = 0;

    static void Check(bool cond, string name)
    {
        if (cond)
        {
            s_passed++;
            Console.WriteLine("PASS " + name);
        }
        else
        {
            s_failed++;
            Console.WriteLine("FAIL " + name);
        }
    }

    static int Main()
    {
        TestProviderArgs();
        TestPrompt();
        TestChangeTracker();
        TestRunnerAsync().GetAwaiter().GetResult();

        Console.WriteLine($"AGENT-CORE-TESTS passed={s_passed} failed={s_failed}");
        return s_failed == 0 ? 0 : 1;
    }

    static void TestProviderArgs()
    {
        var byId = AgentProvider.All.ToDictionary(p => p.Id);
        Check(byId.Count == 4, "provider count is 4");
        Check(Args(byId["muse"]) == "exec|<prompt>", "muse args");
        Check(Args(byId["claude"]) == "-p|<prompt>", "claude args");
        Check(Args(byId["codex"]) == "exec|<prompt>", "codex args");
        Check(Args(byId["gemini"]) == "-p|<prompt>", "gemini args");
        Check(byId["claude"].CliName == "claude", "claude cli name");
    }

    static string Args(AgentProvider p) => string.Join("|", p.BuildArgs("<prompt>"));

    static void TestPrompt()
    {
        string p = AgentPrompt.Build("Fix the bug", "player.cs", "line1\nline2",
            "line2", 1, "main.tscn", "Node Main", "## main\n M player.cs");
        Check(p.Contains("TASK:") && p.Contains("Fix the bug"), "prompt task");
        Check(p.Contains("OPEN SCRIPT: player.cs") && p.Contains("line1"), "prompt script");
        Check(p.Contains("SELECTION (line 2):"), "prompt selection line");
        Check(p.Contains("SCENE: main.tscn"), "prompt scene");
        Check(p.Contains("GIT STATUS:") && p.Contains("## main"), "prompt git");

        string big = new string('x', AgentPrompt.MaxScriptChars + 100);
        string p2 = AgentPrompt.Build("t", "s.cs", big);
        Check(p2.Contains("[...truncated,"), "script truncation marker");
        Check(!p2.Contains(new string('x', AgentPrompt.MaxScriptChars + 1)), "script actually cut");

        string huge = new string('y', AgentPrompt.MaxTotalChars + 5000);
        string p3 = AgentPrompt.Build(huge);
        Check(p3.Length <= AgentPrompt.MaxTotalChars + 100, "total cap enforced");
        Check(p3.Contains("[...prompt truncated to size cap...]"), "total cap marker");
    }

    static void TestChangeTracker()
    {
        string root = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            File.WriteAllText(Path.Combine(root, "a.cs"), "v1");
            File.WriteAllText(Path.Combine(root, "del.md"), "bye");
            File.WriteAllText(Path.Combine(root, "bin", "x.cs"), "build");
            var before = AgentChangeTracker.Snapshot(root);
            Check(before.Count == 2, "snapshot counts tracked only");

            File.WriteAllText(Path.Combine(root, "a.cs"), "v2");
            File.WriteAllText(Path.Combine(root, "new.cs"), "hello");
            File.WriteAllText(Path.Combine(root, "bin", "x.cs"), "build2");
            File.WriteAllText(Path.Combine(root, "shot.png"), "binary");
            File.Delete(Path.Combine(root, "del.md"));
            var after = AgentChangeTracker.Snapshot(root);

            var changes = AgentChangeTracker.Diff(before, after);
            string summary = string.Join(";", changes.Select(c => $"{c.Kind}:{c.RelativePath}"));
            Check(summary == "modified:a.cs;deleted:del.md;added:new.cs", "diff add/mod/del: " + summary);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static async Task TestRunnerAsync()
    {
        string root = NewTempDir();
        try
        {
            // Fake provider: echoes its args, writes a file, exits 0.
            string fake = Path.Combine(root, "fake-agent.cmd");
            File.WriteAllText(fake, "@echo off\r\necho FAKE-OUT %*\r\necho touched > touched.txt\r\nexit /b 0\r\n");

            var lines = new List<string>();
            AgentRunResult r = await AgentRunner.RunAsync(fake, new[] { "exec", "hello task" },
                root, TimeSpan.FromSeconds(30), CancellationToken.None, lines.Add);
            Check(r.Ok && r.ExitCode == 0 && !r.TimedOut, "runner exit 0");
            Check(r.Stdout.Contains("FAKE-OUT"), "runner captures stdout");
            Check(lines.Count > 0 && lines[0].Contains("FAKE-OUT"), "runner streams lines");
            Check(File.Exists(Path.Combine(root, "touched.txt")), "runner cwd + side effect");

            string failer = Path.Combine(root, "fail.cmd");
            File.WriteAllText(failer, "@echo off\r\necho OOPS 1>&2\r\nexit /b 3\r\n");
            AgentRunResult rf = await AgentRunner.RunAsync(failer, Array.Empty<string>(),
                root, TimeSpan.FromSeconds(30), CancellationToken.None);
            Check(!rf.Ok && rf.ExitCode == 3, "runner exit code passthrough");
            Check(rf.Stderr.Contains("OOPS"), "runner captures stderr");

            AgentRunResult missing = await AgentRunner.RunAsync(
                Path.Combine(root, "nope.exe"), Array.Empty<string>(),
                root, TimeSpan.FromSeconds(10), CancellationToken.None);
            Check(!missing.Ok && missing.ExitCode == 127, "runner missing exe -> 127");

            string envProbe = Path.Combine(root, "env.cmd");
            File.WriteAllText(envProbe, "@echo off\r\necho MARKER=%CRETE_TEST_MARKER%\r\n");
            var env = new Dictionary<string, string> { ["CRETE_TEST_MARKER"] = "sentinel42" };
            AgentRunResult re = await AgentRunner.RunAsync(envProbe, Array.Empty<string>(),
                root, TimeSpan.FromSeconds(30), CancellationToken.None, null, null, env);
            Check(re.Ok && re.Stdout.Contains("MARKER=sentinel42"), "runner env passthrough");

            // Sleep ~25s via ping; timeout must kill it fast.
            string sleep = Path.Combine(root, "sleep.cmd");
            File.WriteAllText(sleep, "@echo off\r\necho SLEEPING\r\nping -n 25 127.0.0.1 >nul\r\necho WOKE\r\n");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            AgentRunResult rt = await AgentRunner.RunAsync(sleep, Array.Empty<string>(),
                root, TimeSpan.FromSeconds(3), CancellationToken.None);
            sw.Stop();
            Check(rt.TimedOut && rt.ExitCode == 124, "runner timeout flag");
            Check(sw.Elapsed < TimeSpan.FromSeconds(15), $"runner timeout kills fast ({sw.Elapsed.TotalSeconds:F1}s)");
            Check(rt.Stdout.Contains("SLEEPING") && !rt.Stdout.Contains("WOKE"), "runner keeps pre-kill output");

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(1000);
            AgentRunResult rc = await AgentRunner.RunAsync(sleep, Array.Empty<string>(),
                root, TimeSpan.FromMinutes(5), cts.Token);
            Check(!rc.Ok && !rc.TimedOut && rc.ExitCode == 124, "runner cancel kills, not timeout");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crete-agent-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
