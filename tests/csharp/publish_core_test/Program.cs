// Standalone tests for the Publish tab pure core (no Godot types).
// Run: dotnet run --project engine/tests/csharp/publish_core_test
// Live tool downloads run only with CRETE_LIVE_TOOLS=1.
// Exit code 0 = all passed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading;
using GodotTools.Publish;

static class PublishCoreTests
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
        TestPresetParse();
        TestVdf();
        TestToolUrls();
        TestZipExtract();
        TestTarGzExtract();
        TestKeychain();
        if (Environment.GetEnvironmentVariable("CRETE_LIVE_TOOLS") == "1")
            TestLiveToolsAsync().GetAwaiter().GetResult();
        else
            Console.WriteLine("SKIP live tool downloads (set CRETE_LIVE_TOOLS=1)");

        Console.WriteLine($"PUBLISH-CORE-TESTS passed={s_passed} failed={s_failed}");
        return s_failed == 0 ? 0 : 1;
    }

    static void TestPresetParse()
    {
        string cfg = "[preset.0]\nname=\"Windows\"\nplatform=\"Windows Desktop\"\n" +
            "export_path=\"../build/game.exe\"\n[preset.0.options]\n" +
            "custom_template/debug=\"\"\n[preset.1]\nname=\"Web\"\nplatform=\"Web\"\n" +
            "export_path=\"../build-web/index.html\"\n";
        var presets = ExportPresetsFile.Parse(cfg);
        Check(presets.Count == 2, "preset count");
        Check(presets[0].Name == "Windows" && presets[0].Platform == "Windows Desktop", "preset 0 fields");
        Check(presets[0].ExportPath == "../build/game.exe", "preset 0 path unquoted");
        Check(presets[1].Name == "Web", "preset 1 name");
        Check(ExportPresetsFile.Parse("").Count == 0, "empty cfg");
        Check(ExportPresetsFile.Parse("[preset.1]\nname=\"X\"\n").Count == 0, "gap breaks numbering");
        Check(ExportPresetsFile.DefaultChannel("Windows Desktop") == "windows", "channel windows");
        Check(ExportPresetsFile.DefaultChannel("Web") == "html", "channel html");
        Check(ExportPresetsFile.DefaultChannel("Linux") == "linux", "channel linux");
        Check(ExportPresetsFile.DefaultChannel("???") == "other", "channel other");
    }

    static void TestVdf()
    {
        string vdf = SteamVdf.AppBuild(480, "Crete build 1", "./output", "./content", 1001);
        Check(vdf.Contains("\"appid\"\t\t\"480\""), "vdf appid");
        Check(vdf.Contains("\"desc\"\t\t\"Crete build 1\""), "vdf desc");
        Check(vdf.Contains("\"1001\"") && vdf.Contains("\"depots\""), "vdf depot");
        Check(vdf.Contains("\"contentroot\"\t\"./content\""), "vdf contentroot");
        string evil = SteamVdf.AppBuild(1, "a\"b\\c", "o", "r", 2);
        Check(evil.Contains("a\\\"b\\\\c"), "vdf escaping");
    }

    static void TestToolUrls()
    {
        if (OperatingSystem.IsWindows())
        {
            Check(PublishTools.ButlerDownloadUrl() ==
                "https://broth.itch.zone/butler/windows-amd64/LATEST/archive/default", "butler url win");
            Check(PublishTools.SteamCmdDownloadUrl() ==
                "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip", "steamcmd url win");
            Check(PublishTools.ButlerPath().EndsWith("butler.exe"), "butler exe name");
        }
        string tools = PublishTools.ToolsDir();
        Check(tools.Replace('\\', '/').EndsWith("Crete/tools", StringComparison.OrdinalIgnoreCase),
            "tools dir suffix: " + tools);
        Check(PublishTools.Which("dotnet") != null, "which finds dotnet");
        Check(PublishTools.Which("crete-definitely-not-a-tool-xyz") == null, "which misses cleanly");
    }

    static void TestZipExtract()
    {
        string root = NewTempDir();
        try
        {
            string zip = Path.Combine(root, "dl.zip");
            using (var za = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var entry = za.CreateEntry("nested/deep/butler.exe");
                using (var w = new StreamWriter(entry.Open()))
                    w.Write("fake-exe");
            }
            string dest = Path.Combine(root, "out", "butler.exe");
            PublishTools.ExtractExeFromZip(zip, new[] { "butler.exe", "butler" }, dest);
            Check(File.ReadAllText(dest) == "fake-exe", "zip nested extract");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void TestTarGzExtract()
    {
        string root = NewTempDir();
        try
        {
            string src = Path.Combine(root, "src");
            Directory.CreateDirectory(Path.Combine(src, "sub"));
            File.WriteAllText(Path.Combine(src, "sub", "steamcmd.sh"), "fake-sh");
            string tgz = Path.Combine(root, "dl.tgz");
            using (var file = File.Create(tgz))
            using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
                TarFile.CreateFromDirectory(src, gzip, includeBaseDirectory: false);
            string dest = Path.Combine(root, "out", "steamcmd.sh");
            PublishTools.ExtractExeFromTarGzAsync(tgz, "steamcmd.sh", dest, CancellationToken.None)
                .GetAwaiter().GetResult();
            Check(File.ReadAllText(dest) == "fake-sh", "tar.gz nested extract");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void TestKeychain()
    {
        const string key = "test_roundtrip_do_not_ship";
        PublishKeychain.Delete(key);
        Check(!PublishKeychain.Exists(key), "keychain missing -> false");
        Check(PublishKeychain.Load(key) == null, "keychain missing -> null");
        PublishKeychain.Save(key, "s3cret-value");
        Check(PublishKeychain.Exists(key), "keychain exists after save");
        Check(PublishKeychain.Load(key) == "s3cret-value", "keychain roundtrip");
        PublishKeychain.Delete(key);
        Check(!PublishKeychain.Exists(key), "keychain gone after delete");
    }

    static async System.Threading.Tasks.Task TestLiveToolsAsync()
    {
        string butler = await PublishTools.EnsureButlerAsync(CancellationToken.None, Console.WriteLine);
        Check(File.Exists(butler), "live butler fetched");
        var psi = new ProcessStartInfo(butler, "-V")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        // NOTE: butler -V reports on stderr.
        string output = await proc.StandardOutput.ReadToEndAsync() + await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        Console.WriteLine("butler -V: " + output.Trim());
        Check(proc.ExitCode == 0 && output.Contains("v"), "live butler runs");
        // Idempotency: second call must not re-download.
        string again = await PublishTools.EnsureButlerAsync(CancellationToken.None);
        Check(again == butler, "live butler cached");
        string steamcmd = await PublishTools.EnsureSteamCmdAsync(CancellationToken.None, Console.WriteLine);
        Check(File.Exists(steamcmd), "live steamcmd fetched (not run: first run self-updates)");
    }

    static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crete-pub-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
