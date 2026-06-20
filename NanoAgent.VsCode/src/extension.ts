import * as vscode from 'vscode';
import { LogService } from './services/LogService';
import { NanoAgentProcessManager } from './services/NanoAgentProcessManager';
import { registerCommands } from './commands';
import { SessionManager } from './services/SessionManager';
import { registerChatCommands } from './commands/chat';
import { ChatViewProvider } from './webviews/ChatViewProvider';

let processManager: NanoAgentProcessManager;
let sessionManager: SessionManager;

export function activate(context: vscode.ExtensionContext) {
    const logService = LogService.getInstance();
    logService.info(`NanoAgent extension activated (v${context.extension.packageJSON.version}).`);

    processManager = new NanoAgentProcessManager();
    sessionManager = new SessionManager(processManager, context.secrets);
    const chatViewProvider = new ChatViewProvider(sessionManager);

    context.subscriptions.push(
        vscode.window.registerWebviewViewProvider(
            ChatViewProvider.viewType,
            chatViewProvider,
            {
                webviewOptions: {
                    retainContextWhenHidden: true
                }
            }
        )
    );

    registerCommands(context, processManager, logService);
    registerChatCommands(context, sessionManager, chatViewProvider);

    // Check autoStart config
    const config = vscode.workspace.getConfiguration('nanoagent');
    if (config.get<boolean>('autoStart', false)) {
        processManager.start();
    }

    context.subscriptions.push({
        dispose: () => {
            processManager.stop();
            logService.dispose();
        }
    });
}

export function deactivate() {
    if (processManager) {
        return processManager.stop();
    }
}
