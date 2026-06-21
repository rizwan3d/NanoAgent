using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NanoAgent.VS.Services
{
    /// <summary>
    /// Resolves the NanoAgent CLI across npm / pnpm / bun / dotnet-tool global bin dirs
    /// (GUI-launched VS doesn't inherit the shell PATH), and handles npm-based install and
    /// update checks. C# port of the VS Code extension's NanoAgentProcessManager resolution.
    /// </summary>
    internal static class NanoAgentCli
    {
        private static readonly bool IsWindows = Path.DirectorySeparatorChar == '\\';

        private static string[] CommandNames => IsWindows
            ? new[] { "nanoai.cmd", "nanoai.exe", "nanoai" }
            : new[] { "nanoai" };

        /// <summary>Returns an absolute path to nanoai if found in a known dir, else the bare command.</summary>
        public static string ResolveCommand(string command, LogService log)
        {
            if (command != "nanoai" && command != "nanoai.exe"
                || command.IndexOf('/') >= 0 || command.IndexOf('\\') >= 0)
            {
                return command; // explicit path/command — trust it.
            }

            foreach (string dir in GlobalSearchDirs())
            {
                foreach (string name in CommandNames)
                {
                    string candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate))
                    {
                        log.Info($"Resolved nanoai binary: {candidate}");
                        return candidate;
                    }
                }
            }

            return "nanoai"; // fall back to PATH lookup.
        }

        /// <summary>True if the command is an absolute file or bare "nanoai" is on PATH.</summary>
        public static bool IsAvailable(string command)
        {
            if (command != "nanoai") return File.Exists(command) || OnPath(command);
            return OnPath("nanoai");
        }

        private static bool OnPath(string name)
        {
            try
            {
                var psi = new ProcessStartInfo(IsWindows ? "where" : "which", name)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using Process? p = Process.Start(psi);
                if (p == null) return false;
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        public static IEnumerable<string> GlobalSearchDirs()
        {
            var dirs = new List<string>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            void Add(string? d) { if (!string.IsNullOrWhiteSpace(d) && !dirs.Contains(d!)) dirs.Add(d!); }

            Add(Environment.GetEnvironmentVariable("PNPM_HOME"));
            string? bun = Environment.GetEnvironmentVariable("BUN_INSTALL");
            if (!string.IsNullOrEmpty(bun)) Add(Path.Combine(bun!, "bin"));
            string? appData = Environment.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrEmpty(appData)) Add(Path.Combine(appData!, "npm"));
            string? localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(localAppData)) Add(Path.Combine(localAppData!, "pnpm"));
            Add(Path.Combine(home, ".bun", "bin"));
            Add(Path.Combine(home, ".dotnet", "tools"));          // dotnet tool install -g
            Add(Path.Combine(home, "Library", "pnpm"));
            Add(Path.Combine(home, ".local", "share", "pnpm"));
            Add(Path.Combine(home, ".npm-global", "bin"));
            Add("/usr/local/bin");
            Add("/opt/homebrew/bin");

            string? npmPrefix = NpmGlobalPrefix();
            if (npmPrefix != null) Add(IsWindows ? npmPrefix : Path.Combine(npmPrefix, "bin"));

            return dirs;
        }

        private static string? NpmGlobalPrefix()
        {
            try
            {
                string output = RunCaptured(IsWindows ? "cmd.exe" : "npm",
                    IsWindows ? "/c npm prefix -g" : "prefix -g", out int code);
                return code == 0 && !string.IsNullOrWhiteSpace(output) ? output.Trim() : null;
            }
            catch { return null; }
        }

        /// <summary>Runs `npm install -g <package>`, streaming output to the log. Returns true on success.</summary>
        public static Task<bool> NpmInstallAsync(string package, LogService log)
        {
            return Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = IsWindows ? "cmd.exe" : "npm",
                        Arguments = IsWindows ? $"/c npm install -g {package}" : $"install -g {package}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using Process? p = Process.Start(psi);
                    if (p == null) return false;
                    p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log.Info($"npm: {e.Data}"); };
                    p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log.Warn($"npm: {e.Data}"); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit(180000);
                    return p.HasExited && p.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    log.Error("npm install failed to start. Is Node.js/npm installed?", ex);
                    return false;
                }
            });
        }

        /// <summary>Returns (current, latest) versions, or nulls when undetermined.</summary>
        public static (string? Current, string? Latest) CheckVersions(string command, LogService log)
        {
            try
            {
                string current = ParseVersion(RunCaptured(command, "--version", out _));
                string latest = ParseVersion(RunCaptured(IsWindows ? "cmd.exe" : "npm",
                    IsWindows ? "/c npm view nanoai-cli version" : "view nanoai-cli version", out _));
                return (string.IsNullOrEmpty(current) ? null : current,
                        string.IsNullOrEmpty(latest) ? null : latest);
            }
            catch (Exception ex)
            {
                log.Debug("Update check skipped: " + ex.Message);
                return (null, null);
            }
        }

        public static bool IsNewer(string latest, string current)
        {
            int[] a = ParseTriple(latest), b = ParseTriple(current);
            for (int i = 0; i < 3; i++) if (a[i] != b[i]) return a[i] > b[i];
            return false;
        }

        private static int[] ParseTriple(string v)
        {
            string[] parts = (v ?? "0.0.0").Split('.');
            var n = new int[3];
            for (int i = 0; i < 3 && i < parts.Length; i++) int.TryParse(parts[i], out n[i]);
            return n;
        }

        private static string ParseVersion(string? text)
        {
            Match m = Regex.Match(text ?? string.Empty, @"(\d+)\.(\d+)\.(\d+)");
            return m.Success ? m.Value : string.Empty;
        }

        private static string RunCaptured(string fileName, string arguments, out int exitCode)
        {
            exitCode = -1;
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using Process? p = Process.Start(psi);
            if (p == null) return string.Empty;
            string output = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            if (p.HasExited) exitCode = p.ExitCode;
            return output;
        }

        // ── 24h update-check throttle (no Memento in VS; persist to LocalAppData) ──

        private static string CheckStampPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NanoAgent", "last-update-check");

        public static bool ShouldCheckForUpdate()
        {
            try
            {
                if (!File.Exists(CheckStampPath)) return true;
                string text = File.ReadAllText(CheckStampPath).Trim();
                return !long.TryParse(text, out long last)
                       || (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - last) > 24L * 60 * 60 * 1000;
            }
            catch { return true; }
        }

        public static void RecordUpdateCheck()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CheckStampPath)!);
                File.WriteAllText(CheckStampPath, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
            }
            catch { /* best effort */ }
        }
    }
}
