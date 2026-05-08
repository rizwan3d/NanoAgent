import * as vscode from 'vscode';
import { SessionManager } from '../services/SessionManager';
import { ChatWebviewController } from './ChatWebview';

export class ChatViewProvider implements vscode.WebviewViewProvider {
    public static readonly viewType = 'nanoagent.chatView';

    private controller: ChatWebviewController | undefined;

    constructor(private readonly sessionManager: SessionManager) {
    }

    public resolveWebviewView(webviewView: vscode.WebviewView) {
        this.controller?.dispose();
        this.controller = new ChatWebviewController(webviewView.webview, this.sessionManager);
        void this.sessionManager.ensureStarted().catch((error) => {
            const message = error instanceof Error ? error.message : 'Unable to start NanoAgent.';
            vscode.window.showErrorMessage(message);
        });

        webviewView.onDidDispose(() => {
            this.controller?.dispose();
            this.controller = undefined;
        });
    }

    public async submitMessage(text: string): Promise<boolean> {
        if (!this.controller) {
            return false;
        }

        await this.controller.submitMessage(text);
        return true;
    }

    public prefillMessage(text: string): boolean {
        if (!this.controller) {
            return false;
        }

        this.controller.prefillMessage(text);
        return true;
    }
}
