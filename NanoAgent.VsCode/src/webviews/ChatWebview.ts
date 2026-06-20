import * as path from 'path';
import * as vscode from 'vscode';
import { buildDiffModel } from './diffView';
import { LogService } from '../services/LogService';
import {
    ClientRequest,
    ClientRequestResolution,
    PermissionClientRequest,
    PlanUpdate,
    PromptState,
    SessionInfo,
    SessionManager,
    SessionMessageChunk,
    SessionSummaryInfo,
    ToolCallUpdate,
    TurnMetrics
} from '../services/SessionManager';
import { NanoAgentProcessStatus } from '../services/NanoAgentProcessManager';

type ChatMessage =
    | SendMessage
    | RunSessionCommandMessage
    | SelectModelMessage
    | ChangeModelMessage
    | ChangeProfileMessage
    | OpenVsCodeSettingsMessage
    | ReadyMessage
    | ResolveClientRequestMessage
    | CancelPromptMessage
    | OpenFileMessage
    | SearchFilesMessage
    | ListSessionsMessage
    | ResumeSessionMessage;

type SendMessage = {
    command: 'sendMessage';
    text: string;
};

type RunSessionCommandMessage = {
    command: 'runSessionCommand';
    text: string;
};

type SelectModelMessage = {
    command: 'selectModel';
};

type ChangeModelMessage = {
    command: 'changeModel';
    modelId: string;
};

type ChangeProfileMessage = {
    command: 'changeProfile';
    profileName: string;
};

type OpenVsCodeSettingsMessage = {
    command: 'openVsCodeSettings';
};

type ReadyMessage = {
    command: 'ready';
};

type ResolveClientRequestMessage = {
    command: 'resolveClientRequest';
    requestId: string;
    resolution: ClientRequestResolution;
};

type CancelPromptMessage = {
    command: 'cancelPrompt';
};

type OpenFileMessage = {
    command: 'openFile';
    filePath: string;
    line?: number;
    column?: number;
};

type SearchFilesMessage = {
    command: 'searchFiles';
    query: string;
};

type ListSessionsMessage = {
    command: 'listSessions';
};

type ResumeSessionMessage = {
    command: 'resumeSession';
    sessionId: string;
};

type ChatCommandSuggestion = {
    command: string;
    usage: string;
    description: string;
    insertText: string;
};

const CHAT_COMMANDS: ChatCommandSuggestion[] = [
    { command: '/a', usage: '/a', description: 'Alias for /agent.', insertText: '/a' },
    { command: '/allow', usage: '/allow <tool-or-tag> [pattern]', description: 'Add a session-scoped allow override.', insertText: '/allow ' },
    { command: '/agent', usage: '/agent', description: 'List available subagents.', insertText: '/agent' },
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
    { command: '/terminals', usage: '/terminals [stop <id>|stop all]', description: 'List or stop background terminals.', insertText: '/terminals' },
    { command: '/thinking', usage: '/thinking [on|off]', description: 'Show or set thinking mode.', insertText: '/thinking ' },
    { command: '/tree', usage: '/tree', description: 'Navigate saved sessions and forks.', insertText: '/tree' },
    { command: '/undo', usage: '/undo', description: 'Roll back the most recent tracked edit.', insertText: '/undo' },
    { command: '/update', usage: '/update [now]', description: 'Check for NanoAgent updates.', insertText: '/update ' },
    { command: '/use', usage: '/use <model>', description: 'Switch the active model directly.', insertText: '/use ' }
];

export class ChatWebviewController {
    private readonly disposables: vscode.Disposable[] = [];
    private readonly localRequestResolvers = new Map<string, (resolution: ClientRequestResolution) => void>();
    private readonly toolCalls = new Map<string, ToolCallUpdate>();
    private currentPlan: PlanUpdate | null = null;
    private currentSessionInfo: SessionInfo | null;
    private localRequestCounter = 0;

    constructor(
        private readonly webview: vscode.Webview,
        private readonly sessionManager: SessionManager
    ) {
        this.currentSessionInfo = this.sessionManager.getSessionInfo();
        this.webview.options = {
            enableScripts: true
        };

        const nonce = getNonce();
        this.webview.html = getChatWebviewContent(nonce);
        this.registerSessionListeners();

        this.disposables.push(
            this.webview.onDidReceiveMessage(async (message: ChatMessage) => {
                if (message.command === 'sendMessage') {
                    await this.handleUserMessage(message.text);
                } else if (message.command === 'runSessionCommand') {
                    await this.runSettingsCommand(message.text);
                } else if (message.command === 'selectModel') {
                    await this.handleModelSelection();
                } else if (message.command === 'changeModel') {
                    await this.handleModelChange(message.modelId);
                } else if (message.command === 'changeProfile') {
                    await this.handleProfileChange(message.profileName);
                } else if (message.command === 'openVsCodeSettings') {
                    await vscode.commands.executeCommand('workbench.action.openSettings', 'nanoagent');
                } else if (message.command === 'resolveClientRequest') {
                    this.resolveClientRequest(message.requestId, message.resolution);
                } else if (message.command === 'cancelPrompt') {
                    this.sessionManager.cancelPrompt();
                } else if (message.command === 'openFile') {
                    await this.openFileFromChat(message.filePath, message.line, message.column);
                } else if (message.command === 'searchFiles') {
                    await this.searchFileMentions(message.query);
                } else if (message.command === 'listSessions') {
                    await this.handleListSessions();
                } else if (message.command === 'resumeSession') {
                    await this.resumeChatSession(message.sessionId);
                } else if (message.command === 'ready') {
                    this.postInitialState();
                    await this.ensureSessionReady();
                }
            })
        );
    }

    public dispose() {
        for (const resolve of this.localRequestResolvers.values()) {
            resolve({ outcome: 'cancelled' });
        }

        this.localRequestResolvers.clear();

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

    public async startNewSession() {
        await this.startNewChatSession();
    }

    public prefillMessage(text: string) {
        this.webview.postMessage({ command: 'prefillComposer', text });
    }

    public showSettings() {
        this.webview.postMessage({ command: 'showSettings' });
    }

    private registerSessionListeners() {
        const processStatusListener = (status: NanoAgentProcessStatus) => {
            this.postProcessStatus(status);
        };
        const promptStateListener = (state: PromptState) => {
            this.postPromptState(state);
        };
        const sessionInfoListener = (sessionInfo: SessionInfo | null) => {
            this.currentSessionInfo = sessionInfo;
            this.postSessionInfo(sessionInfo);
        };
        const messageChunkListener = (chunk: SessionMessageChunk) => {
            this.webview.postMessage({ command: 'appendMessageChunk', chunk });
        };
        const toolCallListener = (update: ToolCallUpdate) => {
            const merged = this.mergeToolCall(update);
            this.webview.postMessage({ command: 'setToolCall', toolCall: merged });
        };
        const planListener = (plan: PlanUpdate) => {
            this.currentPlan = plan;
            this.webview.postMessage({ command: 'setPlan', plan });
        };
        const clientRequestListener = (request: ClientRequest) => {
            this.postClientRequest(request);
        };
        const clientRequestResolvedListener = (requestId: string) => {
            this.webview.postMessage({ command: 'resolveClientRequest', requestId });
        };

        this.sessionManager.on('processStatusChanged', processStatusListener);
        this.sessionManager.on('promptStateChanged', promptStateListener);
        this.sessionManager.on('sessionInfoChanged', sessionInfoListener);
        this.sessionManager.on('messageChunk', messageChunkListener);
        this.sessionManager.on('toolCallUpdated', toolCallListener);
        this.sessionManager.on('planUpdated', planListener);
        this.sessionManager.on('clientRequest', clientRequestListener);
        this.sessionManager.on('clientRequestResolved', clientRequestResolvedListener);

        this.disposables.push(
            new vscode.Disposable(() => this.sessionManager.off('processStatusChanged', processStatusListener)),
            new vscode.Disposable(() => this.sessionManager.off('promptStateChanged', promptStateListener)),
            new vscode.Disposable(() => this.sessionManager.off('sessionInfoChanged', sessionInfoListener)),
            new vscode.Disposable(() => this.sessionManager.off('messageChunk', messageChunkListener)),
            new vscode.Disposable(() => this.sessionManager.off('toolCallUpdated', toolCallListener)),
            new vscode.Disposable(() => this.sessionManager.off('planUpdated', planListener)),
            new vscode.Disposable(() => this.sessionManager.off('clientRequest', clientRequestListener)),
            new vscode.Disposable(() => this.sessionManager.off('clientRequestResolved', clientRequestResolvedListener))
        );
    }

    private postInitialState() {
        this.postProcessStatus(this.sessionManager.getProcessStatus());
        this.postPromptState(this.sessionManager.getPromptState());
        this.postSessionInfo(this.currentSessionInfo);

        if (this.currentPlan) {
            this.webview.postMessage({ command: 'setPlan', plan: this.currentPlan });
        }

        for (const toolCall of this.toolCalls.values()) {
            this.webview.postMessage({ command: 'setToolCall', toolCall });
        }

        for (const request of this.sessionManager.getPendingClientRequests()) {
            this.postClientRequest(request);
        }
    }

    private async handleUserMessage(text: string) {
        const trimmedText = text.trim();
        if (trimmedText === '/clear') {
            this.webview.postMessage({ command: 'clearMessages' });
            this.postSystemMessage('Screen cleared.');
            return;
        }

        if (trimmedText === '/models') {
            await this.handleModelSelection();
            return;
        }

        if (trimmedText === '/new') {
            await this.startNewChatSession();
            return;
        }

        if (trimmedText.startsWith('/resume ')) {
            await this.resumeChatSession(trimmedText.slice('/resume '.length).trim());
            return;
        }

        this.postChatMessage(trimmedText, 'user');

        if (trimmedText === '/ls') {
            await this.listWorkspaceFiles();
            return;
        }

        if (trimmedText.startsWith('/read ')) {
            await this.readWorkspaceFile(trimmedText.slice('/read '.length).trim());
            return;
        }

        try {
            const result = await this.sessionManager.sendPrompt(trimmedText);
            if (!trimmedText.startsWith('/')) {
                this.postTurnMetrics(result?.metrics);
            }
        } catch (error) {
            const message = error instanceof Error ? error.message : 'Unknown error';
            LogService.getInstance().error('Chat request failed', error);
            this.postSystemMessage(`Error: ${message}`);
        }
    }

    private postTurnMetrics(metrics: TurnMetrics | undefined) {
        if (!metrics) {
            return;
        }

        const parts: string[] = [];
        if (typeof metrics.elapsedMilliseconds === 'number' && metrics.elapsedMilliseconds > 0) {
            const seconds = metrics.elapsedMilliseconds / 1000;
            parts.push(seconds >= 10 ? `${Math.round(seconds)}s` : `${seconds.toFixed(1)}s`);
        }

        const outputTokens = metrics.displayedEstimatedOutputTokens ?? metrics.estimatedOutputTokens;
        if (typeof outputTokens === 'number' && outputTokens > 0) {
            parts.push(`${formatTokenCount(outputTokens)} tokens`);
        }

        if (typeof metrics.toolRoundCount === 'number' && metrics.toolRoundCount > 0) {
            parts.push(`${metrics.toolRoundCount} tool ${metrics.toolRoundCount === 1 ? 'round' : 'rounds'}`);
        }

        if (typeof metrics.providerRetryCount === 'number' && metrics.providerRetryCount > 0) {
            parts.push(`${metrics.providerRetryCount} retries`);
        }

        if (parts.length === 0) {
            return;
        }

        this.postChatMessage(parts.join(' · '), 'metrics');
    }

    private async handleModelSelection() {
        await this.runSessionCommand('/models', 'Model selection failed');
    }

    private async runSettingsCommand(text: string) {
        const trimmedText = text.trim();
        if (!trimmedText.startsWith('/')) {
            return;
        }

        await this.runSessionCommand(trimmedText, 'Settings command failed');
    }

    private async ensureSessionReady() {
        try {
            await this.sessionManager.ensureSessionReady();
            this.currentSessionInfo = this.sessionManager.getSessionInfo();
            this.postInitialState();
        } catch (error) {
            const message = error instanceof Error ? error.message : 'Unable to initialize NanoAgent session.';
            LogService.getInstance().error('Session initialization failed', error);
            this.postSystemMessage(`Error: ${message}`);
        }
    }

    private async handleModelChange(modelId: string) {
        const trimmedModelId = modelId.trim();
        const sessionInfo = this.currentSessionInfo ?? this.sessionManager.getSessionInfo();
        const models = sessionInfo?.availableModelIds ?? [];
        if (!trimmedModelId || !models.includes(trimmedModelId) || trimmedModelId === sessionInfo?.modelId) {
            return;
        }

        try {
            await this.sessionManager.sendPrompt(`/use ${trimmedModelId}`);
        } catch (error) {
            const message = error instanceof Error ? error.message : 'Unknown error';
            LogService.getInstance().error('Model change failed', error);
            this.postSystemMessage(`Error: ${message}`);
        }
    }

    private async handleProfileChange(profileName: string) {
        const trimmedProfileName = profileName.trim();
        const sessionInfo = this.currentSessionInfo ?? this.sessionManager.getSessionInfo();
        if (!trimmedProfileName ||
            (sessionInfo?.agentProfileName &&
                trimmedProfileName.toLowerCase() === sessionInfo.agentProfileName.toLowerCase())) {
            return;
        }

        const profiles = sessionInfo?.availableAgentProfiles ?? [];
        if (profiles.length > 0 &&
            !profiles.some(profile => profile.name.toLowerCase() === trimmedProfileName.toLowerCase())) {
            return;
        }

        await this.runSessionCommand(`/profile ${trimmedProfileName}`, 'Profile change failed');
    }

    private async runSessionCommand(commandText: string, logContext: string) {
        try {
            await this.sessionManager.sendPrompt(commandText);
        } catch (error) {
            const message = error instanceof Error ? error.message : 'Unknown error';
            LogService.getInstance().error(logContext, error);
            this.postSystemMessage(`Error: ${message}`);
        }
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

    private async searchFileMentions(query: string) {
        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        if (!workspaceFolder) {
            this.webview.postMessage({ command: 'fileMentions', query, files: [] });
            return;
        }

        try {
            // ponytail: re-scans (capped) per query. Cache the file list if it lags on huge repos.
            const files = await vscode.workspace.findFiles(
                new vscode.RelativePattern(workspaceFolder, '**/*'),
                '{**/.git/**,**/node_modules/**,**/bin/**,**/obj/**,**/dist/**,**/out/**}',
                2000
            );

            const normalizedQuery = query.trim().toLowerCase();
            const relativeFiles = files
                .map((file) => path.relative(workspaceFolder.uri.fsPath, file.fsPath).replace(/\\/g, '/'))
                .filter((file) => !normalizedQuery || file.toLowerCase().includes(normalizedQuery))
                .sort((left, right) => left.localeCompare(right))
                .slice(0, 20);

            this.webview.postMessage({ command: 'fileMentions', query, files: relativeFiles });
        } catch (error) {
            LogService.getInstance().error('File mention search failed', error);
            this.webview.postMessage({ command: 'fileMentions', query, files: [] });
        }
    }

    private async handleListSessions() {
        try {
            const sessions: SessionSummaryInfo[] = await this.sessionManager.listSessions();
            this.webview.postMessage({
                command: 'sessionList',
                sessions,
                currentSessionId: this.currentSessionInfo?.sessionId ?? this.sessionManager.getSessionInfo()?.sessionId ?? ''
            });
        } catch (error) {
            const message = error instanceof Error ? error.message : 'Unable to list sessions.';
            LogService.getInstance().error('List sessions failed', error);
            this.postSystemMessage(`Error: ${message}`);
        }
    }

    private async startNewChatSession() {
        this.clearSessionActivity();

        try {
            const sessionId = await this.sessionManager.startNewSession();
            this.currentSessionInfo = this.sessionManager.getSessionInfo();
            this.webview.postMessage({ command: 'clearMessages' });
            this.postSystemMessage(`Started new NanoAgent session: ${sessionId}`);
            this.postInitialState();
        } catch (error) {
            const message = error instanceof Error ? error.message : 'Unable to start a new NanoAgent session.';
            LogService.getInstance().error('New session failed', error);
            this.postSystemMessage(`Error: ${message}`);
        }
    }

    private async resumeChatSession(sessionId: string) {
        if (!sessionId) {
            this.postSystemMessage('Usage: /resume <session-id>');
            return;
        }

        this.clearSessionActivity();

        try {
            await this.sessionManager.loadSession(sessionId);
            this.currentSessionInfo = this.sessionManager.getSessionInfo();
            this.webview.postMessage({ command: 'clearMessages' });
            this.postSystemMessage(`Resumed NanoAgent session: ${sessionId}`);
            this.postInitialState();
        } catch (error) {
            const message = error instanceof Error ? error.message : 'Unable to resume NanoAgent session.';
            LogService.getInstance().error('Resume session failed', error);
            this.postSystemMessage(`Error: ${message}`);
        }
    }

    private clearSessionActivity() {
        this.currentPlan = null;
        this.toolCalls.clear();
        this.webview.postMessage({ command: 'setPlan', plan: null });
        this.webview.postMessage({ command: 'clearToolCalls' });
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

            const permissionRequest: PermissionClientRequest = {
                id: this.createLocalRequestId('read'),
                kind: 'permission',
                title: 'Allow local file read?',
                description: `Read file '${relativePath}'?`,
                options: [
                    { optionId: 'allow', name: 'Allow', kind: 'allow_once' },
                    { optionId: 'deny', name: 'Deny', kind: 'reject_once' }
                ],
                allowCancellation: true
            };
            const resolution = await this.requestFromWebview(permissionRequest);

            if (resolution.outcome !== 'selected' || resolution.optionId !== 'allow') {
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

    private async openFileFromChat(filePath: string, line?: number, column?: number) {
        const parsed = this.parseOpenFileRequest(filePath, line, column);
        if (!parsed.filePath) {
            return;
        }

        const candidates = this.resolveOpenFileCandidates(parsed.filePath);
        for (const candidate of candidates) {
            const uri = vscode.Uri.file(candidate);
            try {
                const stat = await vscode.workspace.fs.stat(uri);
                if (stat.type === vscode.FileType.Directory) {
                    continue;
                }

                const document = await vscode.workspace.openTextDocument(uri);
                const position = typeof parsed.line === 'number'
                    ? new vscode.Position(
                        Math.max(0, parsed.line - 1),
                        Math.max(0, (parsed.column ?? 1) - 1))
                    : undefined;
                await vscode.window.showTextDocument(document, {
                    preview: true,
                    selection: position
                        ? new vscode.Range(position, position)
                        : undefined
                });
                return;
            } catch {
                continue;
            }
        }

        vscode.window.showWarningMessage(`Could not open '${parsed.filePath}' from the current workspace.`);
    }

    private parseOpenFileRequest(
        filePath: string,
        line?: number,
        column?: number
    ): { filePath: string; line?: number; column?: number } {
        let normalizedPath = String(filePath || '')
            .trim()
            .replace(/^[`"'(<[]+/, '')
            .replace(/[>`'"),.;\]]+$/, '');

        const location = /^(.*?)(?::(\d{1,7})(?::(\d{1,5}))?)$/.exec(normalizedPath);
        if (location && location[1]) {
            normalizedPath = location[1];
            line ??= Number.parseInt(location[2], 10);
            if (location[3]) {
                column ??= Number.parseInt(location[3], 10);
            }
        }

        return {
            filePath: normalizedPath,
            line: this.normalizePositiveInteger(line),
            column: this.normalizePositiveInteger(column)
        };
    }

    private resolveOpenFileCandidates(filePath: string): string[] {
        const roots = this.getOpenFileRoots();
        const relativeCandidates = this.createRelativeOpenFileCandidates(filePath);
        const resolvedCandidates: string[] = [];

        for (const candidate of relativeCandidates) {
            if (path.isAbsolute(candidate)) {
                this.pushOpenFileCandidate(resolvedCandidates, candidate, roots);
                continue;
            }

            for (const root of roots) {
                this.pushOpenFileCandidate(resolvedCandidates, path.resolve(root, candidate), roots);
            }
        }

        return resolvedCandidates;
    }

    private createRelativeOpenFileCandidates(filePath: string): string[] {
        const normalizedPath = filePath.replace(/\\/g, path.sep).replace(/\//g, path.sep);
        const candidates = [normalizedPath];
        const diffPrefixMatch = /^(?:a|b)[\\/](.+)$/.exec(filePath);
        if (diffPrefixMatch?.[1]) {
            candidates.push(diffPrefixMatch[1].replace(/\\/g, path.sep).replace(/\//g, path.sep));
        }

        return Array.from(new Set(candidates));
    }

    private getOpenFileRoots(): string[] {
        const roots = [
            ...(vscode.workspace.workspaceFolders?.map(folder => folder.uri.fsPath) ?? []),
            this.sessionManager.getWorkingDirectory()
        ]
            .filter((root): root is string => typeof root === 'string' && root.trim().length > 0)
            .map(root => path.resolve(root));

        return Array.from(new Set(roots));
    }

    private pushOpenFileCandidate(candidates: string[], candidate: string, roots: string[]) {
        const resolved = path.resolve(candidate);
        if (!roots.some(root => this.isPathInsideRoot(resolved, root))) {
            return;
        }

        if (!candidates.includes(resolved)) {
            candidates.push(resolved);
        }
    }

    private isPathInsideRoot(candidate: string, root: string): boolean {
        const relative = path.relative(root, candidate);
        return relative === '' || (!!relative && !relative.startsWith('..') && !path.isAbsolute(relative));
    }

    private normalizePositiveInteger(value: number | undefined): number | undefined {
        return typeof value === 'number' && Number.isFinite(value) && value > 0
            ? Math.floor(value)
            : undefined;
    }

    private requestFromWebview(request: ClientRequest): Promise<ClientRequestResolution> {
        this.postClientRequest(request);
        return new Promise((resolve) => {
            this.localRequestResolvers.set(request.id, resolve);
        });
    }

    private resolveClientRequest(requestId: string, resolution: ClientRequestResolution) {
        const localResolve = this.localRequestResolvers.get(requestId);
        if (localResolve) {
            this.localRequestResolvers.delete(requestId);
            localResolve(resolution);
            this.webview.postMessage({ command: 'resolveClientRequest', requestId });
            return;
        }

        this.sessionManager.resolveClientRequest(requestId, resolution);
    }

    private mergeToolCall(update: ToolCallUpdate): ToolCallUpdate {
        const current = this.toolCalls.get(update.toolCallId);
        const merged: ToolCallUpdate = {
            ...current,
            ...update,
            rawInput: update.rawInput ?? current?.rawInput,
            content: update.content && update.content.length > 0
                ? update.content
                : current?.content
        };
        this.toolCalls.set(update.toolCallId, merged);
        return merged;
    }

    private createLocalRequestId(kind: string): string {
        this.localRequestCounter += 1;
        return `local-${kind}-${this.localRequestCounter}`;
    }

    private postClientRequest(request: ClientRequest) {
        this.webview.postMessage({ command: 'showClientRequest', request });
    }

    private postProcessStatus(status: NanoAgentProcessStatus) {
        this.webview.postMessage({ command: 'setProcessStatus', status });
    }

    private postPromptState(promptState: PromptState) {
        this.webview.postMessage({ command: 'setPromptState', promptState });
    }

    private postChatMessage(
        text: string,
        role: 'assistant' | 'system' | 'user' | 'metrics'
    ) {
        this.webview.postMessage({ command: 'appendMessage', text, role });
    }

    private postSystemMessage(text: string) {
        this.postChatMessage(text, 'system');
    }

    private postSessionInfo(sessionInfo: SessionInfo | null) {
        this.webview.postMessage({
            command: 'setSessionInfo',
            sessionInfo,
            workingDirectory: this.sessionManager.getWorkingDirectory()
        });
    }
}

function getChatWebviewContent(nonce: string) {
    const commandSuggestionsJson = JSON.stringify(CHAT_COMMANDS);

    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta http-equiv="Content-Security-Policy"
          content="default-src 'none'; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}'; img-src data:; connect-src 'none'; form-action 'none'; frame-ancestors 'none'; base-uri 'none';">
    <title>NanoAgent</title>
    <style nonce="${nonce}">
        :root {
            color-scheme: dark;
            --app-bg: var(--vscode-editor-background, #1e1e1e);
            --panel-bg: var(--vscode-sideBar-background, #181818);
            --input-bg: var(--vscode-input-background, #252526);
            --input-fg: var(--vscode-input-foreground, var(--vscode-foreground, #cccccc));
            --fg: var(--vscode-foreground, #cccccc);
            --muted: var(--vscode-descriptionForeground, #8f8f8f);
            --border: var(--vscode-panel-border, #2b2b2b);
            --focus: var(--vscode-focusBorder, #007fd4);
            --button-bg: var(--vscode-button-background, #0e639c);
            --button-fg: var(--vscode-button-foreground, #ffffff);
            --button-hover: var(--vscode-button-hoverBackground, #1177bb);
            --danger: var(--vscode-errorForeground, #f48771);
            --warning: var(--vscode-charts-yellow, #cca700);
            --ok: var(--vscode-testing-iconPassed, #73c991);
        }

        * {
            box-sizing: border-box;
        }

        html,
        body {
            width: 100%;
            height: 100%;
            padding: 0;
            margin: 0;
            overflow: hidden;
            color: var(--fg);
            background: var(--app-bg);
            font-family: var(--vscode-font-family, "Segoe UI", sans-serif);
            font-size: var(--vscode-font-size, 13px);
            letter-spacing: 0;
        }

        button,
        input,
        select,
        textarea {
            font: inherit;
        }

        button {
            border: 0;
            cursor: pointer;
        }

        button:disabled,
        input:disabled,
        textarea:disabled {
            cursor: not-allowed;
            opacity: 0.55;
        }

        .workbench {
            display: grid;
            grid-template-rows: minmax(0, 1fr);
            width: 100%;
            height: 100vh;
            min-width: 0;
            background: var(--app-bg);
        }

        .top-rail,
        .brand-row {
            display: none;
        }

        .main-grid {
            display: grid;
            grid-template-columns: minmax(0, 1fr);
            min-height: 0;
        }

        .chat-pane {
            display: grid;
            grid-template-rows: minmax(0, 1fr) auto auto;
            min-width: 0;
            min-height: 0;
        }

        .messages,
        .settings-page {
            grid-row: 1;
        }

        .messages.hidden,
        .settings-page.hidden {
            display: none;
        }

        .messages {
            display: flex;
            flex-direction: column;
            gap: 12px;
            min-height: 0;
            padding: 14px 16px 10px;
            overflow-y: auto;
        }

        .message-card {
            display: grid;
            gap: 4px;
            max-width: min(820px, 94%);
            white-space: pre-wrap;
            word-break: break-word;
            overflow-wrap: anywhere;
        }

        .message-card.user {
            align-self: flex-end;
            max-width: min(560px, 86%);
            padding: 8px 10px;
            border: 1px solid var(--border);
            border-radius: 8px;
            color: var(--input-fg);
            background: color-mix(in srgb, var(--input-bg) 82%, var(--fg) 5%);
        }

        .message-card.assistant {
            align-self: flex-start;
            max-width: min(820px, 94%);
        }

        .message-card.reasoning {
            align-self: flex-start;
            max-width: min(760px, 92%);
            color: var(--muted);
        }

        .thinking-details {
            padding: 7px 9px;
            border-left: 2px solid var(--focus);
            border-radius: 6px;
            background: color-mix(in srgb, var(--input-bg) 58%, transparent);
        }

        .thinking-details summary {
            color: var(--muted);
            font-size: 11px;
            font-weight: 700;
            text-transform: uppercase;
        }

        .thinking-details pre {
            margin: 6px 0 0;
            color: var(--muted);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 12px;
            line-height: 1.45;
            white-space: pre-wrap;
            overflow-wrap: anywhere;
        }

        .message-card.tool {
            align-self: flex-start;
            max-width: min(760px, 92%);
            padding: 7px 9px;
            border-left: 2px solid var(--warning);
            border-radius: 6px;
            color: var(--muted);
            background: color-mix(in srgb, var(--input-bg) 60%, transparent);
        }

        .message-card.tool.completed {
            border-left-color: var(--ok);
        }

        .message-card.tool.failed {
            border-left-color: var(--danger);
        }

        .tool-message {
            display: grid;
            gap: 8px;
            min-width: 0;
        }

        .tool-message-header {
            display: flex;
            align-items: center;
            gap: 8px;
            min-width: 0;
        }

        .tool-message-status {
            flex: 0 0 auto;
            color: var(--warning);
            font-size: 10px;
            font-weight: 700;
            text-transform: uppercase;
        }

        .message-card.tool.completed .tool-message-status {
            color: var(--ok);
        }

        .message-card.tool.failed .tool-message-status {
            color: var(--danger);
        }

        .tool-message-title {
            min-width: 0;
            color: var(--fg);
            font-weight: 600;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .tool-message-kind {
            color: var(--muted);
            font-size: 11px;
        }

        .tool-output-pre {
            max-height: 260px;
            margin: 0;
            padding: 8px;
            overflow: auto;
            border: 1px solid var(--border);
            border-radius: 6px;
            color: var(--fg);
            background: var(--app-bg);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 12px;
            line-height: 1.45;
            white-space: pre;
            overflow-wrap: normal;
        }

        .tool-arguments summary {
            color: var(--muted);
            font-size: 11px;
            cursor: pointer;
        }

        .diff-view {
            display: grid;
            gap: 0;
            border: 1px solid var(--border);
            border-radius: 6px;
            overflow: hidden;
        }

        .diff-header {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 8px;
            padding: 5px 8px;
            background: color-mix(in srgb, var(--input-bg) 80%, transparent);
            border-bottom: 1px solid var(--border);
            font-size: 11px;
        }

        .diff-stat {
            flex: 0 0 auto;
            color: var(--muted);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
        }

        .diff-body {
            max-height: 320px;
            margin: 0;
            padding: 4px 0;
            overflow: auto;
            border: 0;
            border-radius: 0;
            background: var(--app-bg);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 12px;
            line-height: 1.4;
            white-space: pre;
        }

        .diff-line {
            padding: 0 8px;
            white-space: pre-wrap;
            overflow-wrap: anywhere;
        }

        .diff-add {
            color: var(--ok);
            background: color-mix(in srgb, var(--ok) 12%, transparent);
        }

        .diff-del {
            color: var(--danger);
            background: color-mix(in srgb, var(--danger) 12%, transparent);
        }

        .diff-meta {
            color: var(--muted);
            background: color-mix(in srgb, var(--focus) 10%, transparent);
        }

        .tool-pending {
            color: var(--muted);
            font-size: 12px;
        }

        .message-card.metrics {
            align-self: flex-start;
            max-width: min(760px, 92%);
            color: var(--muted);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 11px;
            opacity: 0.85;
        }

        .message-card.system {
            align-self: center;
            max-width: 96%;
            padding: 7px 9px;
            border: 1px solid color-mix(in srgb, var(--warning) 45%, transparent);
            border-radius: 8px;
            color: var(--warning);
            background: color-mix(in srgb, var(--warning) 10%, var(--app-bg));
        }

        .message-label {
            display: none;
        }

        .message-text {
            color: inherit;
            font-size: 13px;
            line-height: 1.5;
        }

        .assistant .message-text {
            color: var(--fg);
        }

        .file-link {
            color: var(--vscode-textLink-foreground, #3794ff);
            text-decoration: none;
            cursor: pointer;
        }

        .file-link:hover {
            color: var(--vscode-textLink-activeForeground, #4daafc);
            text-decoration: underline;
        }

        .progress-indicator {
            position: sticky;
            bottom: 0;
            z-index: 1;
            width: max-content;
            max-width: min(320px, 90%);
            padding: 6px 10px;
            border: 1px solid var(--border);
            border-radius: 8px;
            color: var(--muted);
            background: color-mix(in srgb, var(--panel-bg) 94%, transparent);
            box-shadow: 0 -6px 12px color-mix(in srgb, var(--app-bg) 70%, transparent);
        }

        .progress-dots {
            display: inline-flex;
            gap: 2px;
            margin-left: 2px;
        }

        .progress-dots span {
            animation: progress-dot 1.1s infinite ease-in-out;
        }

        .progress-dots span:nth-child(2) {
            animation-delay: 0.15s;
        }

        .progress-dots span:nth-child(3) {
            animation-delay: 0.3s;
        }

        @keyframes progress-dot {
            0%,
            80%,
            100% {
                opacity: 0.25;
            }

            40% {
                opacity: 1;
            }
        }

        .empty-state {
            margin: auto;
            max-width: 420px;
            padding: 20px;
            color: var(--muted);
            text-align: center;
            line-height: 1.5;
        }

        .side-pane {
            grid-row: 2;
            display: none;
            grid-template-columns: minmax(0, 1fr);
            gap: 8px;
            min-width: 0;
            max-height: 180px;
            padding: 0 14px 8px;
            overflow: hidden;
            background: var(--app-bg);
        }

        .side-pane.visible {
            display: grid;
        }

        .section {
            min-width: 0;
            min-height: 0;
            padding: 8px;
            overflow: hidden;
            border: 1px solid var(--border);
            border-radius: 8px;
            background: var(--panel-bg);
        }

        .section-scroll {
            overflow-y: auto;
        }

        .section-header {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 8px;
            margin-bottom: 7px;
        }

        .section-header h2 {
            margin: 0;
            color: var(--fg);
            font-size: 11px;
            line-height: 1.2;
            font-weight: 600;
            text-transform: uppercase;
        }

        .section-count {
            color: var(--muted);
            font-size: 11px;
            white-space: nowrap;
        }

        .context-grid {
            display: grid;
            grid-template-columns: repeat(2, minmax(0, 1fr));
            gap: 6px;
        }

        .context-item {
            min-width: 0;
            padding: 6px;
            border-radius: 6px;
            background: color-mix(in srgb, var(--input-bg) 76%, transparent);
        }

        .context-item.wide {
            grid-column: 1 / -1;
        }

        .context-label {
            color: var(--muted);
            font-size: 10px;
            text-transform: uppercase;
        }

        .context-value {
            margin-top: 2px;
            color: var(--fg);
            font-size: 11px;
            line-height: 1.35;
            overflow-wrap: anywhere;
        }

        .plan-list,
        .tool-list {
            display: grid;
            gap: 7px;
        }

        .plan-item {
            display: grid;
            grid-template-columns: auto 1fr;
            gap: 7px;
            align-items: start;
            padding: 6px;
            border-radius: 6px;
            background: color-mix(in srgb, var(--input-bg) 76%, transparent);
        }

        .plan-dot {
            width: 8px;
            height: 8px;
            margin-top: 4px;
            border-radius: 50%;
            background: var(--muted);
        }

        .plan-item.completed .plan-dot {
            background: var(--ok);
        }

        .plan-item.in_progress .plan-dot {
            background: var(--warning);
        }

        .plan-content {
            color: var(--fg);
            font-size: 12px;
            line-height: 1.4;
            overflow-wrap: anywhere;
        }

        .plan-status {
            margin-top: 2px;
            color: var(--muted);
            font-size: 10px;
            text-transform: uppercase;
        }

        .agent-thread-list {
            display: grid;
            gap: 7px;
        }

        .agent-thread-item {
            width: 100%;
            display: grid;
            gap: 4px;
            padding: 7px;
            border: 1px solid transparent;
            border-radius: 6px;
            color: inherit;
            text-align: left;
            background: color-mix(in srgb, var(--input-bg) 76%, transparent);
            cursor: pointer;
        }

        .agent-thread-item:hover,
        .agent-thread-item.active {
            border-color: var(--focus);
            background: var(--vscode-list-hoverBackground, color-mix(in srgb, var(--input-bg) 88%, transparent));
        }

        .agent-thread-topline {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 8px;
        }

        .agent-thread-name {
            color: var(--fg);
            font-size: 12px;
            font-weight: 600;
        }

        .agent-thread-status {
            color: var(--muted);
            font-size: 10px;
            text-transform: uppercase;
            white-space: nowrap;
        }

        .agent-thread-task,
        .agent-thread-meta,
        .agent-thread-output {
            color: var(--muted);
            font-size: 11px;
            line-height: 1.4;
            overflow-wrap: anywhere;
        }

        .agent-thread-output {
            padding-top: 4px;
            color: var(--fg);
            border-top: 1px solid var(--border);
            white-space: pre-wrap;
        }

        .tool-card {
            display: grid;
            gap: 7px;
            padding: 7px;
            border-left: 2px solid var(--warning);
            border-radius: 6px;
            background: color-mix(in srgb, var(--input-bg) 76%, transparent);
        }

        .tool-card.completed {
            border-left-color: var(--ok);
        }

        .tool-card.failed {
            border-left-color: var(--danger);
        }

        .tool-title-row {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 8px;
        }

        .tool-title {
            min-width: 0;
            color: var(--fg);
            font-size: 12px;
            font-weight: 600;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .tool-status {
            flex: 0 0 auto;
            color: var(--warning);
            font-size: 10px;
            font-weight: 600;
            text-transform: uppercase;
        }

        .tool-card.completed .tool-status {
            color: var(--ok);
        }

        .tool-card.failed .tool-status {
            color: var(--danger);
        }

        .tool-kind {
            color: var(--muted);
            font-size: 11px;
        }

        details {
            min-width: 0;
        }

        summary {
            color: var(--muted);
            font-size: 11px;
            cursor: pointer;
        }

        pre {
            max-height: 180px;
            margin: 6px 0 0;
            padding: 7px;
            overflow: auto;
            border: 1px solid var(--border);
            border-radius: 6px;
            color: var(--fg);
            background: var(--app-bg);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 11px;
            line-height: 1.45;
            white-space: pre-wrap;
            overflow-wrap: anywhere;
        }

        .empty-panel {
            color: var(--muted);
            font-size: 12px;
            line-height: 1.45;
        }

        .settings-page {
            min-height: 0;
            padding: 12px 14px 10px;
            overflow-y: auto;
            background: var(--app-bg);
        }

        .settings-header {
            display: flex;
            align-items: flex-start;
            justify-content: space-between;
            gap: 12px;
            margin-bottom: 12px;
        }

        .settings-title {
            min-width: 0;
        }

        .settings-title h1 {
            margin: 0;
            color: var(--fg);
            font-size: 16px;
            line-height: 1.25;
        }

        .settings-summary {
            margin-top: 4px;
            color: var(--muted);
            font-size: 12px;
            line-height: 1.45;
            overflow-wrap: anywhere;
        }

        .settings-groups {
            display: grid;
            gap: 12px;
            max-width: 860px;
        }

        .settings-group {
            display: grid;
            gap: 6px;
            min-width: 0;
        }

        .settings-group h2 {
            margin: 0;
            color: var(--muted);
            font-size: 11px;
            font-weight: 700;
            text-transform: uppercase;
        }

        .settings-action {
            display: grid;
            grid-template-columns: minmax(0, 1fr) auto;
            gap: 8px;
            align-items: center;
            width: 100%;
            min-height: 44px;
            padding: 8px 10px;
            border: 1px solid var(--border);
            border-radius: 8px;
            color: var(--fg);
            text-align: left;
            background: var(--panel-bg);
        }

        .settings-action:hover {
            border-color: var(--focus);
            background: var(--vscode-list-hoverBackground, var(--panel-bg));
        }

        .settings-action-main {
            display: grid;
            gap: 3px;
            min-width: 0;
        }

        .settings-action-title {
            font-size: 13px;
            font-weight: 600;
            overflow-wrap: anywhere;
        }

        .settings-action-description {
            color: var(--muted);
            font-size: 11px;
            line-height: 1.35;
            overflow-wrap: anywhere;
        }

        .settings-action-value {
            max-width: min(240px, 34vw);
            color: var(--muted);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 11px;
            overflow: hidden;
            text-align: right;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .composer {
            grid-row: 3;
            position: relative;
            display: grid;
            gap: 8px;
            padding: 8px 14px 12px;
            background: var(--app-bg);
        }

        .suggestions {
            position: absolute;
            left: 14px;
            right: 14px;
            bottom: 102px;
            z-index: 4;
            max-height: 240px;
            overflow-y: auto;
            border: 1px solid var(--border);
            border-radius: 8px;
            background: var(--panel-bg);
            box-shadow: 0 12px 28px rgba(0, 0, 0, 0.32);
        }

        .suggestions.hidden {
            display: none;
        }

        .suggestion {
            display: grid;
            gap: 3px;
            padding: 8px 10px;
            border-bottom: 1px solid var(--border);
            cursor: pointer;
        }

        .suggestion:last-child {
            border-bottom: 0;
        }

        .suggestion.active,
        .suggestion:hover {
            background: var(--vscode-list-hoverBackground, rgba(255, 255, 255, 0.06));
        }

        .suggestion-usage {
            color: var(--fg);
            font-size: 12px;
            font-weight: 600;
            overflow-wrap: anywhere;
        }

        .suggestion-description {
            color: var(--muted);
            font-size: 11px;
        }

        .composer-row {
            display: grid;
            grid-template-rows: auto auto;
            gap: 7px;
            padding: 8px;
            border: 1px solid var(--border);
            border-radius: 18px;
            background: var(--input-bg);
        }

        .composer-row:focus-within {
            border-color: var(--focus);
        }

        .chat-input {
            width: 100%;
            min-height: 38px;
            max-height: 150px;
            padding: 8px 8px 2px;
            resize: none;
            border: 0;
            outline: none;
            color: var(--input-fg);
            background: transparent;
        }

        .chat-input::placeholder {
            color: var(--vscode-input-placeholderForeground, var(--muted));
        }

        .composer-toolbar {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 8px;
            min-width: 0;
        }

        .toolbar-left,
        .toolbar-right,
        .status-cluster {
            display: flex;
            align-items: center;
            gap: 7px;
            min-width: 0;
        }

        .toolbar-left {
            flex: 1 1 auto;
        }

        .toolbar-right {
            flex: 0 0 auto;
        }

        .status-pill,
        .model-pill,
        .model-select,
        .profile-select,
        .ghost-button,
        .danger-button,
        .icon-button {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            gap: 6px;
            min-height: 28px;
            border: 1px solid transparent;
            border-radius: 7px;
            padding: 4px 8px;
            color: var(--fg);
            background: transparent;
            white-space: nowrap;
        }

        .model-select,
        .profile-select {
            min-width: 0;
            max-width: min(260px, 42vw);
            outline: none;
            cursor: pointer;
        }

        .profile-select {
            max-width: min(190px, 32vw);
            color: var(--warning);
            font-weight: 600;
        }

        .icon-button,
        .composer .primary-button {
            width: 30px;
            min-width: 30px;
            height: 30px;
            padding: 0;
            border-radius: 50%;
            font-size: 17px;
            line-height: 1;
        }

        .model-pill {
            max-width: min(260px, 42vw);
        }

        .model-pill span {
            overflow: hidden;
            text-overflow: ellipsis;
        }

        .model-pill {
            cursor: pointer;
        }

        .status-pill {
            margin-left: auto;
            color: var(--muted);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 11px;
        }

        .context-meter {
            flex: 0 0 auto;
            padding: 2px 7px;
            border-radius: 7px;
            color: var(--muted);
            background: color-mix(in srgb, var(--input-bg) 76%, transparent);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 11px;
            white-space: nowrap;
        }

        .context-meter.context-meter-warn {
            color: var(--warning);
            background: color-mix(in srgb, var(--warning) 14%, transparent);
        }

        .ghost-button:hover,
        .model-pill:hover,
        .model-select:hover,
        .profile-select:hover,
        .icon-button:hover {
            background: var(--vscode-toolbar-hoverBackground, rgba(255, 255, 255, 0.08));
        }

        .access-pill {
            color: var(--warning);
            font-weight: 600;
        }

        .danger-button {
            color: var(--danger);
        }

        .danger-button:hover {
            background: color-mix(in srgb, var(--danger) 14%, transparent);
        }

        .primary-button {
            min-height: 32px;
            border-radius: 7px;
            padding: 0 12px;
            color: var(--button-fg);
            font-weight: 600;
            background: var(--button-bg);
        }

        .primary-button:hover {
            background: var(--button-hover);
        }

        .composer .primary-button {
            color: var(--vscode-button-secondaryForeground, #ffffff);
            background: var(--vscode-button-secondaryBackground, #3a3d41);
        }

        .composer .primary-button:not(:disabled):hover {
            background: var(--vscode-button-secondaryHoverBackground, #45494e);
        }

        .modal-backdrop {
            position: fixed;
            inset: 0;
            z-index: 20;
            display: grid;
            place-items: center;
            padding: 18px;
            background: rgba(0, 0, 0, 0.52);
        }

        .modal-backdrop.hidden {
            display: none;
        }

        .modal {
            width: min(560px, 100%);
            max-height: min(720px, 92vh);
            display: grid;
            gap: 12px;
            padding: 14px;
            overflow: hidden;
            border: 1px solid var(--border);
            border-radius: 8px;
            background: var(--panel-bg);
            box-shadow: 0 18px 48px rgba(0, 0, 0, 0.42);
        }

        .modal h2 {
            margin: 0;
            color: var(--fg);
            font-size: 15px;
        }

        .modal-description {
            margin: 0;
            color: var(--muted);
            font-size: 12px;
            line-height: 1.45;
            overflow-wrap: anywhere;
        }

        .modal-options {
            display: grid;
            gap: 8px;
            max-height: 390px;
            overflow-y: auto;
        }

        .modal-option {
            display: grid;
            gap: 3px;
            padding: 10px;
            border: 1px solid var(--border);
            border-radius: 8px;
            color: var(--fg);
            text-align: left;
            background: var(--input-bg);
        }

        .modal-option:hover,
        .modal-option.active {
            border-color: var(--focus);
            background: var(--vscode-list-hoverBackground, var(--input-bg));
        }

        .modal-option strong {
            font-size: 13px;
            overflow-wrap: anywhere;
        }

        .modal-option span {
            color: var(--muted);
            font-size: 11px;
            text-transform: uppercase;
        }

        .modal-countdown {
            margin-right: auto;
            color: var(--muted);
            font-size: 12px;
            line-height: 1.4;
        }

        .modal-input {
            width: 100%;
            min-height: 40px;
            padding: 9px 10px;
            border: 1px solid var(--border);
            border-radius: 8px;
            outline: none;
            color: var(--input-fg);
            background: var(--input-bg);
        }

        .modal-input:focus {
            border-color: var(--focus);
        }

        .modal-actions {
            display: flex;
            justify-content: flex-end;
            gap: 8px;
        }

        @media (max-width: 760px) {
            .messages {
                padding: 12px;
            }

            .side-pane {
                grid-template-columns: 1fr;
                max-height: 220px;
                overflow-y: auto;
                padding: 0 12px 8px;
            }

            .composer {
                padding: 8px 12px 12px;
            }

            .composer-toolbar {
                align-items: stretch;
                flex-direction: column;
            }

            .toolbar-left,
            .toolbar-right {
                width: 100%;
                flex-wrap: wrap;
            }

            .toolbar-right {
                justify-content: flex-end;
            }

            .model-pill {
                max-width: 100%;
            }
        }
    </style>
</head>
<body>
    <div class="workbench">
        <main class="main-grid">
            <section class="chat-pane">
                <span id="section-title" hidden>No active section</span>
                <div id="messages" class="messages">
                    <div id="empty-state" class="empty-state">Start a request or use slash commands from the composer.</div>
                </div>
                <section id="settings-page" class="settings-page hidden" aria-label="NanoAgent settings">
                    <div class="settings-header">
                        <div class="settings-title">
                            <h1>Settings</h1>
                            <div id="settings-summary" class="settings-summary">No active session.</div>
                        </div>
                        <button id="settings-close-button" class="ghost-button">Chat</button>
                    </div>
                    <div class="settings-groups">
                        <section class="settings-group">
                            <h2>Session</h2>
                            <button class="settings-action" data-action="model">
                                <span class="settings-action-main">
                                    <span class="settings-action-title">Model</span>
                                    <span class="settings-action-description">Choose active model for this session.</span>
                                </span>
                                <span id="settings-model-value" class="settings-action-value">-</span>
                            </button>
                            <button class="settings-action" data-command="/setting profile">
                                <span class="settings-action-main">
                                    <span class="settings-action-title">Profile</span>
                                    <span class="settings-action-description">Switch build, plan, review, or subagent profile.</span>
                                </span>
                                <span id="settings-profile-value" class="settings-action-value">-</span>
                            </button>
                            <button class="settings-action" data-command="/setting thinking">
                                <span class="settings-action-main">
                                    <span class="settings-action-title">Thinking</span>
                                    <span class="settings-action-description">Set provider reasoning effort for later prompts.</span>
                                </span>
                                <span id="settings-thinking-value" class="settings-action-value">-</span>
                            </button>
                            <button class="settings-action" data-command="/setting summary">
                                <span class="settings-action-main">
                                    <span class="settings-action-title">Summary</span>
                                    <span class="settings-action-description">Show provider, model, profile, thinking, and session details.</span>
                                </span>
                                <span class="settings-action-value">open</span>
                            </button>
                            <button class="settings-action" data-action="sessions">
                                <span class="settings-action-main">
                                    <span class="settings-action-title">Sessions</span>
                                    <span class="settings-action-description">Browse, resume, fork, or export saved sessions.</span>
                                </span>
                                <span class="settings-action-value">browse</span>
                            </button>
                        </section>
                        <section class="settings-group">
                            <h2>Provider</h2>
                            <button class="settings-action" data-command="/setting provider">
                                <span class="settings-action-main">
                                    <span class="settings-action-title">Provider</span>
                                    <span class="settings-action-description">Switch saved provider for this session.</span>
                                </span>
                                <span id="settings-provider-value" class="settings-action-value">-</span>
                            </button>
                            <button class="settings-action" data-command="/setting onboarding">
                                <span class="settings-action-main">
                                    <span class="settings-action-title">Onboarding</span>
                                    <span class="settings-action-description">Add or repair provider credentials.</span>
                                </span>
                                <span class="settings-action-value">setup</span>
                            </button>
                        </section>
                        <section class="settings-group">
                            <h2>Workspace</h2>
                            <button class="settings-action" data-command="/setting workspace">
                                <span class="settings-action-main">
                                    <span class="settings-action-title">Workspace Files</span>
                                    <span class="settings-action-description">Create or review .nanoagent project files.</span>
                                </span>
                                <span class="settings-action-value">init</span>
                            </button>
                            <button class="settings-action" data-command="/setting budget">
                                <span class="settings-action-main">
                                    <span class="settings-action-title">Budget</span>
                                    <span class="settings-action-description">Configure local or cloud budget controls.</span>
                                </span>
                                <span class="settings-action-value">open</span>
                            </button>
                            <button class="settings-action" data-command="/setting permissions">
                                <span class="settings-action-main">
                                    <span class="settings-action-title">Permissions</span>
                                    <span class="settings-action-description">Edit modes, sandbox, and session overrides.</span>
                                </span>
                                <span class="settings-action-value">policy</span>
                            </button>
                            <button class="settings-action" data-command="/setting rules">
                                <span class="settings-action-main">
                                    <span class="settings-action-title">Rules</span>
                                    <span class="settings-action-description">Inspect effective tool permission rules.</span>
                                </span>
                                <span class="settings-action-value">view</span>
                            </button>
                            <button class="settings-action" data-command="/setting tools">
                                <span class="settings-action-main">
                                    <span class="settings-action-title">Tools</span>
                                    <span class="settings-action-description">Show MCP servers, custom tools, and dynamic tool status.</span>
                                </span>
                                <span class="settings-action-value">view</span>
                            </button>
                            <button class="settings-action" data-command="/terminals">
                                <span class="settings-action-main">
                                    <span class="settings-action-title">Background Terminals</span>
                                    <span class="settings-action-description">List running terminals and stop them with /terminals stop.</span>
                                </span>
                                <span class="settings-action-value">view</span>
                            </button>
                        </section>
                        <section class="settings-group">
                            <h2>Extension</h2>
                            <button class="settings-action" data-action="vscodeSettings">
                                <span class="settings-action-main">
                                    <span class="settings-action-title">VS Code Extension Settings</span>
                                    <span class="settings-action-description">Edit command, args, working directory, auto-start, and log level.</span>
                                </span>
                                <span class="settings-action-value">vscode</span>
                            </button>
                        </section>
                    </div>
                </section>
                <aside id="activity-pane" class="side-pane">
                    <section class="section section-scroll">
                        <div class="section-header">
                            <h2>Plan</h2>
                            <span id="plan-count" class="section-count">0 steps</span>
                        </div>
                        <div id="plan-list" class="plan-list"></div>
                    </section>
                    <section class="section section-scroll">
                        <div class="section-header">
                            <h2>Agents</h2>
                            <span id="agent-count" class="section-count">0 threads</span>
                        </div>
                        <div id="agent-thread-list" class="agent-thread-list"></div>
                    </section>
                </aside>
                <div class="composer">
                    <div id="suggestions" class="suggestions hidden"></div>
                    <div class="composer-row">
                        <textarea id="chat-input" class="chat-input" rows="1" placeholder="Ask anything · / for commands · @ for files"></textarea>
                        <div class="composer-toolbar">
                            <div class="toolbar-left">
                                <button id="add-context-button" class="icon-button" title="Read workspace file" aria-label="Read workspace file">+</button>
                                <button id="settings-button" class="icon-button" title="Settings" aria-label="Settings">&#9881;</button>
                                <select id="profile-select" class="profile-select" title="Profile" aria-label="Profile"></select>
                                <button id="model-button" class="model-pill" title="Choose model" aria-label="Choose model"><span id="model-button-label">Model</span></button>
                                <div class="status-pill" title="Process status">
                                    <span id="status-text">Stopped</span>
                                </div>
                                <span id="context-meter" class="context-meter" title="Context usage" hidden></span>
                            </div>
                            <div class="toolbar-right">
                                <button id="stop-button" class="danger-button" disabled>Stop</button>
                                <button id="send-button" class="primary-button" title="Send" aria-label="Send">&#8593;</button>
                            </div>
                        </div>
                    </div>
                </div>
            </section>
        </main>
    </div>

    <div id="modal-backdrop" class="modal-backdrop hidden">
        <div class="modal">
            <h2 id="modal-title"></h2>
            <p id="modal-description" class="modal-description"></p>
            <div id="modal-body"></div>
            <div id="modal-actions" class="modal-actions"></div>
        </div>
    </div>

    <script nonce="${nonce}">
        const api = acquireVsCodeApi();
        const commandSuggestions = ${commandSuggestionsJson};
        const buildDiffModel = ${buildDiffModel.toString()};
        const messagesDiv = document.getElementById('messages');
        const emptyState = document.getElementById('empty-state');
        const settingsPage = document.getElementById('settings-page');
        const settingsSummary = document.getElementById('settings-summary');
        const settingsCloseButton = document.getElementById('settings-close-button');
        const settingsModelValue = document.getElementById('settings-model-value');
        const settingsProfileValue = document.getElementById('settings-profile-value');
        const settingsThinkingValue = document.getElementById('settings-thinking-value');
        const settingsProviderValue = document.getElementById('settings-provider-value');
        const inputField = document.getElementById('chat-input');
        const addContextButton = document.getElementById('add-context-button');
        const settingsButton = document.getElementById('settings-button');
        const sendButton = document.getElementById('send-button');
        const stopButton = document.getElementById('stop-button');
        const modelButton = document.getElementById('model-button');
        const modelButtonLabel = document.getElementById('model-button-label');
        const profileSelect = document.getElementById('profile-select');
        const sectionTitle = document.getElementById('section-title');
        const statusText = document.getElementById('status-text');
        const contextMeter = document.getElementById('context-meter');
        const suggestionsDiv = document.getElementById('suggestions');
        const sidePane = document.getElementById('activity-pane');
        const planList = document.getElementById('plan-list');
        const planCount = document.getElementById('plan-count');
        const agentThreadList = document.getElementById('agent-thread-list');
        const agentCount = document.getElementById('agent-count');
        const modalBackdrop = document.getElementById('modal-backdrop');
        const modalTitle = document.getElementById('modal-title');
        const modalDescription = document.getElementById('modal-description');
        const modalBody = document.getElementById('modal-body');
        const modalActions = document.getElementById('modal-actions');

        let sessionInfo = null;
        let workingDirectory = '';
        let processStatus = 'stopped';
        let promptState = { isRunning: false, isCancelling: false };
        let visibleSuggestions = [];
        let activeSuggestionIndex = 0;
        let fileMentionQuery = null;
        let fileMentionResults = [];
        let activeAssistantMessage = null;
        let activeReasoningMessage = null;
        let activeView = 'chat';
        let activeModalRequest = null;
        let activeAutoSelectTimer = null;
        let progressIndicator = null;
        let hasPlanActivity = false;
        let activeAgentThreadId = '';
        const queuedClientRequests = [];
        const toolCalls = new Map();
        const toolMessageElements = new Map();
        const agentThreads = new Map();
        const fallbackProfiles = [
            { name: 'build', mode: 'primary', description: 'Default coding agent profile' },
            { name: 'plan', mode: 'primary', description: 'Read-only planning profile' },
            { name: 'review', mode: 'primary', description: 'Read-only review profile' },
            { name: 'general', mode: 'subagent', description: 'Implementation-capable subagent profile' },
            { name: 'explore', mode: 'subagent', description: 'Read-only subagent profile' }
        ];

        function post(command) {
            api.postMessage(command);
        }

        function appendMessage(text, role) {
            if (typeof text !== 'string') {
                return null;
            }

            emptyState.hidden = true;
            const article = document.createElement('article');
            article.className = 'message-card ' + role;

            const label = document.createElement('div');
            label.className = 'message-label';
            label.textContent = role === 'user'
                ? 'You'
                : role === 'system'
                    ? 'System'
                    : role === 'tool'
                        ? 'Tool'
                        : role === 'reasoning'
                            ? 'Thinking'
                            : 'NanoAgent';
            article.appendChild(label);

            const body = document.createElement('div');
            body.className = 'message-text';
            renderLinkifiedText(body, text);
            article.appendChild(body);

            article.dataset.text = text;
            messagesDiv.appendChild(article);
            moveProgressIndicatorToEnd();
            messagesDiv.scrollTop = messagesDiv.scrollHeight;
            updateStatusRail();
            return article;
        }

        const fileReferencePattern = /(^|[\\s("'<>\\[])([A-Za-z]:[\\\\/][^\\s"'<>|]+|(?:\\.{1,2}[\\\\/])?(?:(?:[^\\s"'<>:|\\\\/]+)[\\\\/])+[^\\s"'<>:|\\\\/]+\\.[A-Za-z][A-Za-z0-9]{0,15}|[^\\s"'<>:|\\\\/]+\\.[A-Za-z][A-Za-z0-9]{0,15})(?::(\\d{1,7})(?::(\\d{1,5}))?)?/g;

        function renderLinkifiedText(container, text) {
            container.textContent = '';
            appendLinkifiedText(container, String(text || ''));
        }

        function appendLinkifiedText(container, text) {
            let lastIndex = 0;
            fileReferencePattern.lastIndex = 0;
            let match = fileReferencePattern.exec(text);

            while (match) {
                const prefix = match[1] || '';
                const candidate = match[2] || '';
                const candidateStart = match.index + prefix.length;
                const line = parsePositiveInteger(match[3]);
                const column = parsePositiveInteger(match[4]);
                const locationText = line ? ':' + line + (column ? ':' + column : '') : '';
                const normalized = trimFileReference(candidate);
                const displayText = normalized + locationText;

                if (normalized && isLikelyFileReference(normalized, text, candidateStart)) {
                    container.appendChild(document.createTextNode(text.slice(lastIndex, candidateStart)));
                    container.appendChild(createFileReferenceLink(displayText, normalized, line, column));
                    const consumedEnd = candidateStart + candidate.length + locationText.length;
                    const trailingText = candidate.slice(normalized.length);
                    if (trailingText) {
                        container.appendChild(document.createTextNode(trailingText));
                    }
                    lastIndex = consumedEnd;
                }

                match = fileReferencePattern.exec(text);
            }

            if (lastIndex < text.length) {
                container.appendChild(document.createTextNode(text.slice(lastIndex)));
            }
        }

        function trimFileReference(value) {
            return String(value || '').replace(/[),.;\\]}]+$/, '');
        }

        function isLikelyFileReference(candidate, sourceText, startIndex) {
            if (!candidate || candidate.length > 320) {
                return false;
            }

            const before = sourceText.slice(Math.max(0, startIndex - 8), startIndex).toLowerCase();
            if (before.includes('://')) {
                return false;
            }

            return candidate.includes('.') || candidate.includes('/') || candidate.includes('\\\\');
        }

        function createFileReferenceLink(displayText, filePath, line, column) {
            const link = document.createElement('a');
            link.href = '#';
            link.className = 'file-link';
            link.textContent = displayText;
            link.title = 'Open ' + displayText;
            link.addEventListener('click', event => {
                event.preventDefault();
                event.stopPropagation();
                post({
                    command: 'openFile',
                    filePath,
                    line,
                    column
                });
            });
            return link;
        }

        function parsePositiveInteger(value) {
            const number = Number.parseInt(value || '', 10);
            return Number.isFinite(number) && number > 0 ? number : undefined;
        }

        function appendMessageChunk(chunk) {
            const role = chunk.role === 'user'
                ? 'user'
                : chunk.role === 'reasoning'
                    ? 'reasoning'
                    : 'assistant';
            if (role === 'user') {
                appendMessage(chunk.text, 'user');
                return;
            }

            if (role === 'reasoning') {
                appendReasoningChunk(chunk.text);
                return;
            }

            activeReasoningMessage = null;
            if (!activeAssistantMessage) {
                activeAssistantMessage = appendMessage('', 'assistant');
            }

            if (!activeAssistantMessage) {
                return;
            }

            emptyState.hidden = true;
            const body = activeAssistantMessage.querySelector('.message-text');
            const currentText = activeAssistantMessage.dataset.text || '';
            const nextText = currentText + chunk.text;
            activeAssistantMessage.dataset.text = nextText;
            renderLinkifiedText(body, nextText);
            moveProgressIndicatorToEnd();
            messagesDiv.scrollTop = messagesDiv.scrollHeight;
            updateStatusRail();
        }

        function appendReasoningChunk(text) {
            if (!activeReasoningMessage) {
                activeReasoningMessage = appendMessage('', 'reasoning');
            }

            if (!activeReasoningMessage) {
                return;
            }

            emptyState.hidden = true;
            const body = activeReasoningMessage.querySelector('.message-text');
            const currentText = activeReasoningMessage.dataset.text || '';
            const nextText = currentText + text;
            activeReasoningMessage.dataset.text = nextText;
            body.textContent = '';
            const details = createDetails('Thinking', nextText, true);
            details.className = 'thinking-details';
            body.appendChild(details);
            moveProgressIndicatorToEnd();
            messagesDiv.scrollTop = messagesDiv.scrollHeight;
            updateStatusRail();
        }

        function sendCurrentInput() {
            const text = inputField.value.trim();
            if (!text || promptState.isRunning) {
                return;
            }

            post({ command: 'sendMessage', text });
            inputField.value = '';
            hideSuggestions();
            updateComposerState();
        }

        sendButton.addEventListener('click', sendCurrentInput);
        addContextButton.addEventListener('click', () => {
            inputField.value = '/read ';
            inputField.focus();
            inputField.setSelectionRange(inputField.value.length, inputField.value.length);
            updateSuggestions();
            updateComposerState();
        });
        settingsButton.addEventListener('click', showSettingsPage);
        settingsCloseButton.addEventListener('click', showChatPage);
        settingsPage.querySelectorAll('.settings-action').forEach(button => {
            button.addEventListener('click', () => runSettingsAction(button));
        });
        modelButton.addEventListener('click', showModelPicker);
        profileSelect.addEventListener('change', () => {
            post({ command: 'changeProfile', profileName: profileSelect.value || '' });
        });
        stopButton.addEventListener('click', () => post({ command: 'cancelPrompt' }));

        inputField.addEventListener('keydown', (event) => {
            if (visibleSuggestions.length > 0) {
                if (event.key === 'ArrowDown') {
                    event.preventDefault();
                    activeSuggestionIndex = (activeSuggestionIndex + 1) % visibleSuggestions.length;
                    renderSuggestions();
                    return;
                }

                if (event.key === 'ArrowUp') {
                    event.preventDefault();
                    activeSuggestionIndex = (activeSuggestionIndex - 1 + visibleSuggestions.length) % visibleSuggestions.length;
                    renderSuggestions();
                    return;
                }

                if (event.key === 'Tab') {
                    event.preventDefault();
                    applySuggestion(visibleSuggestions[activeSuggestionIndex]);
                    return;
                }

                if (event.key === 'Escape') {
                    hideSuggestions();
                    return;
                }
            }

            if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') &&
                inputField.value.trim().length === 0 &&
                agentThreads.size > 0) {
                event.preventDefault();
                switchAgentThread(event.key === 'ArrowDown' ? 1 : -1);
                return;
            }

            if (event.key === 'Enter' && !event.shiftKey) {
                event.preventDefault();
                sendCurrentInput();
            }
        });

        inputField.addEventListener('input', () => {
            updateSuggestions();
            updateComposerState();
        });
        inputField.addEventListener('blur', () => setTimeout(hideSuggestions, 150));

        window.addEventListener('message', event => {
            const message = event.data;
            if (message.command === 'appendMessage') {
                appendMessage(message.text, message.role);
            } else if (message.command === 'appendMessageChunk') {
                appendMessageChunk(message.chunk);
            } else if (message.command === 'clearMessages') {
                messagesDiv.textContent = '';
                messagesDiv.appendChild(emptyState);
                emptyState.hidden = false;
                activeAssistantMessage = null;
                activeReasoningMessage = null;
                progressIndicator = null;
                toolCalls.clear();
                toolMessageElements.clear();
                agentThreads.clear();
                activeAgentThreadId = '';
                renderPlan(null);
                renderAgentThreads();
                updateStatusRail();
            } else if (message.command === 'setSessionInfo') {
                sessionInfo = message.sessionInfo;
                workingDirectory = message.workingDirectory || '';
                renderContext();
                updateSuggestions();
            } else if (message.command === 'setProcessStatus') {
                processStatus = message.status;
                updateStatusRail();
            } else if (message.command === 'setPromptState') {
                const wasRunning = promptState.isRunning;
                promptState = message.promptState || { isRunning: false, isCancelling: false };
                if (promptState.isRunning && !wasRunning) {
                    activeAssistantMessage = null;
                    activeReasoningMessage = null;
                }
                if (!promptState.isRunning && wasRunning && promptState.lastStopReason === 'cancelled') {
                    appendMessage('Cancelled.', 'system');
                }
                if (!promptState.isRunning) {
                    activeAssistantMessage = null;
                    activeReasoningMessage = null;
                }
                updateStatusRail();
                updateComposerState();
            } else if (message.command === 'setToolCall') {
                setToolCall(message.toolCall);
            } else if (message.command === 'clearToolCalls') {
                toolCalls.clear();
                toolMessageElements.clear();
                agentThreads.clear();
                activeAgentThreadId = '';
                renderTools();
                renderAgentThreads();
            } else if (message.command === 'setPlan') {
                renderPlan(message.plan);
            } else if (message.command === 'showClientRequest') {
                showClientRequest(message.request);
            } else if (message.command === 'resolveClientRequest') {
                clearClientRequest(message.requestId);
            } else if (message.command === 'prefillComposer') {
                inputField.value = message.text || '';
                showChatPage();
                inputField.focus();
                inputField.setSelectionRange(inputField.value.length, inputField.value.length);
                updateSuggestions();
                updateComposerState();
            } else if (message.command === 'showSettings') {
                showSettingsPage();
            } else if (message.command === 'sessionList') {
                showSessionBrowser(message.sessions || [], message.currentSessionId || '');
            } else if (message.command === 'fileMentions') {
                if (message.query === fileMentionQuery) {
                    fileMentionResults = Array.isArray(message.files) ? message.files : [];
                    visibleSuggestions = buildFileSuggestions(fileMentionResults);
                    activeSuggestionIndex = 0;
                    renderSuggestions();
                }
            }
        });

        function showSettingsPage() {
            activeView = 'settings';
            messagesDiv.classList.add('hidden');
            settingsPage.classList.remove('hidden');
            hideSuggestions();
            renderSettingsSummary();
            updateActivityVisibility();
        }

        function showChatPage() {
            activeView = 'chat';
            settingsPage.classList.add('hidden');
            messagesDiv.classList.remove('hidden');
            updateActivityVisibility();
            inputField.focus();
        }

        function runSettingsAction(button) {
            if (promptState.isRunning === true) {
                return;
            }

            const action = button.dataset.action || '';
            if (action === 'model') {
                showModelPicker();
                return;
            }

            if (action === 'vscodeSettings') {
                post({ command: 'openVsCodeSettings' });
                return;
            }

            if (action === 'sessions') {
                post({ command: 'listSessions' });
                return;
            }

            const commandText = button.dataset.command || '';
            if (commandText) {
                post({ command: 'runSessionCommand', text: commandText });
            }
        }

        function updateStatusRail() {
            const running = promptState.isRunning === true;
            stopButton.disabled = !running || promptState.isCancelling;
            statusText.textContent = formatStatusText(running);
            updateProgressIndicator();
            updateSettingsActionState();
        }

        function formatStatusText(running) {
            if (running) {
                return promptState.isCancelling ? 'Cancelling' : 'Running';
            }

            return formatProcessStatus(processStatus);
        }

        function formatProcessStatus(status) {
            if (status === 'starting') {
                return 'Starting';
            }

            if (status === 'running') {
                return 'Ready';
            }

            if (status === 'error') {
                return 'Error';
            }

            return 'Stopped';
        }

        function updateProgressIndicator() {
            const shouldShow = promptState.isRunning === true && !isSlashCommandText(promptState.input);
            if (!shouldShow) {
                if (progressIndicator) {
                    progressIndicator.remove();
                    progressIndicator = null;
                }
                return;
            }

            if (!progressIndicator) {
                progressIndicator = createProgressIndicator();
            }

            moveProgressIndicatorToEnd();
        }

        function createProgressIndicator() {
            const indicator = document.createElement('article');
            indicator.className = 'message-card assistant progress-indicator';
            indicator.dataset.text = '';
            indicator.setAttribute('role', 'status');
            indicator.setAttribute('aria-live', 'polite');

            const label = document.createElement('span');
            label.textContent = 'Thinking';
            indicator.appendChild(label);

            const dots = document.createElement('span');
            dots.className = 'progress-dots';
            for (let index = 0; index < 3; index += 1) {
                const dot = document.createElement('span');
                dot.textContent = '.';
                dots.appendChild(dot);
            }

            indicator.appendChild(dots);
            return indicator;
        }

        function moveProgressIndicatorToEnd() {
            if (progressIndicator) {
                messagesDiv.appendChild(progressIndicator);
            }
        }

        function isSlashCommandText(value) {
            return String(value || '').trimStart().startsWith('/');
        }

        function updateComposerState() {
            const hasText = inputField.value.trim().length > 0;
            inputField.disabled = promptState.isRunning === true;
            sendButton.disabled = promptState.isRunning === true || !hasText;
            modelButton.disabled = promptState.isRunning === true || !sessionInfo;
            profileSelect.disabled = promptState.isRunning === true;
            updateSettingsActionState();
        }

        function renderContext() {
            const models = sessionInfo && Array.isArray(sessionInfo.availableModelIds)
                ? sessionInfo.availableModelIds
                : [];

            sectionTitle.textContent = sessionInfo && sessionInfo.sectionTitle ? sessionInfo.sectionTitle : 'No active section';
            renderProfileSelect();
            renderModelSelect(models);
            renderSettingsSummary();
            renderContextMeter();

            updateStatusRail();
            updateComposerState();
        }

        function renderContextMeter() {
            const used = sessionInfo && sessionInfo.sectionEstimatedContextTokens;
            const window = sessionInfo && sessionInfo.activeModelContextWindowTokens;
            if (!used) {
                contextMeter.hidden = true;
                return;
            }

            contextMeter.hidden = false;
            if (window) {
                const pct = Math.min(100, Math.round((used / window) * 100));
                contextMeter.textContent = formatTokens(used) + ' / ' + formatTokens(window) + ' (' + pct + '%)';
                contextMeter.classList.toggle('context-meter-warn', pct >= 80);
                contextMeter.title = 'Context: ' + formatNumber(used) + ' of ' + formatNumber(window) + ' tokens';
            } else {
                contextMeter.textContent = formatTokens(used) + ' ctx';
                contextMeter.classList.remove('context-meter-warn');
                contextMeter.title = 'Context: ' + formatNumber(used) + ' tokens';
            }
        }

        function formatTokens(value) {
            if (value >= 1000) {
                return (value / 1000).toFixed(value >= 10000 ? 0 : 1) + 'k';
            }
            return String(value);
        }

        function renderSettingsSummary() {
            const provider = sessionInfo && sessionInfo.providerName ? sessionInfo.providerName : 'No provider';
            const model = sessionInfo && sessionInfo.modelId ? sessionInfo.modelId : 'No model';
            const profile = sessionInfo && sessionInfo.agentProfileName ? sessionInfo.agentProfileName : 'No profile';
            const thinking = sessionInfo && sessionInfo.thinkingMode ? sessionInfo.thinkingMode : 'default';

            settingsSummary.textContent = provider + ' / ' + model + ' / ' + profile + ' / thinking ' + thinking;
            settingsProviderValue.textContent = provider;
            settingsModelValue.textContent = model;
            settingsProfileValue.textContent = profile;
            settingsThinkingValue.textContent = thinking;
        }

        function updateSettingsActionState() {
            const disabled = promptState.isRunning === true;
            settingsPage.querySelectorAll('.settings-action').forEach(button => {
                button.disabled = disabled;
            });
        }

        function renderProfileSelect() {
            const profiles = sessionInfo && Array.isArray(sessionInfo.availableAgentProfiles)
                ? sessionInfo.availableAgentProfiles
                : [];
            const renderedProfiles = profiles.length > 0 ? profiles : fallbackProfiles;
            const profileName = sessionInfo && sessionInfo.agentProfileName
                ? sessionInfo.agentProfileName
                : getDefaultProfileName(renderedProfiles);
            profileSelect.textContent = '';
            renderedProfiles.forEach(profile => {
                const option = document.createElement('option');
                option.value = profile.name;
                option.textContent = profile.mode ? profile.name + ' (' + profile.mode + ')' : profile.name;
                if (profile.description) {
                    option.title = profile.description;
                }
                option.selected = profile.name.toLowerCase() === profileName.toLowerCase();
                profileSelect.appendChild(option);
            });

            if (!renderedProfiles.some(profile => profile.name.toLowerCase() === profileName.toLowerCase())) {
                const option = document.createElement('option');
                option.value = profileName;
                option.textContent = profileName;
                option.selected = true;
                profileSelect.insertBefore(option, profileSelect.firstChild);
            }

            profileSelect.value = profileName;
            const activeProfile = renderedProfiles.find(profile =>
                profile.name.toLowerCase() === profileName.toLowerCase());
            profileSelect.title = activeProfile && activeProfile.description
                ? profileName + ' — ' + activeProfile.description
                : 'Profile: ' + profileName;
        }

        function renderModelSelect(models) {
            const activeModelId = sessionInfo && sessionInfo.modelId ? sessionInfo.modelId : '';
            modelButtonLabel.textContent = activeModelId || 'Model';
            modelButton.title = models.length > 0
                ? 'Choose model. Current: ' + (activeModelId || models[0])
                : 'Choose model';
        }

        function getDefaultProfileName(profiles) {
            const buildProfile = profiles.find(profile =>
                String(profile.name || '').toLowerCase() === 'build');
            return buildProfile ? buildProfile.name : 'build';
        }

        function formatNumber(value) {
            return new Intl.NumberFormat('en-US').format(value);
        }

        function renderPlan(plan) {
            const entries = plan && Array.isArray(plan.entries) ? plan.entries : [];
            hasPlanActivity = entries.length > 0;
            updateActivityVisibility();
            planList.textContent = '';
            planCount.textContent = entries.length + (entries.length === 1 ? ' step' : ' steps');

            if (entries.length === 0) {
                return;
            }

            entries.forEach(entry => {
                const item = document.createElement('div');
                item.className = 'plan-item ' + normalizeClass(entry.status);

                const dot = document.createElement('div');
                dot.className = 'plan-dot';
                item.appendChild(dot);

                const content = document.createElement('div');
                const text = document.createElement('div');
                text.className = 'plan-content';
                text.textContent = entry.content;
                content.appendChild(text);

                const status = document.createElement('div');
                status.className = 'plan-status';
                status.textContent = entry.status || 'pending';
                content.appendChild(status);

                item.appendChild(content);
                planList.appendChild(item);
            });
        }

        function setToolCall(toolCall) {
            if (!toolCall || !toolCall.toolCallId) {
                return;
            }

            const current = toolCalls.get(toolCall.toolCallId) || {};
            const merged = Object.assign({}, current, toolCall);
            if (!toolCall.content || toolCall.content.length === 0) {
                merged.content = current.content;
            }
            if (typeof toolCall.rawInput === 'undefined') {
                merged.rawInput = current.rawInput;
            }
            toolCalls.set(toolCall.toolCallId, merged);
            trackAgentThreads(merged);
            renderToolMessage(merged);
            renderAgentThreads();
            updateStatusRail();
        }

        function renderTools() {
            const calls = Array.from(toolCalls.values());
            calls.forEach(renderToolMessage);
            updateStatusRail();
        }

        function renderToolMessage(call) {
            if (!call || !call.toolCallId) {
                return;
            }

            activeReasoningMessage = null;
            let article = toolMessageElements.get(call.toolCallId);
            if (!article) {
                article = appendMessage('', 'tool');
                toolMessageElements.set(call.toolCallId, article);
            }

            const text = formatToolMessage(call);
            const body = article.querySelector('.message-text');
            article.className = 'message-card tool ' + normalizeClass(call.status || 'pending');
            article.dataset.text = text;
            body.textContent = '';
            body.appendChild(createToolMessageView(call));
            moveProgressIndicatorToEnd();
            messagesDiv.scrollTop = messagesDiv.scrollHeight;
        }

        function formatToolMessage(call) {
            const lines = [];
            const title = call.title || call.toolCallId;
            const status = call.status || 'pending';
            lines.push('Tool - ' + status);
            lines.push(title);

            if (call.kind) {
                lines.push('Kind: ' + call.kind);
            }

            if (typeof call.rawInput !== 'undefined') {
                lines.push('');
                lines.push('Arguments');
                lines.push(formatPayload(call.rawInput));
            }

            const output = Array.isArray(call.content) ? call.content.join('\\n') : '';
            if (output) {
                lines.push('');
                lines.push('Output');
                lines.push(output);
            }

            return lines.join('\\n');
        }

        function createToolMessageView(call) {
            const container = document.createElement('div');
            container.className = 'tool-message';

            const header = document.createElement('div');
            header.className = 'tool-message-header';

            const status = document.createElement('span');
            status.className = 'tool-message-status';
            status.textContent = call.status || 'pending';
            header.appendChild(status);

            const title = document.createElement('span');
            title.className = 'tool-message-title';
            title.textContent = call.title || call.toolCallId;
            title.title = call.title || call.toolCallId;
            header.appendChild(title);
            container.appendChild(header);

            if (call.kind) {
                const kind = document.createElement('div');
                kind.className = 'tool-message-kind';
                kind.textContent = call.kind;
                container.appendChild(kind);
            }

            const output = Array.isArray(call.content) ? call.content.join('\\n') : '';
            if (output) {
                const pre = document.createElement('pre');
                pre.className = 'tool-output-pre';
                renderLinkifiedText(pre, output);
                container.appendChild(pre);
            } else {
                const pending = document.createElement('div');
                pending.className = 'tool-pending';
                pending.textContent = 'Waiting for output...';
                container.appendChild(pending);
            }

            const diffModel = buildDiffModel(call);
            if (diffModel) {
                diffModel.forEach(file => container.appendChild(createDiffView(file)));
            } else if (typeof call.rawInput !== 'undefined') {
                const details = createDetails('Arguments', formatPayload(call.rawInput), false);
                details.className = 'tool-arguments';
                container.appendChild(details);
            }

            return container;
        }

        function createDiffView(file) {
            const wrap = document.createElement('div');
            wrap.className = 'diff-view';

            const header = document.createElement('div');
            header.className = 'diff-header';
            let added = 0;
            let removed = 0;
            file.lines.forEach(line => {
                if (line.type === 'add') { added += 1; }
                if (line.type === 'del') { removed += 1; }
            });
            const pathLink = createFileReferenceLink(file.path, file.path);
            header.appendChild(pathLink);
            const stat = document.createElement('span');
            stat.className = 'diff-stat';
            stat.textContent = '+' + added + ' -' + removed;
            header.appendChild(stat);
            wrap.appendChild(header);

            const pre = document.createElement('pre');
            pre.className = 'diff-body';
            file.lines.forEach(line => {
                const row = document.createElement('div');
                row.className = 'diff-line diff-' + line.type;
                const sign = line.type === 'add' ? '+' : line.type === 'del' ? '-' : line.type === 'meta' ? '' : ' ';
                row.textContent = sign + line.text;
                pre.appendChild(row);
            });
            wrap.appendChild(pre);
            return wrap;
        }

        function createDetails(summaryText, bodyText, open) {
            const details = document.createElement('details');
            details.open = open;

            const summary = document.createElement('summary');
            summary.textContent = summaryText;
            details.appendChild(summary);

            const pre = document.createElement('pre');
            renderLinkifiedText(pre, bodyText);
            details.appendChild(pre);
            return details;
        }

        function updateActivityVisibility() {
            sidePane.classList.toggle(
                'visible',
                activeView === 'chat' && (hasPlanActivity || agentThreads.size > 0));
        }

        function trackAgentThreads(call) {
            const descriptors = createAgentThreadDescriptors(call);
            if (descriptors.length === 0) {
                return;
            }

            descriptors.forEach(descriptor => {
                const current = agentThreads.get(descriptor.id) || {};
                const merged = Object.assign({}, current, descriptor, {
                    updatedAt: Date.now()
                });
                agentThreads.set(descriptor.id, merged);
            });

            if (!activeAgentThreadId || !agentThreads.has(activeAgentThreadId)) {
                activeAgentThreadId = descriptors[0].id;
            }
        }

        function createAgentThreadDescriptors(call) {
            if (!call || !call.toolCallId) {
                return [];
            }

            const rawInput = call.rawInput && typeof call.rawInput === 'object'
                ? call.rawInput
                : null;
            const output = Array.isArray(call.content) ? call.content.join('\n').trim() : '';
            const status = String(call.status || 'pending');

            if (rawInput && typeof rawInput.agent === 'string' && rawInput.agent.trim()) {
                return [{
                    id: call.toolCallId,
                    toolCallId: call.toolCallId,
                    agent: rawInput.agent.trim(),
                    task: typeof rawInput.task === 'string' ? rawInput.task.trim() : '',
                    context: typeof rawInput.context === 'string' ? rawInput.context.trim() : '',
                    status,
                    output,
                    source: 'delegate'
                }];
            }

            if (rawInput && Array.isArray(rawInput.tasks)) {
                return rawInput.tasks
                    .map((task, index) => {
                        if (!task || typeof task !== 'object' || typeof task.agent !== 'string' || !task.agent.trim()) {
                            return null;
                        }

                        return {
                            id: call.toolCallId + ':' + index,
                            toolCallId: call.toolCallId,
                            agent: task.agent.trim(),
                            task: typeof task.task === 'string' ? task.task.trim() : '',
                            context: typeof task.context === 'string' ? task.context.trim() : '',
                            ownership: typeof task.ownership === 'string' ? task.ownership.trim() : '',
                            status,
                            output,
                            source: 'orchestrate'
                        };
                    })
                    .filter(Boolean);
            }

            return [];
        }

        function renderAgentThreads() {
            const threads = getOrderedAgentThreads();
            agentThreadList.textContent = '';
            agentCount.textContent = threads.length + (threads.length === 1 ? ' thread' : ' threads');
            updateActivityVisibility();

            if (threads.length === 0) {
                activeAgentThreadId = '';
                return;
            }

            if (!activeAgentThreadId || !agentThreads.has(activeAgentThreadId)) {
                activeAgentThreadId = threads[0].id;
            }

            threads.forEach(thread => {
                const item = document.createElement('button');
                item.type = 'button';
                item.className = 'agent-thread-item' + (thread.id === activeAgentThreadId ? ' active' : '');
                item.addEventListener('click', () => {
                    activeAgentThreadId = thread.id;
                    renderAgentThreads();
                });

                const topLine = document.createElement('div');
                topLine.className = 'agent-thread-topline';

                const name = document.createElement('div');
                name.className = 'agent-thread-name';
                name.textContent = '@' + thread.agent;
                topLine.appendChild(name);

                const status = document.createElement('div');
                status.className = 'agent-thread-status';
                status.textContent = thread.status || 'pending';
                topLine.appendChild(status);

                item.appendChild(topLine);

                if (thread.task) {
                    const task = document.createElement('div');
                    task.className = 'agent-thread-task';
                    task.textContent = thread.task;
                    item.appendChild(task);
                }

                const metaParts = [];
                if (thread.source === 'orchestrate') {
                    metaParts.push('orchestrated');
                }
                if (thread.ownership) {
                    metaParts.push(thread.ownership);
                }
                if (thread.context) {
                    metaParts.push(thread.context);
                }
                if (metaParts.length > 0) {
                    const meta = document.createElement('div');
                    meta.className = 'agent-thread-meta';
                    meta.textContent = metaParts.join(' | ');
                    item.appendChild(meta);
                }

                if (thread.id === activeAgentThreadId && thread.output) {
                    const output = document.createElement('div');
                    output.className = 'agent-thread-output';
                    renderLinkifiedText(output, truncateForAgentInspector(thread.output));
                    item.appendChild(output);
                }

                agentThreadList.appendChild(item);
            });
        }

        function switchAgentThread(delta) {
            const threads = getOrderedAgentThreads();
            if (threads.length === 0) {
                return;
            }

            const currentIndex = Math.max(0, threads.findIndex(thread => thread.id === activeAgentThreadId));
            const nextIndex = (currentIndex + delta + threads.length) % threads.length;
            activeAgentThreadId = threads[nextIndex].id;
            renderAgentThreads();
        }

        function getOrderedAgentThreads() {
            return Array.from(agentThreads.values()).sort((left, right) => {
                return Number(right.updatedAt || 0) - Number(left.updatedAt || 0);
            });
        }

        function truncateForAgentInspector(text) {
            const normalized = String(text || '').trim();
            if (normalized.length <= 500) {
                return normalized;
            }

            return normalized.slice(0, 497) + '...';
        }

        function formatPayload(value) {
            if (typeof value === 'string') {
                return value;
            }

            try {
                return JSON.stringify(value, null, 2);
            } catch {
                return String(value);
            }
        }

        function normalizeClass(value) {
            return String(value || '').toLowerCase().replace(/[^a-z0-9_-]+/g, '_');
        }

        function showModelPicker() {
            if (activeModalRequest) {
                return;
            }

            const models = getAvailableModelIds();
            if (models.length === 0) {
                post({ command: 'selectModel' });
                return;
            }

            modalTitle.textContent = 'Choose model';
            const activeModelId = sessionInfo && sessionInfo.modelId ? sessionInfo.modelId : '';
            modalDescription.textContent = activeModelId ? 'Current: ' + activeModelId : '';
            modalDescription.hidden = !activeModelId;
            modalBody.textContent = '';
            modalActions.textContent = '';

            const options = document.createElement('div');
            options.className = 'modal-options';
            modalBody.appendChild(options);

            models.forEach(modelId => {
                const button = document.createElement('button');
                button.className = 'modal-option' + (modelId === activeModelId ? ' active' : '');
                button.addEventListener('click', () => {
                    if (modelId !== activeModelId) {
                        post({ command: 'changeModel', modelId });
                    }

                    closeModal();
                });

                const name = document.createElement('strong');
                name.textContent = modelId;
                button.appendChild(name);

                if (modelId === activeModelId) {
                    const current = document.createElement('span');
                    current.textContent = 'Current';
                    button.appendChild(current);
                }

                options.appendChild(button);
            });

            const close = document.createElement('button');
            close.className = 'ghost-button';
            close.textContent = 'Close';
            close.addEventListener('click', closeModal);
            modalActions.appendChild(close);
            modalBackdrop.classList.remove('hidden');
        }

        function showSessionBrowser(sessions, currentSessionId) {
            if (activeModalRequest) {
                return;
            }

            modalTitle.textContent = 'Sessions';
            modalDescription.textContent = sessions.length === 1 ? '1 saved session' : sessions.length + ' saved sessions';
            modalDescription.hidden = false;
            modalBody.textContent = '';
            modalActions.textContent = '';

            const options = document.createElement('div');
            options.className = 'modal-options';
            modalBody.appendChild(options);

            if (sessions.length === 0) {
                const empty = document.createElement('div');
                empty.className = 'empty-panel';
                empty.textContent = 'No saved sessions yet.';
                options.appendChild(empty);
            }

            sessions.forEach(session => {
                const button = document.createElement('button');
                button.className = 'modal-option' + (session.sessionId === currentSessionId ? ' active' : '');
                button.addEventListener('click', () => {
                    if (session.sessionId !== currentSessionId) {
                        post({ command: 'resumeSession', sessionId: session.sessionId });
                    }
                    closeModal();
                });

                const name = document.createElement('strong');
                name.textContent = session.title || 'Untitled session';
                button.appendChild(name);

                const metaParts = [];
                if (session.modelId) {
                    metaParts.push(session.modelId);
                }
                if (typeof session.turnCount === 'number') {
                    metaParts.push(session.turnCount + (session.turnCount === 1 ? ' turn' : ' turns'));
                }
                if (session.parentSessionId) {
                    metaParts.push('fork');
                }
                const when = formatRelativeTime(session.updatedAtUtc);
                if (when) {
                    metaParts.push(when);
                }
                if (session.sessionId === currentSessionId) {
                    metaParts.push('current');
                }

                const meta = document.createElement('span');
                meta.textContent = metaParts.join(' · ');
                button.appendChild(meta);

                options.appendChild(button);
            });

            const fork = document.createElement('button');
            fork.className = 'ghost-button';
            fork.textContent = 'Fork current';
            fork.title = 'Fork the active session';
            fork.addEventListener('click', () => {
                post({ command: 'runSessionCommand', text: '/fork' });
                closeModal();
            });

            const exportButton = document.createElement('button');
            exportButton.className = 'ghost-button';
            exportButton.textContent = 'Export current';
            exportButton.title = 'Export the active session to JSON';
            exportButton.addEventListener('click', () => {
                post({ command: 'runSessionCommand', text: '/export json' });
                closeModal();
            });

            const close = document.createElement('button');
            close.className = 'ghost-button';
            close.textContent = 'Close';
            close.addEventListener('click', closeModal);

            modalActions.appendChild(fork);
            modalActions.appendChild(exportButton);
            modalActions.appendChild(close);
            modalBackdrop.classList.remove('hidden');
        }

        function formatRelativeTime(iso) {
            if (!iso) {
                return '';
            }

            const then = Date.parse(iso);
            if (!Number.isFinite(then)) {
                return '';
            }

            const diff = Date.now() - then;
            if (diff < 0) {
                return '';
            }

            const minutes = Math.floor(diff / 60000);
            if (minutes < 1) {
                return 'just now';
            }
            if (minutes < 60) {
                return minutes + 'm ago';
            }
            const hours = Math.floor(minutes / 60);
            if (hours < 24) {
                return hours + 'h ago';
            }
            const days = Math.floor(hours / 24);
            if (days < 30) {
                return days + 'd ago';
            }
            return Math.floor(days / 30) + 'mo ago';
        }

        function getAvailableModelIds() {
            const activeModelId = sessionInfo && sessionInfo.modelId ? sessionInfo.modelId : '';
            const models = sessionInfo && Array.isArray(sessionInfo.availableModelIds)
                ? sessionInfo.availableModelIds
                : [];
            const unique = [];
            const seen = new Set();

            if (activeModelId) {
                unique.push(activeModelId);
                seen.add(activeModelId);
            }

            models.forEach(modelId => {
                if (typeof modelId !== 'string') {
                    return;
                }

                const normalizedModelId = modelId.trim();
                if (!normalizedModelId || seen.has(normalizedModelId)) {
                    return;
                }

                unique.push(normalizedModelId);
                seen.add(normalizedModelId);
            });

            return unique;
        }

        function closeModal() {
            clearAutoSelectTimer();
            activeModalRequest = null;
            modalBackdrop.classList.add('hidden');
            modalTitle.textContent = '';
            modalDescription.textContent = '';
            modalDescription.hidden = true;
            modalBody.textContent = '';
            modalActions.textContent = '';
            inputField.focus();
        }

        function showClientRequest(request) {
            if (!request || !request.id) {
                return;
            }

            if (activeModalRequest && activeModalRequest.id === request.id) {
                return;
            }

            if (queuedClientRequests.some(queuedRequest => queuedRequest.id === request.id)) {
                return;
            }

            if (activeModalRequest) {
                queuedClientRequests.push(request);
                return;
            }

            openClientRequest(request);
        }

        function openClientRequest(request) {
            clearAutoSelectTimer();
            activeModalRequest = request;
            modalTitle.textContent = request.kind === 'text' ? request.label : request.title;
            modalDescription.textContent = request.description || '';
            modalDescription.hidden = !request.description;
            modalBody.textContent = '';
            modalActions.textContent = '';

            if (request.kind === 'text') {
                renderTextRequest(request);
            } else {
                renderPermissionRequest(request);
            }

            modalBackdrop.classList.remove('hidden');
        }

        function renderTextRequest(request) {
            const input = document.createElement('input');
            input.className = 'modal-input';
            input.type = request.isSecret ? 'password' : 'text';
            input.value = request.defaultValue || '';
            input.autocomplete = request.isSecret ? 'off' : 'on';
            modalBody.appendChild(input);

            const submit = document.createElement('button');
            submit.className = 'primary-button';
            submit.textContent = 'Submit';
            submit.addEventListener('click', () => {
                const value = input.value;
                if (request.isSecret) {
                    input.value = '';
                }
                resolveActiveRequest({ outcome: 'submitted', value });
            });

            if (request.allowCancellation && shouldShowBackButton(request)) {
                modalActions.appendChild(createBackButton());
            }

            modalActions.appendChild(submit);

            if (request.allowCancellation && !shouldShowBackButton(request)) {
                modalActions.appendChild(createCancelButton());
            }

            input.addEventListener('keydown', event => {
                if (event.key === 'Enter') {
                    event.preventDefault();
                    submit.click();
                }
                if (event.key === 'Escape' && request.allowCancellation) {
                    event.preventDefault();
                    cancelActiveRequest();
                }
            });

            setTimeout(() => input.focus(), 0);
        }

        function renderPermissionRequest(request) {
            const requestOptions = Array.isArray(request.options) ? request.options : [];
            const defaultOptionId = getDefaultOptionId(request, requestOptions);
            const options = document.createElement('div');
            options.className = 'modal-options';
            modalBody.appendChild(options);

            requestOptions.forEach(option => {
                const button = document.createElement('button');
                button.className = 'modal-option' + (option.optionId === defaultOptionId ? ' active' : '');
                button.addEventListener('click', () => {
                    resolveActiveRequest({ outcome: 'selected', optionId: option.optionId });
                });

                const name = document.createElement('strong');
                name.textContent = option.name || option.optionId;
                button.appendChild(name);

                const optionKind = formatOptionKind(option.kind);
                if (optionKind) {
                    const kind = document.createElement('span');
                    kind.textContent = optionKind;
                    button.appendChild(kind);
                }

                options.appendChild(button);
            });

            if (request.allowCancellation !== false) {
                modalActions.appendChild(shouldShowBackButton(request) ? createBackButton() : createCancelButton());
            }

            startPermissionAutoSelectCountdown(request, defaultOptionId, requestOptions);
        }

        function getDefaultOptionId(request, options) {
            const requestedDefault = String(request.defaultOptionId || '').trim();
            if (requestedDefault && options.some(option => option.optionId === requestedDefault)) {
                return requestedDefault;
            }

            return options.length > 0 ? options[0].optionId : '';
        }

        function startPermissionAutoSelectCountdown(request, defaultOptionId, options) {
            const delayMilliseconds = Number(request.autoSelectAfterMilliseconds);
            if (!defaultOptionId ||
                !Number.isFinite(delayMilliseconds) ||
                delayMilliseconds <= 0) {
                return;
            }

            const defaultOption = options.find(option => option.optionId === defaultOptionId);
            const defaultLabel = defaultOption ? (defaultOption.name || defaultOption.optionId) : defaultOptionId;
            const deadline = Date.now() + delayMilliseconds;
            const countdown = document.createElement('div');
            countdown.className = 'modal-countdown';
            modalActions.insertBefore(countdown, modalActions.firstChild);

            const updateCountdown = () => {
                if (!activeModalRequest || activeModalRequest.id !== request.id) {
                    clearAutoSelectTimer();
                    return;
                }

                const remainingMilliseconds = deadline - Date.now();
                const remainingSeconds = Math.max(0, Math.ceil(remainingMilliseconds / 1000));
                countdown.textContent = 'Auto-selecting ' + defaultLabel + ' in ' + remainingSeconds + 's';

                if (remainingMilliseconds <= 0) {
                    resolveActiveRequest({ outcome: 'selected', optionId: defaultOptionId });
                }
            };

            activeAutoSelectTimer = setInterval(updateCountdown, 250);
            updateCountdown();
        }

        function clearAutoSelectTimer() {
            if (activeAutoSelectTimer) {
                clearInterval(activeAutoSelectTimer);
                activeAutoSelectTimer = null;
            }
        }

        function createBackButton() {
            const back = document.createElement('button');
            back.className = 'ghost-button';
            back.textContent = 'Back';
            back.addEventListener('click', cancelActiveRequest);
            return back;
        }

        function createCancelButton() {
            const cancel = document.createElement('button');
            cancel.className = 'ghost-button';
            cancel.textContent = 'Cancel';
            cancel.addEventListener('click', cancelActiveRequest);
            return cancel;
        }

        function shouldShowBackButton(request) {
            const title = String(request.title || request.label || '').trim().toLowerCase();
            const description = String(request.description || '').trim().toLowerCase();
            return title === 'api key' ||
                title === 'base url' ||
                title === 'choose subscription provider' ||
                title === 'choose api key provider' ||
                title === 'choose local provider' ||
                description.includes('returns to provider setup type');
        }

        function formatOptionKind(kind) {
            const normalized = String(kind || '').trim();
            if (!normalized || ['allow_once', 'allow_always', 'reject_once', 'reject_always'].includes(normalized)) {
                return '';
            }

            return normalized.replace(/_/g, ' ');
        }

        function resolveActiveRequest(resolution) {
            if (!activeModalRequest) {
                return;
            }

            const requestId = activeModalRequest.id;
            post({ command: 'resolveClientRequest', requestId, resolution });
            clearClientRequest(requestId);
        }

        function cancelActiveRequest() {
            resolveActiveRequest({ outcome: 'cancelled' });
        }

        function clearClientRequest(requestId) {
            if (!activeModalRequest || activeModalRequest.id !== requestId) {
                const queuedIndex = queuedClientRequests.findIndex(request => request.id === requestId);
                if (queuedIndex >= 0) {
                    queuedClientRequests.splice(queuedIndex, 1);
                }
                return;
            }

            modalBody.querySelectorAll('input').forEach(input => {
                input.value = '';
            });
            closeModal();
            showNextClientRequest();
        }

        function showNextClientRequest() {
            const nextRequest = queuedClientRequests.shift();
            if (nextRequest) {
                openClientRequest(nextRequest);
            }
        }

        function updateSuggestions() {
            const value = inputField.value;
            const mention = detectMentionQuery(value, inputField.selectionStart);
            if (mention !== null) {
                if (mention !== fileMentionQuery) {
                    fileMentionQuery = mention;
                    fileMentionResults = [];
                    post({ command: 'searchFiles', query: mention });
                }
                visibleSuggestions = buildFileSuggestions(fileMentionResults);
                activeSuggestionIndex = 0;
                renderSuggestions();
                return;
            }

            fileMentionQuery = null;
            visibleSuggestions = createSuggestions(value);
            activeSuggestionIndex = 0;
            renderSuggestions();
        }

        function detectMentionQuery(value, caret) {
            const cursor = typeof caret === 'number' ? caret : value.length;
            const before = value.slice(0, cursor);
            const match = /(^|\\s)@([^\\s@]*)$/.exec(before);
            return match ? match[2] : null;
        }

        function buildFileSuggestions(files) {
            return files.map(file => ({
                usage: file,
                description: 'Insert file path',
                insertText: file,
                mention: true
            }));
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

            if (value.startsWith('/profile ')) {
                const query = value.slice('/profile '.length).trim().toLowerCase();
                const profiles = sessionInfo && Array.isArray(sessionInfo.availableAgentProfiles)
                    ? sessionInfo.availableAgentProfiles
                    : fallbackProfiles;

                return profiles
                    .filter(profile => !query || profile.name.toLowerCase().includes(query))
                    .slice(0, 8)
                    .map(profile => ({
                        usage: profile.name,
                        description: profile.name === (sessionInfo && sessionInfo.agentProfileName)
                            ? 'Current profile'
                            : (profile.description || 'Switch profile'),
                        insertText: '/profile ' + profile.name
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
            if (suggestion.mention) {
                const caret = typeof inputField.selectionStart === 'number'
                    ? inputField.selectionStart
                    : inputField.value.length;
                const before = inputField.value.slice(0, caret).replace(/@[^\\s@]*$/, suggestion.insertText + ' ');
                const after = inputField.value.slice(caret);
                inputField.value = before + after;
                inputField.focus();
                inputField.setSelectionRange(before.length, before.length);
                fileMentionQuery = null;
                hideSuggestions();
                updateComposerState();
                return;
            }

            inputField.value = suggestion.insertText;
            inputField.focus();
            inputField.setSelectionRange(inputField.value.length, inputField.value.length);
            updateSuggestions();
            updateComposerState();
        }

        function hideSuggestions() {
            visibleSuggestions = [];
            suggestionsDiv.classList.add('hidden');
            suggestionsDiv.textContent = '';
        }

        renderContext();
        renderPlan(null);
        renderAgentThreads();
        renderTools();
        updateStatusRail();
        updateComposerState();
        post({ command: 'ready' });
    </script>
</body>
</html>`;
}


function formatTokenCount(value: number): string {
    if (value >= 1000) {
        return `${(value / 1000).toFixed(value >= 10000 ? 0 : 1)}k`;
    }
    return String(Math.round(value));
}

function getNonce(): string {
    const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    let result = '';
    const array = new Uint8Array(32);
    crypto.getRandomValues(array);
    for (let i = 0; i < array.length; i++) {
        result += chars[array[i] % chars.length];
    }
    return result;
}
