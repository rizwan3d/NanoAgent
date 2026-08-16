using StemCode.VS.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace StemCode.VS.ToolWindows
{
    public sealed partial class ChatToolWindowControl
    {
        private bool _voiceDictationRunning;

        protected override async void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.R &&
                (Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
                InputTextBox.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                await RunVoiceDictationAsync();
                return;
            }

            base.OnPreviewKeyDown(e);
        }

        private async Task RunVoiceDictationAsync()
        {
            if (_voiceDictationRunning)
            {
                return;
            }

            _voiceDictationRunning = true;
            StatusText.Text = "Voice: preparing";

            try
            {
                string transcript = await CaptureVoiceTranscriptAsync();
                if (string.IsNullOrWhiteSpace(transcript))
                {
                    StatusText.Text = "Voice: no speech detected";
                    return;
                }

                string current = InputTextBox.Text ?? string.Empty;
                InputTextBox.Text = string.IsNullOrWhiteSpace(current)
                    ? transcript.Trim()
                    : current + (char.IsWhiteSpace(current[current.Length - 1]) ? string.Empty : " ") + transcript.Trim();
                InputTextBox.CaretIndex = InputTextBox.Text.Length;
                InputTextBox.Focus();
                StatusText.Text = "Ready";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Voice: failed";
                _log.Error("Voice dictation failed.", ex);
                MessageBox.Show(
                    ex.Message,
                    "Voice Dictation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _voiceDictationRunning = false;
            }
        }

        private async Task<string> CaptureVoiceTranscriptAsync()
        {
            string command = ResolveVoiceCliCommand();
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = "--voice-dictate",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start the voice process."))
            {
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = ReadVoiceProgressAsync(process);
                await Task.Run(() => process.WaitForExit());

                string output = await outputTask;
                string error = await errorTask;
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(error) ? "Voice dictation failed." : error.Trim());
                }

                return output.Trim();
            }
        }

        private async Task<string> ReadVoiceProgressAsync(Process process)
        {
            var error = new StringBuilder();
            string line;
            while ((line = await process.StandardError.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (error.Length > 0)
                {
                    error.AppendLine();
                }
                error.Append(line);
                await Dispatcher.InvokeAsync(() => StatusText.Text = "Voice: " + line.Trim());
            }

            return error.ToString();
        }

        private static string ResolveVoiceCliCommand()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string localExecutable = Path.Combine(baseDirectory, "StemCode.CLI.exe");
            if (File.Exists(localExecutable))
            {
                return localExecutable;
            }

            string localCommand = Path.Combine(baseDirectory, "stemcode.exe");
            if (File.Exists(localCommand))
            {
                return localCommand;
            }

            return StemCodeCli.ResolveCommand("stemcode", LogService.Instance);
        }
    }
}
