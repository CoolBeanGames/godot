using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GodotTools.Publish
{
    /// <summary>
    /// Fetches and caches the external upload tools (itch.io butler,
    /// Valve steamcmd) in a machine-local Crete tools dir on first use.
    /// BCL only (unit-testable; downloads are live-gated in tests).
    /// </summary>
    public static class PublishTools
    {
        public static string ToolsDir()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Crete", "tools");
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "Crete", "tools");
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "crete", "tools");
        }

        public static string ButlerPath() =>
            Path.Combine(ToolsDir(), RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "butler.exe" : "butler");

        public static string SteamCmdPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Path.Combine(ToolsDir(), "steamcmd.exe");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Path.Combine(ToolsDir(), "steamcmd.sh");
            throw new PlatformNotSupportedException("Valve does not ship steamcmd for macOS.");
        }

        // NOTE: broth.itch.ovh is retired (NXDOMAIN); broth.itch.zone is canonical
        // per itch.io admin notice. There is no GitHub-release fallback.
        public static string ButlerDownloadUrl()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "https://broth.itch.zone/butler/windows-amd64/LATEST/archive/default";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "https://broth.itch.zone/butler/darwin-amd64/LATEST/archive/default";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "https://broth.itch.zone/butler/linux-amd64/LATEST/archive/default";
            throw new PlatformNotSupportedException("Unsupported OS for butler.");
        }

        /// <summary>
        /// Minimal PATH lookup (BCL only; the Utils.OS one is unavailable to
        /// the standalone test compile). Prefers an installed tool over a
        /// download and doubles as the offline fallback.
        /// </summary>
        public static string? Which(string name)
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv))
                return null;
            string[] exts = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';')
                : new[] { "" };
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                foreach (string ext in exts)
                {
                    string candidate = Path.Combine(dir.Trim(), name + ext.ToLowerInvariant());
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            return null;
        }

        public static string SteamCmdDownloadUrl()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";
            throw new PlatformNotSupportedException("Valve does not ship steamcmd for macOS.");
        }

        public static async Task<string> EnsureButlerAsync(CancellationToken cancel,
            Action<string>? log = null)
        {
            string? onPath = Which("butler");
            if (!string.IsNullOrEmpty(onPath))
            {
                log?.Invoke("Using butler from PATH: " + onPath);
                return onPath;
            }
            string exe = ButlerPath();
            if (File.Exists(exe))
                return exe;
            log?.Invoke("Fetching butler (itch.io uploader)...");
            try
            {
                string zip = await DownloadAsync(ButlerDownloadUrl(), "butler-archive", cancel).ConfigureAwait(false);
                ExtractExeFromZip(zip, new[] { "butler.exe", "butler" }, exe);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    "Could not fetch butler automatically. Install it by hand (https://itchio.itch.io/butler) " +
                    "and put it on PATH. Details: " + e.Message, e);
            }
            MakeExecutable(exe);
            log?.Invoke("butler ready: " + exe);
            return exe;
        }

        public static async Task<string> EnsureSteamCmdAsync(CancellationToken cancel,
            Action<string>? log = null)
        {
            string? onPath = Which("steamcmd");
            if (!string.IsNullOrEmpty(onPath))
            {
                log?.Invoke("Using steamcmd from PATH: " + onPath);
                return onPath;
            }
            string exe = SteamCmdPath();
            if (File.Exists(exe))
                return exe;
            log?.Invoke("Fetching steamcmd (Steam uploader)...");
            try
            {
                string archive = await DownloadAsync(SteamCmdDownloadUrl(), "steamcmd-archive", cancel).ConfigureAwait(false);
                if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    ExtractExeFromZip(archive, new[] { "steamcmd.exe" }, exe);
                else
                    await ExtractExeFromTarGzAsync(archive, "steamcmd.sh", exe, cancel).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    "Could not fetch steamcmd automatically. Install it by hand " +
                    "(https://developer.valvesoftware.com/wiki/SteamCMD) and put it on PATH. Details: " + e.Message, e);
            }
            MakeExecutable(exe);
            log?.Invoke("steamcmd ready: " + exe);
            return exe;
        }

        public static async Task<string> DownloadAsync(string url, string localName, CancellationToken cancel)
        {
            Directory.CreateDirectory(ToolsDir());
            string dest = Path.Combine(ToolsDir(), localName + (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".pkg"));
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Crete-Publish/1.0");
            using var response = await http.GetAsync(url, cancel).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var outStream = File.Create(dest);
            await response.Content.CopyToAsync(outStream, cancel).ConfigureAwait(false);
            return dest;
        }

        public static void ExtractExeFromZip(string zipPath, string[] exeNames, string destPath)
        {
            using var zip = ZipFile.OpenRead(zipPath);
            ZipArchiveEntry? match = zip.Entries.FirstOrDefault(e =>
                exeNames.Contains(Path.GetFileName(e.FullName), StringComparer.OrdinalIgnoreCase));
            if (match == null)
                throw new InvalidDataException($"None of [{string.Join(",", exeNames)}] found in {zipPath}");
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            match.ExtractToFile(destPath, overwrite: true);
        }

        public static async Task ExtractExeFromTarGzAsync(string tarGzPath, string exeName, string destPath, CancellationToken cancel)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "crete-tar-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                using (var file = File.OpenRead(tarGzPath))
                using (var gzip = new System.IO.Compression.GZipStream(file, CompressionMode.Decompress))
                {
                    await TarFile.ExtractToDirectoryAsync(gzip, tmp, overwriteFiles: true, cancellationToken: cancel).ConfigureAwait(false);
                }
                string? match = Directory.EnumerateFiles(tmp, exeName, SearchOption.AllDirectories).FirstOrDefault();
                if (match == null)
                    throw new InvalidDataException($"{exeName} not found in {tarGzPath}");
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(match, destPath, overwrite: true);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch (Exception) { }
            }
        }

        private static void MakeExecutable(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;
            try
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch (Exception)
            {
                // Best effort; the caller surfaces exec failures.
            }
        }
    }
}
