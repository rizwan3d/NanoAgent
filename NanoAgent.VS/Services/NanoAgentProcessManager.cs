using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NanoAgent.VS.Services
{
    /// <summary>
    /// Manages the NanoAgent CLI process used for ACP communication.
    /// </summary>
    internal sealed class NanoAgentProcessManager : IDisposable
    {
        private Process? _process;
        private readonly LogService _log;
        private bool _disposed;

        public NanoAgentProcessManager()
        {
            _log = LogService.Instance;
        }

        public StreamWriter? Stdin { get; private set; }
        public StreamReader? Stdout { get; private set; }
        public bool IsRunning => _process is not null && !_process.HasExited;

        public void Start(string executablePath, params string[] args)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NanoAgentProcessManager));
            }

            if (IsRunning)
            {
                _log.Warn("NanoAgentProcessManager: process is already running.");
                return;
            }

            string resolvedExecutable = ResolveExecutablePath(executablePath);

            ProcessStartInfo startInfo = new()
            {
                FileName = resolvedExecutable,
                Arguments = string.Join(" ", args.Where(static arg => !string.IsNullOrWhiteSpace(arg)).Select(QuoteArgument)),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _process = new Process { StartInfo = startInfo };
            _process.Start();

            Stdin = _process.StandardInput;
            Stdout = _process.StandardOutput;

            _log.Info($"NanoAgent CLI process started (PID: {_process.Id}, FileName: {resolvedExecutable}).");

            Task.Run(() =>
            {
                try
                {
                    string? errorLine;
                    while ((errorLine = _process.StandardError.ReadLine()) is not null)
                    {
                        if (!string.IsNullOrWhiteSpace(errorLine))
                        {
                            _log.Error($"NanoAgent CLI stderr: {errorLine}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Debug($"NanoAgent CLI stderr reader ended: {ex.Message}");
                }
            });
        }

        public void Stop()
        {
            if (_process is null || _process.HasExited)
            {
                return;
            }

            try
            {
                _process.Kill();
                _process.WaitForExit(1000);
                _log.Info("NanoAgent CLI process killed.");
            }
            catch (Exception ex)
            {
                _log.Error("Failed to kill NanoAgent CLI process", ex);
            }

            Cleanup();
        }

        public async Task StopAsync(int timeoutMs = 3000)
        {
            if (_process is null || _process.HasExited)
            {
                return;
            }

            try
            {
                if (Stdin is not null)
                {
                    await Stdin.FlushAsync();
                    Stdin.Close();
                }
            }
            catch
            {
            }

            if (!_process.WaitForExit(timeoutMs))
            {
                _process.Kill();
                _log.Warn("NanoAgent CLI process had to be killed after shutdown timeout.");
            }

            Cleanup();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
        }

        private void Cleanup()
        {
            Stdin?.Dispose();
            Stdout?.Dispose();
            _process?.Dispose();
            Stdin = null;
            Stdout = null;
            _process = null;
        }

        private static string QuoteArgument(string value)
        {
            return value.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0
                ? "\"" + value.Replace("\"", "\\\"") + "\""
                : value;
        }

        private static string ResolveExecutablePath(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new ArgumentException("Executable path must not be empty.", nameof(executablePath));
            }

            string candidate = executablePath.Trim();
            if (LooksLikePath(candidate))
            {
                string fullPath = Path.GetFullPath(candidate);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException(
                        $"NanoAgent CLI executable not found at: {fullPath}",
                        fullPath);
                }

                return fullPath;
            }

            string? resolvedOnPath = TryResolveOnPath(candidate);
            if (resolvedOnPath is not null)
            {
                return resolvedOnPath;
            }

            throw new FileNotFoundException(
                $"NanoAgent CLI executable '{candidate}' was not found on the system PATH.",
                candidate);
        }

        private static bool LooksLikePath(string value)
        {
            return Path.IsPathRooted(value)
                || value.IndexOf(Path.DirectorySeparatorChar) >= 0
                || value.IndexOf(Path.AltDirectorySeparatorChar) >= 0;
        }

        private static string? TryResolveOnPath(string fileName)
        {
            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                return null;
            }

            string[] extensions = Path.HasExtension(fileName)
                ? new[] { string.Empty }
                : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string directory in pathValue.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmedDirectory = directory.Trim();
                if (trimmedDirectory.Length == 0 || !Directory.Exists(trimmedDirectory))
                {
                    continue;
                }

                foreach (string extension in extensions)
                {
                    string candidate = Path.Combine(trimmedDirectory, fileName + extension);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }
    }
}
