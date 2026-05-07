import { AcpClient } from './AcpClient';
import { NanoAgentProcessManager } from './NanoAgentProcessManager';
import { LogService } from './LogService';
import { AcpNotification, AcpRequest } from '../types/acp';
import { EventEmitter } from 'events';
import * as vscode from 'vscode';
import * as path from 'path';

type SessionNewResult = {
    sessionId: string;
};

type SessionPromptResult = {
    stopReason?: string;
};

export type SessionInfo = {
    sessionId: string;
    sectionResumeCommand?: string;
    providerName?: string;
    modelId?: string;
    activeModelContextWindowTokens?: number | null;
    availableModelIds: string[];
    thinkingMode?: string;
    agentProfileName?: string;
    sectionTitle?: string;
};

export class SessionManager extends EventEmitter {
    private acpClient: AcpClient | null = null;
    private currentSessionInfo: SessionInfo | null = null;
    private initializePromise: Promise<void> | null = null;
    private promptTail: Promise<void> = Promise.resolve();
    private sessionId: string | null = null;
    private sessionPromise: Promise<string> | null = null;
    private logService: LogService;
    
    constructor(private processManager: NanoAgentProcessManager) {
        super();
        this.logService = LogService.getInstance();
        
        // Listen to process manager events to wire up ACP
        this.processManager.on('status', (status) => {
            if (status === 'running') {
                this.initializeSession();
            } else if (status === 'stopped' || status === 'error') {
                this.terminateSession();
            }
        });
        
        // If already running, wire it up immediately
        if (this.processManager.getProcess()) {
            this.initializeSession();
        }
    }

    public async ensureStarted(): Promise<void> {
        if (!this.processManager.getProcess()) {
            await this.processManager.start();
        }

        if (!this.acpClient) {
            this.initializeSession();
        }

        if (!this.acpClient) {
            throw new Error('NanoAgent ACP server did not expose stdio.');
        }
    }

    public async sendPrompt(text: string): Promise<string> {
        const runPrompt = this.promptTail.then(
            () => this.sendPromptCore(text),
            () => this.sendPromptCore(text)
        );
        this.promptTail = runPrompt.then(
            () => undefined,
            () => undefined
        );

        return runPrompt;
    }

    public getSessionInfo(): SessionInfo | null {
        return this.currentSessionInfo;
    }

    private async sendPromptCore(text: string): Promise<string> {
        const client = await this.getReadyClient();
        const sessionId = await this.ensureSession(client);
        const chunks: string[] = [];

        const onNotification = (message: AcpNotification) => {
            const chunk = this.tryReadAgentMessageChunk(message, sessionId);
            if (chunk) {
                chunks.push(chunk);
            }
        };

        client.on('notification', onNotification);

        let stopReason: string | undefined;
        try {
            const result = await client.sendRequest<SessionPromptResult>('session/prompt', {
                sessionId,
                prompt: [
                    {
                        type: 'text',
                        text
                    }
                ]
            });
            stopReason = result?.stopReason;
        } catch (error) {
            throw this.normalizeError(error);
        } finally {
            client.off('notification', onNotification);
        }

        const response = chunks.join('\n').trim();
        if (response) {
            return response;
        }

        return stopReason === 'cancelled'
            ? 'Cancelled.'
            : '';
    }

    private initializeSession() {
        if (this.acpClient) {
            return;
        }

        const process = this.processManager.getProcess();
        if (!process || !process.stdout || !process.stdin) {
            this.logService.error('Cannot initialize session: Process or stdio is not available');
            return;
        }

        this.acpClient = new AcpClient();

        // Wire stdout to ACP Client
        process.stdout.on('data', (data) => {
            if (this.acpClient) {
                this.acpClient.handleData(data);
            }
        });

        // Wire ACP Client sends to stdin
        this.acpClient.on('send', (data: string) => {
            if (process && process.stdin) {
                process.stdin.write(data);
            }
        });

        // Handle notifications and requests
        this.acpClient.on('notification', (msg) => this.handleNotification(msg));

        this.acpClient.on('request', (msg) => {
            void this.handleClientRequest(msg);
        });

        this.logService.info('ACP Session initialized');
    }

    private terminateSession() {
        if (this.acpClient) {
            this.acpClient.rejectAllPendingRequests();
            this.acpClient.removeAllListeners();
            this.acpClient = null;
            this.currentSessionInfo = null;
            this.initializePromise = null;
            this.promptTail = Promise.resolve();
            this.sessionId = null;
            this.sessionPromise = null;
            this.emit('sessionInfoChanged', null);
            this.logService.info('ACP Session terminated');
        }
    }

    private async getReadyClient(): Promise<AcpClient> {
        await this.ensureStarted();

        const client = this.acpClient;
        if (!client) {
            throw new Error('NanoAgent ACP client is not initialized.');
        }

        await this.ensureInitialized(client);
        return client;
    }

    private async ensureInitialized(client: AcpClient): Promise<void> {
        if (!this.initializePromise) {
            this.initializePromise = client
                .sendRequest('initialize', { protocolVersion: 1 })
                .then(() => undefined)
                .catch((error) => {
                    this.initializePromise = null;
                    throw this.normalizeError(error);
                });
        }

        await this.initializePromise;
    }

    private async ensureSession(client: AcpClient): Promise<string> {
        if (this.sessionId) {
            return this.sessionId;
        }

        if (!this.sessionPromise) {
            this.sessionPromise = client
                .sendRequest<SessionNewResult>('session/new', {
                    cwd: this.getWorkingDirectory()
                })
                .then((result) => {
                    if (!result || typeof result.sessionId !== 'string' || !result.sessionId.trim()) {
                        throw new Error('NanoAgent ACP server returned an invalid session.');
                    }

                    this.sessionId = result.sessionId;
                    return this.sessionId;
                })
                .catch((error) => {
                    this.sessionPromise = null;
                    throw this.normalizeError(error);
                });
        }

        return this.sessionPromise;
    }

    private async handleClientRequest(message: AcpRequest): Promise<void> {
        const client = this.acpClient;
        if (!client) {
            return;
        }

        switch (message.method) {
            case 'session/request_permission':
                await this.handlePermissionRequest(client, message);
                return;

            case 'session/request_text':
                await this.handleTextRequest(client, message);
                return;

            default:
                client.sendError(message.id, -32601, `Method '${message.method}' is not supported.`);
        }
    }

    private handleNotification(message: AcpNotification): void {
        this.logService.debug('SessionManager received notification', message);

        const sessionInfo = this.tryReadSessionInfo(message);
        if (sessionInfo) {
            this.currentSessionInfo = sessionInfo;
            this.emit('sessionInfoChanged', sessionInfo);
        }
    }

    private async handlePermissionRequest(client: AcpClient, message: AcpRequest): Promise<void> {
        try {
            const selection = await this.showPermissionPicker(message.params);
            client.sendResponse(message.id, {
                outcome: selection
                    ? {
                        outcome: 'selected',
                        optionId: selection.optionId
                    }
                    : {
                        outcome: 'cancelled'
                    }
            });
        } catch (error) {
            const normalized = this.normalizeError(error);
            this.logService.error('Permission request failed', normalized);
            client.sendResponse(message.id, {
                outcome: {
                    outcome: 'cancelled'
                }
            });
        }
    }

    private async handleTextRequest(client: AcpClient, message: AcpRequest): Promise<void> {
        try {
            const value = await this.showTextInput(message.params);
            client.sendResponse(message.id, {
                outcome: value === null
                    ? {
                        outcome: 'cancelled'
                    }
                    : {
                        outcome: 'submitted',
                        value
                    }
            });
        } catch (error) {
            const normalized = this.normalizeError(error);
            this.logService.error('Text request failed', normalized);
            client.sendResponse(message.id, {
                outcome: {
                    outcome: 'cancelled'
                }
            });
        }
    }

    private async showPermissionPicker(params: unknown): Promise<PermissionSelection | null> {
        const request = this.readPermissionRequest(params);
        if (request.options.length === 0) {
            return null;
        }

        const picked = await vscode.window.showQuickPick(
            request.options.map((option) => ({
                label: option.name,
                description: option.kind,
                detail: request.title,
                optionId: option.optionId
            })),
            {
                title: 'NanoAgent Permission',
                placeHolder: request.title,
                ignoreFocusOut: true
            }
        );

        return picked
            ? {
                optionId: picked.optionId
            }
            : null;
    }

    private readPermissionRequest(params: unknown): PermissionRequest {
        if (!params || typeof params !== 'object') {
            return {
                title: 'NanoAgent wants permission to continue.',
                options: []
            };
        }

        const request = params as {
            toolCall?: {
                title?: unknown;
            };
            options?: unknown;
        };

        const title = typeof request.toolCall?.title === 'string' && request.toolCall.title.trim()
            ? request.toolCall.title.trim()
            : 'NanoAgent wants permission to continue.';
        const options = Array.isArray(request.options)
            ? request.options
                .map((option) => this.readPermissionOption(option))
                .filter((option): option is PermissionOption => option !== null)
            : [];

        return {
            title,
            options
        };
    }

    private readPermissionOption(value: unknown): PermissionOption | null {
        if (!value || typeof value !== 'object') {
            return null;
        }

        const option = value as {
            optionId?: unknown;
            name?: unknown;
            kind?: unknown;
        };

        if (typeof option.optionId !== 'string' || !option.optionId.trim()) {
            return null;
        }

        return {
            optionId: option.optionId,
            name: typeof option.name === 'string' && option.name.trim()
                ? option.name.trim()
                : option.optionId,
            kind: typeof option.kind === 'string'
                ? option.kind
                : ''
        };
    }

    private async showTextInput(params: unknown): Promise<string | null> {
        const request = this.readTextRequest(params);
        let value: string | undefined;

        do {
            value = await vscode.window.showInputBox({
                title: request.label,
                prompt: request.description,
                value: request.defaultValue ?? '',
                password: request.isSecret,
                ignoreFocusOut: true
            });

            if (value !== undefined) {
                return value;
            }

            if (request.allowCancellation) {
                return null;
            }
        } while (value === undefined);

        return null;
    }

    private readTextRequest(params: unknown): TextRequest {
        if (!params || typeof params !== 'object') {
            return {
                label: 'NanoAgent input',
                isSecret: false,
                allowCancellation: true
            };
        }

        const request = params as {
            label?: unknown;
            description?: unknown;
            defaultValue?: unknown;
            isSecret?: unknown;
            allowCancellation?: unknown;
        };

        return {
            label: typeof request.label === 'string' && request.label.trim()
                ? request.label.trim()
                : 'NanoAgent input',
            description: typeof request.description === 'string'
                ? request.description
                : undefined,
            defaultValue: typeof request.defaultValue === 'string'
                ? request.defaultValue
                : undefined,
            isSecret: request.isSecret === true,
            allowCancellation: request.allowCancellation !== false
        };
    }

    private getWorkingDirectory(): string {
        const config = vscode.workspace.getConfiguration('nanoagent');
        const configuredDirectory = config.get<string>('workingDirectory');
        if (configuredDirectory && configuredDirectory.trim()) {
            const trimmedDirectory = configuredDirectory.trim();
            if (path.isAbsolute(trimmedDirectory)) {
                return trimmedDirectory;
            }

            const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
            if (workspaceFolder) {
                return path.resolve(workspaceFolder.uri.fsPath, trimmedDirectory);
            }

            return path.resolve(trimmedDirectory);
        }

        const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
        if (workspaceFolder) {
            return workspaceFolder.uri.fsPath;
        }

        return process.cwd();
    }

    private tryReadAgentMessageChunk(message: AcpNotification, sessionId: string): string | null {
        if (message.method !== 'session/update' || !message.params || typeof message.params !== 'object') {
            return null;
        }

        const params = message.params as {
            sessionId?: unknown;
            update?: {
                sessionUpdate?: unknown;
                content?: {
                    type?: unknown;
                    text?: unknown;
                };
            };
        };

        if (params.sessionId !== sessionId ||
            params.update?.sessionUpdate !== 'agent_message_chunk' ||
            params.update.content?.type !== 'text' ||
            typeof params.update.content.text !== 'string') {
            return null;
        }

        return params.update.content.text;
    }

    private tryReadSessionInfo(message: AcpNotification): SessionInfo | null {
        if (message.method !== 'session/update' || !message.params || typeof message.params !== 'object') {
            return null;
        }

        const params = message.params as {
            sessionId?: unknown;
            update?: {
                sessionUpdate?: unknown;
                sectionResumeCommand?: unknown;
                providerName?: unknown;
                modelId?: unknown;
                activeModelContextWindowTokens?: unknown;
                availableModelIds?: unknown;
                thinkingMode?: unknown;
                agentProfileName?: unknown;
                sectionTitle?: unknown;
            };
        };

        if (typeof params.sessionId !== 'string' ||
            params.update?.sessionUpdate !== 'session_info_update') {
            return null;
        }

        return {
            sessionId: params.sessionId,
            sectionResumeCommand: this.optionalString(params.update.sectionResumeCommand),
            providerName: this.optionalString(params.update.providerName),
            modelId: this.optionalString(params.update.modelId),
            activeModelContextWindowTokens: typeof params.update.activeModelContextWindowTokens === 'number'
                ? params.update.activeModelContextWindowTokens
                : null,
            availableModelIds: Array.isArray(params.update.availableModelIds)
                ? params.update.availableModelIds.filter((modelId): modelId is string => typeof modelId === 'string')
                : [],
            thinkingMode: this.optionalString(params.update.thinkingMode),
            agentProfileName: this.optionalString(params.update.agentProfileName),
            sectionTitle: this.optionalString(params.update.sectionTitle)
        };
    }

    private optionalString(value: unknown): string | undefined {
        return typeof value === 'string' && value.trim()
            ? value
            : undefined;
    }

    private normalizeError(error: unknown): Error {
        if (error instanceof Error) {
            return error;
        }

        if (error && typeof error === 'object' && 'message' in error) {
            const message = (error as { message?: unknown }).message;
            if (typeof message === 'string' && message.trim()) {
                return new Error(message);
            }
        }

        return new Error('NanoAgent ACP request failed.');
    }
}

type PermissionRequest = {
    title: string;
    options: PermissionOption[];
};

type PermissionOption = PermissionSelection & {
    name: string;
    kind: string;
};

type PermissionSelection = {
    optionId: string;
};

type TextRequest = {
    label: string;
    description?: string;
    defaultValue?: string;
    isSecret: boolean;
    allowCancellation: boolean;
};
