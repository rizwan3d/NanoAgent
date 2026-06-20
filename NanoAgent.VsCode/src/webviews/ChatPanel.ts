import * as vscode from 'vscode';
import { SessionManager } from '../services/SessionManager';
import { ChatWebviewController } from './ChatWebviewController';

export class ChatPanel {
    public static currentPanel: ChatPanel | undefined;

    private readonly controller: ChatWebviewController;
    private readonly disposables: vscode.Disposable[] = [];

    private constructor(
        private readonly panel: vscode.WebviewPanel,
        sessionManager: SessionManager,
        extensionUri: vscode.Uri
    ) {
        this.controller = new ChatWebviewController(panel.webview, sessionManager, extensionUri);

        this.disposables.push(
            this.panel.onDidDispose(() => this.dispose())
        );
    }

    public static createOrShow(sessionManager: SessionManager, extensionUri: vscode.Uri) {
        if (ChatPanel.currentPanel) {
            ChatPanel.currentPanel.panel.reveal(vscode.ViewColumn.Beside);
            return;
        }

        const panel = vscode.window.createWebviewPanel(
            'nanoAgentChat',
            'NanoAgent Chat',
            vscode.ViewColumn.Beside,
            {
                enableScripts: true,
                retainContextWhenHidden: true
            }
        );

        ChatPanel.currentPanel = new ChatPanel(panel, sessionManager, extensionUri);
        void sessionManager.ensureStarted().catch((error) => {
            const message = error instanceof Error ? error.message : 'Unable to start NanoAgent.';
            vscode.window.showErrorMessage(message);
        });
    }

    public dispose() {
        ChatPanel.currentPanel = undefined;
        this.controller.dispose();

        while (this.disposables.length) {
            this.disposables.pop()?.dispose();
        }
    }

    public async submitMessage(text: string) {
        await this.controller.submitMessage(text);
    }

    public prefillMessage(text: string) {
        this.controller.prefillMessage(text);
    }

    public showSettings() {
        this.controller.showSettings();
    }

    public async startNewSession() {
        await this.controller.startNewSession();
    }
}
