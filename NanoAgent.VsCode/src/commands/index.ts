import * as vscode from 'vscode';
import { NanoAgentProcessManager } from '../services/NanoAgentProcessManager';
import { LogService } from '../services/LogService';

export function registerCommands(context: vscode.ExtensionContext, processManager: NanoAgentProcessManager, logService: LogService) {
    context.subscriptions.push(
        vscode.commands.registerCommand('nanoagent.start', async () => {
            await processManager.start();
        }),
        
        vscode.commands.registerCommand('nanoagent.stop', async () => {
            await processManager.stop();
        }),

        vscode.commands.registerCommand('nanoagent.restart', async () => {
            await processManager.restart();
        }),

        vscode.commands.registerCommand('nanoagent.openLogs', () => {
            logService.show();
        })
    );
}
