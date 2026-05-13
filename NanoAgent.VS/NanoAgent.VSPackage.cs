using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using NanoAgent.VS.Services;
using NanoAgent.VS.ToolWindows;
using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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
            }

            LogService.Instance.Info("NanoAgent.VS package initialized; Show Chat command registered.");
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

    /// <summary>
    /// Options page for NanoAgent settings.
    /// </summary>
    public class NanoAgentOptionsPage : DialogPage
    {
        private string _nanoAgentCliPath = "";
        private string _workingDirectory = "";

        [System.ComponentModel.Category("NanoAgent")]
        [System.ComponentModel.DisplayName("NanoAgent CLI Executable Path")]
        [System.ComponentModel.Description("Optional path or command name for the NanoAgent CLI. Leave empty to use NanoAgent.CLI.exe from the system PATH.")]
        public string NanoAgentCliPath
        {
            get => _nanoAgentCliPath;
            set => _nanoAgentCliPath = value;
        }

        [System.ComponentModel.Category("NanoAgent")]
        [System.ComponentModel.DisplayName("Working Directory")]
        [System.ComponentModel.Description("Default working directory for NanoAgent sessions (leave empty to use solution directory).")]
        public string WorkingDirectory
        {
            get => _workingDirectory;
            set => _workingDirectory = value;
        }
    }
}
