import * as vscode from 'vscode';
import { StemCodeProcessManager } from '../services/StemCodeProcessManager';
import { LogService } from '../services/LogService';

export function registerCommands(context: vscode.ExtensionContext, processManager: StemCodeProcessManager, logService: LogService) {
    context.subscriptions.push(
        vscode.commands.registerCommand('stemcode.start', async () => {
            await processManager.start();
        }),
        
        vscode.commands.registerCommand('stemcode.stop', async () => {
            await processManager.stop();
        }),

        vscode.commands.registerCommand('stemcode.restart', async () => {
            await processManager.restart();
        }),

        vscode.commands.registerCommand('stemcode.openLogs', () => {
            logService.show();
        })
    );
}
