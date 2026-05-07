import * as vscode from 'vscode';
import { SessionInfo, SessionManager } from '../services/SessionManager';
import { LogService } from '../services/LogService';
import * as path from 'path';

type ChatMessage = SendMessage | SelectModelMessage | ReadyMessage;

type SendMessage = {
    command: 'sendMessage';
    text: string;
};

type SelectModelMessage = {
    command: 'selectModel';
};

type ReadyMessage = {
    command: 'ready';
};

type ChatCommandSuggestion = {
    command: string;
    usage: string;
    description: string;
    insertText: string;
};

const CHAT_COMMANDS: ChatCommandSuggestion[] = [
    { command: '/allow', usage: '/allow <tool-or-tag> [pattern]', description: 'Add a session-scoped allow override.', insertText: '/allow ' },
    { command: '/budget', usage: '/budget [status|local [path]|cloud]', description: 'Show or configure budget controls.', insertText: '/budget ' },
    { command: '/clear', usage: '/clear', description: 'Clear the chat view.', insertText: '/clear' },
    { command: '/clone', usage: '/clone', description: 'Duplicate the current session.', insertText: '/clone' },
    { command: '/compact', usage: '/compact [retained-turns]', description: 'Manually compact the session context.', insertText: '/compact ' },
    { command: '/config', usage: '/config', description: 'Show provider, profile, thinking, and model details.', insertText: '/config' },
    { command: '/copy', usage: '/copy', description: 'Copy the last agent message to the clipboard.', insertText: '/copy' },
    { command: '/deny', usage: '/deny <tool-or-tag> [pattern]', description: 'Add a session-scoped deny override.', insertText: '/deny ' },
    { command: '/exit', usage: '/exit', description: 'Exit the interactive shell.', insertText: '/exit' },
    { command: '/export', usage: '/export [json|html] [path]', description: 'Export the current session.', insertText: '/export ' },
    { command: '/fork', usage: '/fork [turn-number]', description: 'Create a fork from a previous user message.', insertText: '/fork ' },
    { command: '/help', usage: '/help', description: 'List available commands.', insertText: '/help' },
    { command: '/import', usage: '/import <json-path>', description: 'Import a session from JSON.', insertText: '/import ' },
    { command: '/init', usage: '/init [recommended|minimal|custom]', description: 'Create workspace-local NanoAgent files.', insertText: '/init ' },
    { command: '/ls', usage: '/ls', description: 'List files in the current workspace.', insertText: '/ls' },
    { command: '/mcp', usage: '/mcp', description: 'Show MCP servers and dynamic tools.', insertText: '/mcp' },
    { command: '/models', usage: '/models', description: 'Open the active model picker.', insertText: '/models' },
    { command: '/new', usage: '/new', description: 'Start a new session.', insertText: '/new' },
    { command: '/onboard', usage: '/onboard', description: 'Re-run provider onboarding.', insertText: '/onboard' },
    { command: '/permissions', usage: '/permissions', description: 'Show permission policy and overrides.', insertText: '/permissions' },
    { command: '/provider', usage: '/provider [list|<name>]', description: 'List or switch saved providers.', insertText: '/provider ' },
    { command: '/profile', usage: '/profile <name>', description: 'Switch the active agent profile.', insertText: '/profile ' },
    { command: '/read', usage: '/read <file>', description: 'Read a workspace file after confirmation.', insertText: '/read ' },
    { command: '/redo', usage: '/redo', description: 'Re-apply the most recently undone edit.', insertText: '/redo' },
    { command: '/reload', usage: '/reload', description: 'Reload profiles, skills, prompts, and tools.', insertText: '/reload' },
    { command: '/resume', usage: '/resume [session-id]', description: 'Resume a different session.', insertText: '/resume ' },
    { command: '/rules', usage: '/rules', description: 'List effective permission rules.', insertText: '/rules' },
    { command: '/session', usage: '/session', description: 'Show session info and stats.', insertText: '/session' },
    { command: '/setting', usage: '/setting [area]', description: 'Open configurable NanoAgent settings.', insertText: '/setting ' },
    { command: '/share', usage: '/share', description: 'Share the current session as a secret GitHub gist.', insertText: '/share' },
    { command: '/thinking', usage: '/thinking [on|off]', description: 'Show or set thinking mode.', insertText: '/thinking ' },
    { command: '/tree', usage: '/tree', description: 'Navigate saved sessions and forks.', insertText: '/tree' },
    { command: '/undo', usage: '/undo', description: 'Roll back the most recent tracked edit.', insertText: '/undo' },
    { command: '/update', usage: '/update [now]', description: 'Check for NanoAgent updates.', insertText: '/update ' },
    { command: '/use', usage: '/use <model>', description: 'Switch the active model directly.', insertText: '/use ' }
];

export class ChatWebviewController {
    private readonly disposables: vscode.Disposable[] = [];
    private currentSessionInfo: SessionInfo | null;

    constructor(
        private readonly webview: vscode.Webview,
        private readonly sessionManager: SessionManager
    ) {
        this.currentSessionInfo = this.sessionManager.getSessionInfo();
        this.webview.options = {
            enableScripts: true
        };

        this.webview.html = getChatWebviewContent();

        const sessionInfoListener = (sessionInfo: SessionInfo | null) => {
            this.currentSessionInfo = sessionInfo;
            this.postSessionInfo(sessionInfo);
        };

        this.sessionManager.on('sessionInfoChanged', sessionInfoListener);

        this.disposables.push(
            new vscode.Disposable(() => this.sessionManager.off('sessionInfoChanged', sessionInfoListener)),
            this.webview.onDidReceiveMessage(async (message: ChatMessage) => {
                if (message.command === 'sendMessage') {
                    await this.handleUserMessage(message.text);
                } else if (message.command === 'selectModel') {
                    await this.handleModelSelection();
                } else if (message.command === 'ready') {
                    this.postSessionInfo(this.currentSessionInfo);
                }
            })
        );
    }

    public dispose() {
        while (this.disposables.length) {
            this.disposables.pop()?.dispose();
        }
    }

    public async submitMessage(text: string) {
        const trimmedText = text.trim();
        if (trimmedText) {
            await this.handleUserMessage(trimmedText);
        }
    }

    private async handleUserMessage(text: string) {
        const localCommandHandled = await this.tryHandleLocalCommand(text);
        if (localCommandHandled) {
            return;
        }

        this.webview.postMessage({ command: 'receiveMessage', text, role: 'user' });

        try {
            const responseText = await this.sessionManager.sendPrompt(text);

            if (responseText) {
                this.webview.postMessage({ command: 'receiveMessage', text: responseText, role: 'assistant' });
            }
        } catch (error) {
            const message = error instanceof Error ? error.message : 'Unknown error';
            LogService.getInstance().error('Chat request failed', error);
            this.webview.postMessage({ command: 'receiveMessage', text: `**Error:** ${message}`, role: 'system' });
        }
    }

    private async handleModelSelection() {
        const sessionInfo = this.currentSessionInfo ?? this.sessionManager.getSessionInfo();
        const models = sessionInfo?.availableModelIds ?? [];
        if (models.length === 0) {
            vscode.window.showInformationMessage('No NanoAgent models are available yet.');
            return;
        }

        const picked = await vscode.window.showQuickPick(
            models.map((modelId) => ({
                label: modelId,
                description: modelId === sessionInfo?.modelId ? 'current' : undefined
            })),
            {
                title: 'Choose NanoAgent Model',
                placeHolder: sessionInfo?.modelId ? `Current: ${sessionInfo.modelId}` : 'Choose a model',
                ignoreFocusOut: true
            }
        );

        if (picked) {
            await this.handleUserMessage(`/use ${picked.label}`);
        }
    }

    private async tryHandleLocalCommand(text: string): Promise<boolean> {
        const trimmedText = text.trim();
        if (trimmedText === '/clear') {
            this.webview.postMessage({ command: 'clearMessages' });
            this.postSystemMessage('Screen cleared.');
            return true;
        }

        if (trimmedText === '/ls') {
            this.webview.postMessage({ command: 'receiveMessage', text: trimmedText, role: 'user' });
            await this.listWorkspaceFiles();
            return true;
        }

        if (trimmedText.startsWith('/read ')) {
            this.webview.postMessage({ command: 'receiveMessage', text: trimmedText, role: 'user' });
            await this.readWorkspaceFile(trimmedText.slice('/read '.length).trim());
            return true;
        }

        return false;
    }

    private async listWorkspaceFiles() {
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        if (!workspaceFolder) {
            this.postSystemMessage('Open a workspace before using /ls.');
            return;
        }

        try {
            const files = await vscode.workspace.findFiles(
                new vscode.RelativePattern(workspaceFolder, '**/*'),
                '{**/.git/**,**/node_modules/**,**/bin/**,**/obj/**}',
                100
            );

            if (files.length === 0) {
                this.postSystemMessage('No files found.');
                return;
            }

            const relativeFiles = files
                .map((file) => path.relative(workspaceFolder.uri.fsPath, file.fsPath))
                .sort((left, right) => left.localeCompare(right));

            this.postSystemMessage('Files:\n\n' + relativeFiles.join('\n'));
        } catch (error) {
            const message = error instanceof Error ? error.message : 'Unable to list files.';
            this.postSystemMessage(`Error listing files: ${message}`);
        }
    }

    private async readWorkspaceFile(requestedPath: string) {
        if (!requestedPath) {
            this.postSystemMessage('Usage: /read <file>');
            return;
        }

        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        if (!workspaceFolder) {
            this.postSystemMessage('Open a workspace before using /read.');
            return;
        }

        try {
            const root = workspaceFolder.uri.fsPath;
            const fullPath = path.resolve(root, requestedPath);
            const relativePath = path.relative(root, fullPath);
            if (relativePath.startsWith('..') || path.isAbsolute(relativePath)) {
                this.postSystemMessage('Path escapes workspace.');
                return;
            }

            const permission = await vscode.window.showQuickPick(
                [
                    { label: 'Allow', value: true },
                    { label: 'Deny', value: false }
                ],
                {
                    title: 'Allow local file read?',
                    placeHolder: `Read file '${relativePath}'?`,
                    ignoreFocusOut: true
                }
            );

            if (!permission?.value) {
                this.postSystemMessage(`Permission denied. Did not read file: ${relativePath}`);
                return;
            }

            const content = new TextDecoder().decode(
                await vscode.workspace.fs.readFile(vscode.Uri.file(fullPath))
            );

            this.postSystemMessage(`Permission granted.\n\nFile: ${relativePath}\n\n${content}`);
        } catch (error) {
            const message = error instanceof Error ? error.message : 'Unable to read file.';
            this.postSystemMessage(`Error reading file: ${message}`);
        }
    }

    private postSystemMessage(text: string) {
        this.webview.postMessage({ command: 'receiveMessage', text, role: 'system' });
    }

    private postSessionInfo(sessionInfo: SessionInfo | null) {
        this.webview.postMessage({ command: 'setSessionInfo', sessionInfo });
    }
}

function getChatWebviewContent() {
    const commandSuggestionsJson = JSON.stringify(CHAT_COMMANDS);

    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>NanoAgent Chat</title>
    <style>
        body {
            font-family: var(--vscode-font-family);
            padding: 0;
            margin: 0;
            display: flex;
            flex-direction: column;
            height: 100vh;
            box-sizing: border-box;
            background-color: var(--vscode-editor-background);
            color: var(--vscode-editor-foreground);
        }
        #status-bar {
            display: flex;
            align-items: center;
            gap: 8px;
            padding: 8px 10px;
            border-bottom: 1px solid var(--vscode-sideBarSectionHeader-border);
            min-height: 34px;
            box-sizing: border-box;
        }
        #provider-name {
            color: var(--vscode-descriptionForeground);
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }
        #model-button {
            display: inline-flex;
            align-items: center;
            min-width: 0;
            max-width: 100%;
            padding: 4px 8px;
            border: 1px solid var(--vscode-button-border, transparent);
            border-radius: 4px;
            background-color: var(--vscode-button-secondaryBackground);
            color: var(--vscode-button-secondaryForeground);
        }
        #model-name {
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }
        #messages {
            flex-grow: 1;
            overflow-y: auto;
            padding: 10px;
            display: flex;
            flex-direction: column;
            gap: 10px;
        }
        .message {
            padding: 8px 12px;
            border-radius: 6px;
            max-width: 80%;
            word-wrap: break-word;
        }
        .user {
            align-self: flex-end;
            background-color: var(--vscode-button-background);
            color: var(--vscode-button-foreground);
        }
        .assistant {
            align-self: flex-start;
            background-color: var(--vscode-editorWidget-background);
            border: 1px solid var(--vscode-editorWidget-border);
        }
        .system {
            align-self: center;
            color: var(--vscode-errorForeground);
            font-size: 0.9em;
            white-space: pre-wrap;
        }
        #composer {
            position: relative;
            padding: 0 10px 10px;
        }
        #input-container {
            display: flex;
            gap: 8px;
        }
        #chat-input {
            flex-grow: 1;
            padding: 8px;
            background-color: var(--vscode-input-background);
            color: var(--vscode-input-foreground);
            border: 1px solid var(--vscode-input-border);
            border-radius: 4px;
        }
        button {
            padding: 8px 16px;
            background-color: var(--vscode-button-background);
            color: var(--vscode-button-foreground);
            border: none;
            border-radius: 4px;
            cursor: pointer;
        }
        button:hover {
            background-color: var(--vscode-button-hoverBackground);
        }
        #suggestions {
            position: absolute;
            left: 10px;
            right: 10px;
            bottom: 48px;
            max-height: 220px;
            overflow-y: auto;
            border: 1px solid var(--vscode-editorWidget-border);
            background-color: var(--vscode-editorWidget-background);
            box-shadow: 0 4px 12px rgba(0, 0, 0, 0.28);
            z-index: 2;
        }
        #suggestions.hidden {
            display: none;
        }
        .suggestion {
            padding: 7px 9px;
            cursor: pointer;
            border-bottom: 1px solid var(--vscode-editorWidget-border);
        }
        .suggestion:last-child {
            border-bottom: 0;
        }
        .suggestion.active,
        .suggestion:hover {
            background-color: var(--vscode-list-activeSelectionBackground);
            color: var(--vscode-list-activeSelectionForeground);
        }
        .suggestion-usage {
            font-weight: 600;
            overflow-wrap: anywhere;
        }
        .suggestion-description {
            color: var(--vscode-descriptionForeground);
            font-size: 0.9em;
            margin-top: 2px;
        }
        .suggestion.active .suggestion-description,
        .suggestion:hover .suggestion-description {
            color: inherit;
        }
    </style>
</head>
<body>
    <div id="status-bar">
        <button id="model-button" title="Select model">
            <span id="model-name">No model</span>
        </button>
        <span id="provider-name"></span>
    </div>
    <div id="messages"></div>
    <div id="composer">
        <div id="suggestions" class="hidden"></div>
        <div id="input-container">
            <input type="text" id="chat-input" placeholder="Ask NanoAgent or type / for commands..." />
            <button id="send-button">Send</button>
        </div>
    </div>

    <script>
        const vscode = acquireVsCodeApi();
        const commandSuggestions = ${commandSuggestionsJson};
        const messagesDiv = document.getElementById('messages');
        const inputField = document.getElementById('chat-input');
        const sendButton = document.getElementById('send-button');
        const suggestionsDiv = document.getElementById('suggestions');
        const modelButton = document.getElementById('model-button');
        const modelName = document.getElementById('model-name');
        const providerName = document.getElementById('provider-name');
        let sessionInfo = null;
        let visibleSuggestions = [];
        let activeSuggestionIndex = 0;

        function appendMessage(text, role) {
            const div = document.createElement('div');
            div.className = 'message ' + role;
            div.textContent = text;
            messagesDiv.appendChild(div);
            messagesDiv.scrollTop = messagesDiv.scrollHeight;
        }

        function sendCurrentInput() {
            const text = inputField.value.trim();
            if (text) {
                vscode.postMessage({ command: 'sendMessage', text });
                inputField.value = '';
                hideSuggestions();
            }
        }

        sendButton.addEventListener('click', () => {
            sendCurrentInput();
        });

        inputField.addEventListener('keydown', (e) => {
            if (visibleSuggestions.length > 0) {
                if (e.key === 'ArrowDown') {
                    e.preventDefault();
                    activeSuggestionIndex = (activeSuggestionIndex + 1) % visibleSuggestions.length;
                    renderSuggestions();
                    return;
                }

                if (e.key === 'ArrowUp') {
                    e.preventDefault();
                    activeSuggestionIndex = (activeSuggestionIndex - 1 + visibleSuggestions.length) % visibleSuggestions.length;
                    renderSuggestions();
                    return;
                }

                if (e.key === 'Tab') {
                    e.preventDefault();
                    applySuggestion(visibleSuggestions[activeSuggestionIndex]);
                    return;
                }

                if (e.key === 'Escape') {
                    hideSuggestions();
                    return;
                }
            }

            if (e.key === 'Enter') {
                sendCurrentInput();
            }
        });

        inputField.addEventListener('input', updateSuggestions);
        inputField.addEventListener('blur', () => setTimeout(hideSuggestions, 150));
        modelButton.addEventListener('click', () => {
            vscode.postMessage({ command: 'selectModel' });
        });

        window.addEventListener('message', event => {
            const message = event.data;
            if (message.command === 'receiveMessage') {
                appendMessage(message.text, message.role);
            } else if (message.command === 'clearMessages') {
                messagesDiv.textContent = '';
            } else if (message.command === 'setSessionInfo') {
                setSessionInfo(message.sessionInfo);
            }
        });

        function setSessionInfo(nextSessionInfo) {
            sessionInfo = nextSessionInfo;
            modelName.textContent = sessionInfo && sessionInfo.modelId ? sessionInfo.modelId : 'No model';
            providerName.textContent = sessionInfo && sessionInfo.providerName ? sessionInfo.providerName : '';
            modelButton.disabled = !sessionInfo || !sessionInfo.availableModelIds || sessionInfo.availableModelIds.length === 0;
            updateSuggestions();
        }

        function updateSuggestions() {
            const value = inputField.value;
            visibleSuggestions = createSuggestions(value);
            activeSuggestionIndex = 0;
            renderSuggestions();
        }

        function createSuggestions(value) {
            if (!value.startsWith('/')) {
                return [];
            }

            if (value.startsWith('/use ')) {
                const query = value.slice('/use '.length).trim().toLowerCase();
                const models = sessionInfo && Array.isArray(sessionInfo.availableModelIds)
                    ? sessionInfo.availableModelIds
                    : [];

                return models
                    .filter(modelId => !query || modelId.toLowerCase().includes(query))
                    .slice(0, 8)
                    .map(modelId => ({
                        usage: modelId,
                        description: modelId === (sessionInfo && sessionInfo.modelId) ? 'Current model' : 'Switch model',
                        insertText: '/use ' + modelId
                    }));
            }

            const token = value.split(/\\s+/, 1)[0].toLowerCase();
            return commandSuggestions
                .filter(command => command.command.startsWith(token) || command.usage.toLowerCase().startsWith(token))
                .slice(0, 10);
        }

        function renderSuggestions() {
            suggestionsDiv.textContent = '';
            if (visibleSuggestions.length === 0) {
                hideSuggestions();
                return;
            }

            suggestionsDiv.classList.remove('hidden');
            visibleSuggestions.forEach((suggestion, index) => {
                const item = document.createElement('div');
                item.className = 'suggestion' + (index === activeSuggestionIndex ? ' active' : '');
                item.addEventListener('mousedown', event => {
                    event.preventDefault();
                    applySuggestion(suggestion);
                });

                const usage = document.createElement('div');
                usage.className = 'suggestion-usage';
                usage.textContent = suggestion.usage;
                item.appendChild(usage);

                const description = document.createElement('div');
                description.className = 'suggestion-description';
                description.textContent = suggestion.description;
                item.appendChild(description);

                suggestionsDiv.appendChild(item);
            });
        }

        function applySuggestion(suggestion) {
            inputField.value = suggestion.insertText;
            inputField.focus();
            inputField.setSelectionRange(inputField.value.length, inputField.value.length);
            updateSuggestions();
        }

        function hideSuggestions() {
            visibleSuggestions = [];
            suggestionsDiv.classList.add('hidden');
            suggestionsDiv.textContent = '';
        }

        vscode.postMessage({ command: 'ready' });
    </script>
</body>
</html>`;
}
