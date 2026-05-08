import * as vscode from 'vscode';
import { execFile } from 'child_process';
import { ChatPanel } from '../webviews/ChatPanel';
import { SessionManager } from '../services/SessionManager';
import { ChatViewProvider } from '../webviews/ChatViewProvider';
import { LogService } from '../services/LogService';

export function registerChatCommands(
    context: vscode.ExtensionContext,
    sessionManager: SessionManager,
    chatViewProvider: ChatViewProvider
) {
    const openChat = async () => {
        try {
            await vscode.commands.executeCommand('workbench.view.extension.nanoagent');
            await vscode.commands.executeCommand(`${ChatViewProvider.viewType}.focus`);
        } catch (error) {
            LogService.getInstance().warn('Falling back to NanoAgent chat webview panel', error);
            ChatPanel.createOrShow(sessionManager);
        }

        await sessionManager.ensureStarted();
    };

    const submitPrompt = async (prompt: string) => {
        await openChat();

        if (await chatViewProvider.submitMessage(prompt)) {
            return;
        }

        ChatPanel.createOrShow(sessionManager);
        await ChatPanel.currentPanel?.submitMessage(prompt);
    };

    const prefillPrompt = async (prompt: string) => {
        await openChat();

        if (chatViewProvider.prefillMessage(prompt)) {
            return;
        }

        ChatPanel.createOrShow(sessionManager);
        ChatPanel.currentPanel?.prefillMessage(prompt);
    };

    context.subscriptions.push(
        vscode.commands.registerCommand('nanoagent.openChat', openChat),
        vscode.commands.registerCommand('nanoagent.newChat', openChat),
        vscode.commands.registerCommand('nanoagent.sendSelection', async () => {
            const selectedText = getSelectedText();
            if (!selectedText) {
                vscode.window.showWarningMessage('Select code or text before sending it to NanoAgent.');
                return;
            }

            await submitPrompt(formatEditorPrompt('Use this selection as context:', selectedText));
        }),
        vscode.commands.registerCommand('nanoagent.explainSelection', async () => {
            const selectedText = getSelectedText();
            if (!selectedText) {
                vscode.window.showWarningMessage('Select code or text before asking NanoAgent to explain it.');
                return;
            }

            await submitPrompt(formatEditorPrompt('Explain this selection:', selectedText));
        }),
        vscode.commands.registerCommand('nanoagent.sendCurrentFile', async () => {
            const editorText = getCurrentEditorText();
            if (!editorText) {
                vscode.window.showWarningMessage('Open a file before sending it to NanoAgent.');
                return;
            }

            await submitPrompt(formatEditorPrompt('Use this file as context:', editorText));
        }),
        vscode.commands.registerCommand('nanoagent.reviewCurrentFile', async () => {
            const editorText = getCurrentEditorText();
            if (!editorText) {
                vscode.window.showWarningMessage('Open a file before asking NanoAgent to review it.');
                return;
            }

            await submitPrompt(formatEditorPrompt('Review this file for bugs, regressions, and missing tests:', editorText));
        }),
        vscode.commands.registerCommand('nanoagent.reviewGitDiff', async () => {
            try {
                const diff = await readGitDiff();
                if (!diff.trim()) {
                    vscode.window.showInformationMessage('No git diff found for NanoAgent to review.');
                    return;
                }

                await submitPrompt(`Review this git diff for bugs, regressions, and missing tests:\n\n\`\`\`diff\n${diff}\n\`\`\``);
            } catch (error) {
                const message = error instanceof Error ? error.message : 'Unable to read git diff.';
                vscode.window.showErrorMessage(message);
            }
        }),
        vscode.commands.registerCommand('nanoagent.planChanges', async () => {
            await prefillPrompt('Plan the following change before editing:\n\n');
        }),
        vscode.commands.registerCommand('nanoagent.applySuggestedChanges', async () => {
            await submitPrompt('Apply the suggested changes from the previous NanoAgent response.');
        })
    );
}

function getSelectedText(): EditorText | undefined {
    const editor = vscode.window.activeTextEditor;
    if (!editor || editor.selection.isEmpty) {
        return undefined;
    }

    return {
        fileName: editor.document.fileName,
        languageId: editor.document.languageId,
        text: editor.document.getText(editor.selection)
    };
}

function getCurrentEditorText(): EditorText | undefined {
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
        return undefined;
    }

    return {
        fileName: editor.document.fileName,
        languageId: editor.document.languageId,
        text: editor.document.getText()
    };
}

function formatEditorPrompt(instruction: string, editorText: EditorText): string {
    return `${instruction}\n\nFile: ${editorText.fileName}\n\n\`\`\`${editorText.languageId}\n${editorText.text}\n\`\`\``;
}

function readGitDiff(): Promise<string> {
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
    if (!workspaceFolder) {
        return Promise.reject(new Error('Open a workspace before asking NanoAgent to review a git diff.'));
    }

    return new Promise((resolve, reject) => {
        execFile(
            'git',
            ['diff', '--no-ext-diff', 'HEAD'],
            {
                cwd: workspaceFolder.uri.fsPath,
                maxBuffer: 1024 * 1024 * 20
            },
            (error, stdout, stderr) => {
                if (error && !stdout) {
                    reject(new Error(stderr.trim() || error.message));
                    return;
                }

                resolve(stdout);
            }
        );
    });
}

type EditorText = {
    fileName: string;
    languageId: string;
    text: string;
};
