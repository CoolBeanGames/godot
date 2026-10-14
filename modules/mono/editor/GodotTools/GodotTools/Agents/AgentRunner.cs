using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GodotTools.Agents
{
    public sealed record AgentRunResult(int ExitCode, string Stdout, string Stderr, bool TimedOut)
    {
        public bool Ok => ExitCode == 0 && !TimedOut;
    }

    /// <summary>
    /// Runs one agent CLI invocation: streams output lines to a callback,
    /// enforces a timeout, supports cancellation. BCL only (no Godot types)
    /// so it can be unit-tested outside the engine.
    /// </summary>
    public static class AgentRunner
    {
        public static async Task<AgentRunResult> RunAsync(string cliPath, string[] args,
            string workingDir, TimeSpan timeout, CancellationToken cancel,
            Action<string>? onStdout = null, Action<string>? onStderr = null)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // .NET cannot spawn .cmd/.bat directly without a shell.
            string ext = Path.GetExtension(cliPath);
            if (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".bat", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.FileName = "cmd.exe";
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add(cliPath);
            }
            else
            {
                startInfo.FileName = cliPath;
            }
            foreach (string arg in args)
                startInfo.ArgumentList.Add(arg);

            try
            {
                using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                        return;
                    lock (stdout)
                        stdout.AppendLine(e.Data);
                    onStdout?.Invoke(e.Data);
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                        return;
                    lock (stderr)
                        stderr.AppendLine(e.Data);
                    onStderr?.Invoke(e.Data);
                };

                if (!process.Start())
                    return new AgentRunResult(127, "", "process failed to start", false);

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using var timeoutCts = new CancellationTokenSource(timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeoutCts.Token);
                try
                {
                    await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    bool timedOut = timeoutCts.IsCancellationRequested && !cancel.IsCancellationRequested;
                    try
                    {
                        if (!process.HasExited)
                            process.Kill(entireProcessTree: true);
                    }
                    catch (Exception)
                    {
                        // Already gone; fall through to result.
                    }
                    // Drain what we captured before the kill.
                    await Task.Delay(200).ConfigureAwait(false);
                    lock (stdout)
                        lock (stderr)
                            return new AgentRunResult(124, stdout.ToString(), stderr.ToString(), timedOut);
                }

                // Let async readers flush.
                await Task.Delay(100).ConfigureAwait(false);
                lock (stdout)
                    lock (stderr)
                        return new AgentRunResult(process.ExitCode, stdout.ToString(), stderr.ToString(), false);
            }
            catch (Exception e)
            {
                return new AgentRunResult(127, stdout.ToString(), e.Message, false);
            }
        }
    }
}
