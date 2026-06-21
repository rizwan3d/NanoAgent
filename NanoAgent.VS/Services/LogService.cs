using System;
using System.Diagnostics;
using System.IO;

namespace NanoAgent.VS.Services
{
    /// <summary>
    /// Simple logging service for the NanoAgent.VS extension.
    /// Writes to a log file in the user's temp directory and optionally to Debug output.
    /// </summary>
    internal sealed class LogService : IDisposable
    {
        private static readonly Lazy<LogService> _instance = new(() => new LogService());
        private readonly StreamWriter? _writer;
        private readonly object _lock = new();

        private LogService()
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NanoAgent",
                    "Logs");
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(
                    logDir,
                    $"NanoAgent.VS_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                _writer = new StreamWriter(logPath, append: true)
                {
                    AutoFlush = true
                };
                Info("LogService initialized.");
            }
            catch
            {
                _writer = null;
            }
        }

        public static LogService Instance => _instance.Value;

        /// <summary>Minimum level to emit: debug, info, warn, error. Set from the options page.</summary>
        public string MinLevel { get; set; } = "info";

        private static int Rank(string level) => level switch
        {
            "DEBUG" or "debug" => 0,
            "INFO" or "info" => 1,
            "WARN" or "warn" => 2,
            _ => 3
        };

        public void Info(string message)
        {
            Write("INFO", message);
        }

        public void Warn(string message, Exception? ex = null)
        {
            Write("WARN", message, ex);
        }

        public void Error(string message, Exception? ex = null)
        {
            Write("ERROR", message, ex);
        }

        public void Debug(string message)
        {
            Write("DEBUG", message);
        }

        private void Write(string level, string message, Exception? ex = null)
        {
            if (Rank(level) < Rank(MinLevel)) return;

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logLine = $"[{timestamp} {level}] {message}";

            if (ex is not null)
            {
                logLine += $" | Exception: {ex.Message}";
            }

            System.Diagnostics.Debug.WriteLine(logLine);

            if (_writer is null)
            {
                return;
            }

            lock (_lock)
            {
                try
                {
                    _writer.WriteLine(logLine);
                    if (ex is not null)
                    {
                        _writer.WriteLine(ex.ToString());
                    }
                }
                catch
                {
                    // Silently handle logging failures
                }
            }
        }

        public void Dispose()
        {
            _writer?.Dispose();
        }
    }
}
