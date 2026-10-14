using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GodotTools.Publish
{
    /// <summary>
    /// OS keychain for publish credentials (itch API key, Steam login).
    /// Secrets live here, never in the project. Windows: Credential Manager
    /// (P/Invoke). macOS: `security` CLI. Linux: `secret-tool` CLI.
    /// BCL only (unit-testable; the Windows roundtrip runs live in tests).
    /// </summary>
    public static class PublishKeychain
    {
        public const string ItchApiKey = "itch_api_key";
        public const string SteamUser = "steam_user";
        public const string SteamPass = "steam_pass";

        private static string Service(string key) => "Crete/publish/" + key;

        public static void Save(string key, string secret)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                WindowsCred.Write(Service(key), secret);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                RunHelper("security", new[] { "add-generic-password", "-a", "crete", "-s", Service(key), "-w", secret, "-U" });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                RunHelperWithStdin("secret-tool", new[] { "store", "--label=Crete " + key, "service", Service(key), "account", "crete" }, secret);
            else
                throw new PlatformNotSupportedException("No keychain helper for this OS.");
        }

        public static string? Load(string key)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return WindowsCred.Read(Service(key));
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return RunHelper("security", new[] { "find-generic-password", "-a", "crete", "-s", Service(key), "-w" }, allowFailure: true)?.TrimEnd();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return RunHelper("secret-tool", new[] { "lookup", "service", Service(key), "account", "crete" }, allowFailure: true)?.TrimEnd();
            throw new PlatformNotSupportedException("No keychain helper for this OS.");
        }

        public static bool Exists(string key) => !string.IsNullOrEmpty(Load(key));

        public static void Delete(string key)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                WindowsCred.Delete(Service(key));
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                RunHelper("security", new[] { "delete-generic-password", "-a", "crete", "-s", Service(key) }, allowFailure: true);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                RunHelper("secret-tool", new[] { "clear", "service", Service(key), "account", "crete" }, allowFailure: true);
            else
                throw new PlatformNotSupportedException("No keychain helper for this OS.");
        }

        private static string? RunHelper(string exe, string[] args, bool allowFailure = false)
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string a in args)
                psi.ArgumentList.Add(a);
            try
            {
                using var proc = Process.Start(psi);
                if (proc == null)
                    throw new InvalidOperationException(exe + " failed to start.");
                string stdout = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                if (proc.ExitCode != 0 && !allowFailure)
                    throw new InvalidOperationException($"{exe} exited {proc.ExitCode}.");
                return proc.ExitCode == 0 ? stdout : null;
            }
            catch (Win32Exception e)
            {
                throw new InvalidOperationException($"Keychain helper '{exe}' is not available: {e.Message}");
            }
        }

        private static void RunHelperWithStdin(string exe, string[] args, string stdin)
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string a in args)
                psi.ArgumentList.Add(a);
            try
            {
                using var proc = Process.Start(psi);
                if (proc == null)
                    throw new InvalidOperationException(exe + " failed to start.");
                proc.StandardInput.Write(stdin);
                proc.StandardInput.Close();
                proc.WaitForExit();
                if (proc.ExitCode != 0)
                    throw new InvalidOperationException($"{exe} exited {proc.ExitCode}.");
            }
            catch (Win32Exception e)
            {
                throw new InvalidOperationException($"Keychain helper '{exe}' is not available: {e.Message}");
            }
        }

        private static class WindowsCred
        {
            private const uint CRED_TYPE_GENERIC = 1;
            private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct CREDENTIAL
            {
                public uint Flags;
                public uint Type;
                public string TargetName;
                public string Comment;
                public long LastWritten;
                public uint CredentialBlobSize;
                public IntPtr CredentialBlob;
                public uint Persist;
                public uint AttributeCount;
                public IntPtr Attributes;
                public string TargetAlias;
                public string UserName;
            }

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern bool CredWrite([In] ref CREDENTIAL userCredential, uint flags);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern bool CredRead(string targetName, uint type, uint flags, out IntPtr credential);

            [DllImport("advapi32.dll", SetLastError = true)]
            private static extern bool CredDelete(string targetName, uint type, uint flags);

            [DllImport("advapi32.dll")]
            private static extern void CredFree(IntPtr credential);

            public static void Write(string target, string secret)
            {
                byte[] blob = Encoding.Unicode.GetBytes(secret);
                IntPtr blobPtr = Marshal.AllocCoTaskMem(Math.Max(blob.Length, 1));
                try
                {
                    Marshal.Copy(blob, 0, blobPtr, blob.Length);
                    var cred = new CREDENTIAL
                    {
                        Flags = 0,
                        Type = CRED_TYPE_GENERIC,
                        TargetName = target,
                        Comment = "Stored by the Crete engine Publish tab.",
                        CredentialBlobSize = (uint)blob.Length,
                        CredentialBlob = blobPtr,
                        Persist = CRED_PERSIST_LOCAL_MACHINE,
                        UserName = "crete",
                    };
                    if (!CredWrite(ref cred, 0))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "CredWrite failed.");
                }
                finally
                {
                    Marshal.FreeCoTaskMem(blobPtr);
                }
            }

            public static string? Read(string target)
            {
                if (!CredRead(target, CRED_TYPE_GENERIC, 0, out IntPtr ptr))
                    return null; // Missing (or unreadable): no secret stored.
                try
                {
                    var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
                    if (cred.CredentialBlobSize == 0)
                        return "";
                    byte[] blob = new byte[cred.CredentialBlobSize];
                    Marshal.Copy(cred.CredentialBlob, blob, 0, blob.Length);
                    return Encoding.Unicode.GetString(blob);
                }
                finally
                {
                    CredFree(ptr);
                }
            }

            public static void Delete(string target)
            {
                CredDelete(target, CRED_TYPE_GENERIC, 0); // Missing is fine.
            }
        }
    }
}
