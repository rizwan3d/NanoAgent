using EnvDTE;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using NanoAgent.VS.Services;
using NanoAgent.VS.ToolWindows;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace NanoAgent.VS
{
    /// <summary>
    /// NanoAgent Visual Studio extension package.
    /// Manages the chat tool window and NanoAgent ACP lifecycle.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(VSPackage.PackageGuidString)]
    [ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideToolWindow(typeof(ChatToolWindow),
        Style = VsDockStyle.Tabbed,
        Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(NanoAgentOptionsPage), "NanoAgent", "General", 0, 0, true)]
    public sealed class VSPackage : AsyncPackage
    {
        public const string PackageGuidString = "08b5699b-4adb-4433-9b60-bc4f5a3807df";
        internal static VSPackage? Instance { get; private set; }

        // Command set GUID and IDs, must match NanoAgentVSPackage.vsct
        internal static readonly Guid CommandSetGuid = new Guid("0A1B2C3D-4E5F-6789-0ABC-DEF012345678");
        internal const int CmdidShowChatWindow = 0x0100;
        internal const int CmdidNewChat = 0x0101;
        internal const int CmdidSendSelection = 0x0102;
        internal const int CmdidExplainSelection = 0x0103;
        internal const int CmdidSendCurrentFile = 0x0104;
        internal const int CmdidReviewCurrentFile = 0x0105;
        internal const int CmdidReviewGitDiff = 0x0106;
        internal const int CmdidPlanChanges = 0x0107;
        internal const int CmdidApplySuggestedChanges = 0x0108;
        internal const int CmdidShowTerminals = 0x0109;

        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            Instance = this;
            await base.InitializeAsync(cancellationToken, progress);
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Register the menu command that shows the chat tool window.
            // The VSCT file places this command under View -> Other Windows, View, and Tools.
            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService menuService)
            {
                var menuCommandId = new CommandID(CommandSetGuid, CmdidShowChatWindow);
                var menuCommand = new OleMenuCommand(
                    ShowChatWindowCallback,
                    menuCommandId)
                {
                    Enabled = true,
                    Visible = true
                };

                menuCommand.BeforeQueryStatus += (_, _) =>
                {
                    menuCommand.Enabled = true;
                    menuCommand.Visible = true;
                };

                menuService.AddCommand(menuCommand);

                // Editor-context / Tools-menu commands.
                Register(menuService, CmdidNewChat, (_, _) => RunOnChatAsync(c => c.NewChatAsync()));
                Register(menuService, CmdidSendSelection, (_, _) => SendEditorContextAsync("Use this selection as context:", selectionOnly: true, review: false));
                Register(menuService, CmdidExplainSelection, (_, _) => SendEditorContextAsync("Explain this selection:", selectionOnly: true, review: false));
                Register(menuService, CmdidSendCurrentFile, (_, _) => SendEditorContextAsync("Use this file as context:", selectionOnly: false, review: false));
                Register(menuService, CmdidReviewCurrentFile, (_, _) => SendEditorContextAsync("Review this file for bugs, regressions, and missing tests:", selectionOnly: false, review: true));
                Register(menuService, CmdidReviewGitDiff, (_, _) => ReviewGitDiffAsync());
                Register(menuService, CmdidPlanChanges, (_, _) => RunOnChatAsync(c => { c.PrefillInput("Plan the following change before editing:\n\n"); return Task.CompletedTask; }));
                Register(menuService, CmdidApplySuggestedChanges, (_, _) => RunOnChatAsync(c => c.SubmitExternalPromptAsync("Apply the suggested changes from the previous NanoAgent response.")));
                Register(menuService, CmdidShowTerminals, (_, _) => RunOnChatAsync(c => c.SubmitExternalPromptAsync("/terminals")));
            }

            LogService.Instance.Info("NanoAgent.VS package initialized; commands registered.");
        }

        private static void Register(OleMenuCommandService menuService, int id, EventHandler handler)
            => menuService.AddCommand(new OleMenuCommand(handler, new CommandID(CommandSetGuid, id)));

        private async Task<ChatToolWindowControl?> GetChatControlAsync()
        {
            ToolWindowPane? pane = await ShowToolWindowAsync(typeof(ChatToolWindow), 0, create: true, cancellationToken: DisposalToken);
            return (pane as ChatToolWindow)?.Control;
        }

        private async void RunOnChatAsync(Func<ChatToolWindowControl, Task> action)
        {
            try
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                ChatToolWindowControl? control = await GetChatControlAsync();
                if (control != null) await action(control);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error("NanoAgent editor command failed.", ex);
            }
        }

        private async void SendEditorContextAsync(string instruction, bool selectionOnly, bool review)
        {
            try
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);

                if (await GetServiceAsync(typeof(DTE)) is not DTE dte || dte.ActiveDocument is not Document doc)
                {
                    Warn("Open a file in the editor first.");
                    return;
                }

                string fileName = doc.FullName;
                string lang = LanguageFromPath(fileName);
                string text;

                if (selectionOnly)
                {
                    if (doc.Selection is not TextSelection sel || string.IsNullOrWhiteSpace(sel.Text))
                    {
                        Warn("Select code or text before sending it to NanoAgent.");
                        return;
                    }
                    text = sel.Text;
                }
                else
                {
                    if (doc.Object("TextDocument") is not TextDocument td)
                    {
                        Warn("The active document is not a text file.");
                        return;
                    }
                    text = td.StartPoint.CreateEditPoint().GetText(td.EndPoint);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        Warn("The active file is empty.");
                        return;
                    }
                }

                string prompt = $"{instruction}\n\nFile: {fileName}\n\n```{lang}\n{text}\n```";
                ChatToolWindowControl? control = await GetChatControlAsync();
                if (control != null) await control.SubmitExternalPromptAsync(prompt);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error("NanoAgent editor-context command failed.", ex);
            }
        }

        private async void ReviewGitDiffAsync()
        {
            try
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                string cwd = GetWorkingDirectory();
                string diff = await Task.Run(() => RunGitDiff(cwd));
                if (string.IsNullOrWhiteSpace(diff))
                {
                    Warn("No git diff found for NanoAgent to review.");
                    return;
                }

                string prompt = $"Review this git diff for bugs, regressions, and missing tests:\n\n```diff\n{diff}\n```";
                ChatToolWindowControl? control = await GetChatControlAsync();
                if (control != null) await control.SubmitExternalPromptAsync(prompt);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error("NanoAgent git-diff review failed.", ex);
                Warn("Unable to read git diff: " + ex.Message);
            }
        }

        private static string RunGitDiff(string workingDirectory)
        {
            var psi = new ProcessStartInfo("git", "diff --no-ext-diff HEAD")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process? p = Process.Start(psi);
            if (p == null) return string.Empty;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(20000);
            return output;
        }

        private string GetWorkingDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string? configured = GetOptionsPage()?.WorkingDirectory?.Trim();
            if (!string.IsNullOrWhiteSpace(configured)) return configured!;

            if (OpenFolderDirectory() is string folder) return folder;

            if (GetGlobalService(typeof(SVsSolution)) is IVsSolution solution &&
                solution.GetSolutionInfo(out string dir, out _, out _) == 0 &&
                !string.IsNullOrWhiteSpace(dir))
            {
                return dir;
            }
            return Directory.GetCurrentDirectory();
        }

        /// <summary>The folder opened via Open Folder / CMake (no .sln). Null in classic solution mode.</summary>
        internal static string? OpenFolderDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var cm = GetGlobalService(typeof(SComponentModel)) as IComponentModel;
                string? loc = cm?.GetService<IVsFolderWorkspaceService>()?.CurrentWorkspace?.Location;
                return string.IsNullOrWhiteSpace(loc) ? null : loc;
            }
            catch { return null; }
        }

        private static string LanguageFromPath(string path)
        {
            string ext = Path.GetExtension(path)?.TrimStart('.').ToLowerInvariant() ?? "";
            return ext switch
            {
                "cs" => "csharp",
                "ts" => "typescript",
                "js" => "javascript",
                "py" => "python",
                "rs" => "rust",
                "go" => "go",
                "java" => "java",
                "cpp" or "cc" or "cxx" or "h" or "hpp" => "cpp",
                "xaml" or "xml" => "xml",
                _ => ext
            };
        }

        private void Warn(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(this, message, "NanoAgent",
                OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        internal bool Confirm(string title, string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            int result = VsShellUtilities.ShowMessageBox(this, message, title,
                OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            return result == 6; // IDYES
        }

        internal void Info(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Warn(message);
        }

        internal void ShowOptionPageInternal()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ShowOptionPage(typeof(NanoAgentOptionsPage));
        }

        private async void ShowChatWindowCallback(object sender, EventArgs e)
        {
            try
            {
                ToolWindowPane? window = await ShowToolWindowAsync(
                    typeof(ChatToolWindow),
                    0,
                    create: true,
                    cancellationToken: DisposalToken);

                if (window is null || window.Frame is null)
                {
                    throw new InvalidOperationException("NanoAgent Chat tool window could not be created.");
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error("Failed to show NanoAgent Chat tool window.", ex);
                VsShellUtilities.ShowMessageBox(
                    this,
                    @"NanoAgent Chat failed to open. Details were written to %LOCALAPPDATA%\NanoAgent\Logs.",
                    "NanoAgent Chat",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }

        public override IVsAsyncToolWindowFactory GetAsyncToolWindowFactory(Guid toolWindowType)
        {
            if (toolWindowType == typeof(ChatToolWindow).GUID)
            {
                return this;
            }

            return base.GetAsyncToolWindowFactory(toolWindowType);
        }

        protected override string GetToolWindowTitle(Type toolWindowType, int id)
        {
            if (toolWindowType == typeof(ChatToolWindow))
            {
                return "NanoAgent Chat";
            }

            return base.GetToolWindowTitle(toolWindowType, id);
        }

        protected override async Task<object?> InitializeToolWindowAsync(
            Type toolWindowType,
            int id,
            CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            return null;
        }

        internal NanoAgentOptionsPage GetOptionsPage()
        {
            return (NanoAgentOptionsPage)GetDialogPage(typeof(NanoAgentOptionsPage));
        }
    }

    public enum NanoAgentLogLevel { Debug, Info, Warn, Error }

    /// <summary>
    /// Options page for NanoAgent settings. Mirrors the VS Code extension's configuration.
    /// </summary>
    public class NanoAgentOptionsPage : DialogPage
    {
        [System.ComponentModel.Category("CLI")]
        [System.ComponentModel.DisplayName("NanoAgent CLI Executable Path")]
        [System.ComponentModel.Description("Optional path or command name for the NanoAgent CLI. Leave empty to auto-resolve 'nanoai' from PATH and npm/pnpm/bun/dotnet global bins.")]
        public string NanoAgentCliPath { get; set; } = "";

        [System.ComponentModel.Category("CLI")]
        [System.ComponentModel.DisplayName("Extra CLI Arguments")]
        [System.ComponentModel.Description("Additional space-separated arguments passed to the NanoAgent CLI (besides --acp --surface visual_studio).")]
        public string ExtraArguments { get; set; } = "";

        [System.ComponentModel.Category("CLI")]
        [System.ComponentModel.DisplayName("Working Directory")]
        [System.ComponentModel.Description("Default working directory for NanoAgent sessions (leave empty to use the solution directory).")]
        public string WorkingDirectory { get; set; } = "";

        [System.ComponentModel.Category("CLI")]
        [System.ComponentModel.DisplayName("Auto-install CLI")]
        [System.ComponentModel.Description("When 'nanoai' is not found, offer to install it via 'npm install -g nanoai-cli'.")]
        public bool AutoInstall { get; set; } = true;

        [System.ComponentModel.Category("CLI")]
        [System.ComponentModel.DisplayName("Check for Updates")]
        [System.ComponentModel.Description("Periodically check npm for a newer NanoAgent CLI and offer to update (max once per 24h).")]
        public bool CheckForUpdates { get; set; } = true;

        [System.ComponentModel.Category("ACP")]
        [System.ComponentModel.DisplayName("ACP Authentication Token")]
        [System.ComponentModel.Description("Optional token sent when the ACP server requires token auth. Falls back to the NANOAGENT_ACP_AUTH_TOKEN environment variable.")]
        public string AcpAuthenticationToken { get; set; } = "";

        [System.ComponentModel.Category("General")]
        [System.ComponentModel.DisplayName("Log Level")]
        [System.ComponentModel.Description("Verbosity of the extension log written under %LOCALAPPDATA%\\NanoAgent\\Logs.")]
        public NanoAgentLogLevel LogLevel { get; set; } = NanoAgentLogLevel.Info;
    }
}
