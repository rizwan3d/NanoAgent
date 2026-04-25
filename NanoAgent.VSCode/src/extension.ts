import * as vscode from 'vscode';

interface NanoAgentConfig {
    command: string;
    defaultProfile: string;
    thinking: string;
    extraArgs: string;
    promptSendDelayMs: number;
    reuseTerminal: boolean;
}

interface SelectionSnippet {
    languageId: string;
    filePath: string;
    text: string;
}

export function activate(context: vscode.ExtensionContext): void {
    const controller = new NanoAgentController();

    context.subscriptions.push(
        controller,
        vscode.commands.registerCommand('nanoAgent.start', () => controller.start()),
        vscode.commands.registerCommand('nanoAgent.restart', () => controller.restart()),
        vscode.commands.registerCommand('nanoAgent.ask', () => controller.ask()),
        vscode.commands.registerCommand('nanoAgent.explainSelection', () => controller.explainSelection()),
        vscode.commands.registerCommand('nanoAgent.fixSelection', () => controller.fixSelection()),
        vscode.commands.registerCommand('nanoAgent.askAboutFile', (uri?: vscode.Uri) => controller.askAboutFile(uri)),
        vscode.commands.registerCommand('nanoAgent.reviewWorkspace', () => controller.reviewWorkspace()),
        vscode.commands.registerCommand('nanoAgent.openSettings', () => controller.openSettings())
    );
}

export function deactivate(): void {
}

class NanoAgentController implements vscode.Disposable {
    private readonly disposables: vscode.Disposable[] = [];
    private readonly statusItem: vscode.StatusBarItem;
    private terminal: vscode.Terminal | undefined;
    private terminalStarted = false;

    public constructor() {
        this.statusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
        this.statusItem.text = '$(terminal) NanoAgent';
        this.statusItem.tooltip = 'Start NanoAgent';
        this.statusItem.command = 'nanoAgent.start';
        this.statusItem.show();

        this.disposables.push(
            this.statusItem,
            vscode.window.onDidCloseTerminal(terminal => {
                if (terminal === this.terminal) {
                    this.terminal = undefined;
                    this.terminalStarted = false;
                }
            })
        );
    }

    public dispose(): void {
        for (const disposable of this.disposables) {
            disposable.dispose();
        }
    }

    public async start(): Promise<vscode.Terminal> {
        const terminal = this.ensureTerminal();
        terminal.show();

        if (!this.terminalStarted) {
            terminal.sendText(this.buildCommandLine(), true);
            this.terminalStarted = true;
        }

        return terminal;
    }

    public async restart(): Promise<void> {
        if (this.terminal) {
            this.terminal.dispose();
            this.terminal = undefined;
            this.terminalStarted = false;
        }

        await this.start();
    }

    public async ask(): Promise<void> {
        const prompt = await vscode.window.showInputBox({
            title: 'Ask NanoAgent',
            prompt: 'Send a prompt to NanoAgent in the integrated terminal.',
            placeHolder: 'Fix the failing tests, explain this repo, add a feature...'
        });

        if (!prompt || !prompt.trim()) {
            return;
        }

        await this.sendPrompt(prompt.trim());
    }

    public async explainSelection(): Promise<void> {
        const snippet = this.getSelectionSnippet();
        if (!snippet) {
            return;
        }

        await this.sendPrompt(buildSelectionPrompt(
            'Explain this selected code. Focus on behavior, important dependencies, and any risks worth noticing.',
            snippet));
    }

    public async fixSelection(): Promise<void> {
        const snippet = this.getSelectionSnippet();
        if (!snippet) {
            return;
        }

        await this.sendPrompt(buildSelectionPrompt(
            'Fix or improve this selected code. Apply focused edits directly if the change is clear, and keep the surrounding style.',
            snippet));
    }

    public async askAboutFile(uri?: vscode.Uri): Promise<void> {
        const targetUri = uri ?? vscode.window.activeTextEditor?.document.uri;
        if (!targetUri || targetUri.scheme !== 'file') {
            vscode.window.showWarningMessage('Open a file or select one in Explorer before asking NanoAgent about it.');
            return;
        }

        const filePath = vscode.workspace.asRelativePath(targetUri, false);
        const question = await vscode.window.showInputBox({
            title: 'Ask NanoAgent About File',
            prompt: `Ask about ${filePath}.`,
            placeHolder: 'Review this file for bugs, explain how it works, add tests...'
        });

        if (!question || !question.trim()) {
            return;
        }

        await this.sendPrompt([
            `File: ${filePath}`,
            '',
            question.trim()
        ].join('\n'));
    }

    public async reviewWorkspace(): Promise<void> {
        await this.sendPrompt(
            'Review the current workspace for likely bugs, risky code paths, and missing tests. Start from the files that look most relevant and keep the findings actionable.');
    }

    public async openSettings(): Promise<void> {
        await vscode.commands.executeCommand('workbench.action.openSettings', '@ext:rizwan3d.nanoagent-vscode');
    }

    private async sendPrompt(prompt: string): Promise<void> {
        const shouldWaitForStartup = !this.terminalStarted;
        const terminal = await this.start();

        if (shouldWaitForStartup) {
            const delay = Math.max(0, readConfig().promptSendDelayMs);
            if (delay > 0) {
                await sleep(delay);
            }
        }

        sendBracketedPaste(terminal, prompt);
    }

    private ensureTerminal(): vscode.Terminal {
        const config = readConfig();

        if (config.reuseTerminal && this.terminal) {
            return this.terminal;
        }

        const workspaceFolder = getActiveWorkspaceFolder();
        this.terminal = vscode.window.createTerminal({
            name: 'NanoAgent',
            cwd: workspaceFolder?.uri.fsPath
        });
        this.terminalStarted = false;

        return this.terminal;
    }

    private buildCommandLine(): string {
        const config = readConfig();
        const parts = [config.command.trim() || 'nanoai'];

        if (config.defaultProfile) {
            parts.push('--profile', config.defaultProfile);
        }

        if (config.thinking) {
            parts.push('--thinking', config.thinking);
        }

        if (config.extraArgs.trim()) {
            parts.push(config.extraArgs.trim());
        }

        return parts.join(' ');
    }

    private getSelectionSnippet(): SelectionSnippet | undefined {
        const editor = vscode.window.activeTextEditor;
        if (!editor || editor.selection.isEmpty) {
            vscode.window.showWarningMessage('Select code in an editor before using this NanoAgent command.');
            return undefined;
        }

        return {
            languageId: editor.document.languageId,
            filePath: vscode.workspace.asRelativePath(editor.document.uri, false),
            text: editor.document.getText(editor.selection)
        };
    }
}

function readConfig(): NanoAgentConfig {
    const config = vscode.workspace.getConfiguration('nanoAgent');

    return {
        command: config.get<string>('command', 'nanoai'),
        defaultProfile: config.get<string>('defaultProfile', ''),
        thinking: config.get<string>('thinking', ''),
        extraArgs: config.get<string>('extraArgs', ''),
        promptSendDelayMs: config.get<number>('promptSendDelayMs', 1200),
        reuseTerminal: config.get<boolean>('reuseTerminal', true)
    };
}

function getActiveWorkspaceFolder(): vscode.WorkspaceFolder | undefined {
    const activeUri = vscode.window.activeTextEditor?.document.uri;
    if (activeUri) {
        return vscode.workspace.getWorkspaceFolder(activeUri);
    }

    return vscode.workspace.workspaceFolders?.[0];
}

function buildSelectionPrompt(instruction: string, snippet: SelectionSnippet): string {
    const fence = getFence(snippet.text);

    return [
        instruction,
        '',
        `File: ${snippet.filePath}`,
        '',
        `${fence}${snippet.languageId}`,
        snippet.text,
        fence
    ].join('\n');
}

function getFence(text: string): string {
    let fence = '```';
    while (text.includes(fence)) {
        fence += '`';
    }

    return fence;
}

function sleep(milliseconds: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, milliseconds));
}

function sendBracketedPaste(terminal: vscode.Terminal, text: string): void {
    const normalized = text
        .replace(/\r\n/g, '\n')
        .replace(/\r/g, '\n');

    // NanoAgent understands bracketed paste, which preserves multi-line prompts as one input.
    terminal.sendText(`\x1b[200~${normalized}\x1b[201~`, false);
    terminal.sendText('', true);
}
