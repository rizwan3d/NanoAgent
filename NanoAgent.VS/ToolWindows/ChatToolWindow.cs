using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using NanoAgent.VS.Services;

namespace NanoAgent.VS.ToolWindows
{
    /// <summary>
    /// Tool window pane for the NanoAgent Chat panel.
    /// </summary>
    [Guid("a8b5c2d3-4e6f-4a7b-9c0d-1e2f3a4b5c6d")]
    public sealed class ChatToolWindow : ToolWindowPane
    {
        private readonly ChatToolWindowControl _control;

        public ChatToolWindow() : base(null)
        {
            Caption = "NanoAgent Chat";
            Content = _control = new ChatToolWindowControl();
        }

        public ChatToolWindowControl Control => _control;

        protected override void OnClose()
        {
            _control.Dispose();
            base.OnClose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _control.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
