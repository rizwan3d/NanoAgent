import * as vscode from 'vscode';

export function getChatWebviewContent(webview: vscode.Webview, extensionUri: vscode.Uri, nonce: string) {
    const styleUri = webview.asWebviewUri(vscode.Uri.joinPath(extensionUri, 'media', 'chat.css'));
    const scriptUri = webview.asWebviewUri(vscode.Uri.joinPath(extensionUri, 'dist', 'webview.js'));

    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta http-equiv="Content-Security-Policy"
          content="default-src 'none'; style-src ${webview.cspSource}; script-src 'nonce-${nonce}'; img-src ${webview.cspSource} data:; connect-src 'none'; form-action 'none'; frame-ancestors 'none'; base-uri 'none';">
    <title>NanoAgent</title>
    <link href="${styleUri}" rel="stylesheet">
</head>
<body>
    <div class="workbench">
        <main class="main-grid">
            <section class="chat-pane">
                <header class="chat-header">
                    <div class="chat-header-copy">
                        <span class="chat-kicker">NanoAgent</span>
                        <span id="section-title" class="chat-section-title">No active section</span>
                    </div>
                    <div class="chat-header-actions">
                        <button id="sessions-button" class="icon-button top-action" title="Sections" aria-label="Sections">&#9776;</button>
                        <button id="settings-button" class="icon-button top-action" title="Settings" aria-label="Settings">&#9881;</button>
                    </div>
                </header>
                <div id="messages" class="messages">
                    <div id="empty-state" class="empty-state">
                        <div class="empty-title">NanoAgent</div>
                        <div class="empty-hint">A compact coding agent for edits, files, tools, and focused workspace help.</div>
                        <div class="empty-keys"><kbd>/</kbd> commands<span class="empty-dot">·</span><kbd>@</kbd> files<span class="empty-dot">·</span><kbd>Shift</kbd>+<kbd>Enter</kbd> newline</div>
                    </div>
                </div>
                <section id="settings-page" class="settings-page hidden" aria-label="NanoAgent settings">
                    <div class="settings-shell">
                        <div class="settings-hero">
                            <div class="settings-header">
                                <div class="settings-kicker">Control center</div>
                                <div class="settings-title">
                                    <h1>Settings</h1>
                                    <div id="settings-summary" class="settings-summary">No active session.</div>
                                </div>
                                <div class="settings-quick" aria-label="Settings categories">
                                    <span class="settings-chip">Session</span>
                                    <span class="settings-chip">Provider</span>
                                    <span class="settings-chip">Workspace</span>
                                    <span class="settings-chip">Extension</span>
                                </div>
                            </div>
                            <div class="settings-hero-actions">
                                <button id="settings-close-button" class="ghost-button" title="Back to chat" aria-label="Back to chat">Chat</button>
                            </div>
                        </div>
                        <div class="settings-groups">
                            <section class="settings-group">
                                <div class="settings-group-header">
                                    <div>
                                        <h2>Session</h2>
                                        <p class="settings-group-hint">Model, profile, reasoning, and saved chats.</p>
                                    </div>
                                    <span class="settings-group-count">5</span>
                                </div>
                                <button class="settings-action" data-icon="M" data-action="model">
                                    <span class="settings-action-main">
                                        <span class="settings-action-title">Model</span>
                                        <span class="settings-action-description">Choose active model for this session.</span>
                                    </span>
                                    <span id="settings-model-value" class="settings-action-value">-</span>
                                </button>
                                <button class="settings-action" data-icon="P" data-command="/setting profile">
                                    <span class="settings-action-main">
                                        <span class="settings-action-title">Profile</span>
                                        <span class="settings-action-description">Switch build, plan, review, or subagent profile.</span>
                                    </span>
                                    <span id="settings-profile-value" class="settings-action-value">-</span>
                                </button>
                                <button class="settings-action" data-icon="T" data-command="/setting thinking">
                                    <span class="settings-action-main">
                                        <span class="settings-action-title">Thinking</span>
                                        <span class="settings-action-description">Set provider reasoning effort for later prompts.</span>
                                    </span>
                                    <span id="settings-thinking-value" class="settings-action-value">-</span>
                                </button>
                                <button class="settings-action" data-icon="I" data-command="/setting summary">
                                    <span class="settings-action-main">
                                        <span class="settings-action-title">Summary</span>
                                        <span class="settings-action-description">Show provider, model, profile, thinking, and session details.</span>
                                    </span>
                                    <span class="settings-action-value">open</span>
                                </button>
                                <button class="settings-action" data-icon="S" data-action="sessions">
                                    <span class="settings-action-main">
                                        <span class="settings-action-title">Sessions</span>
                                        <span class="settings-action-description">Browse, resume, fork, or export saved sessions.</span>
                                    </span>
                                    <span class="settings-action-value">browse</span>
                                </button>
                            </section>
                            <section class="settings-group">
                                <div class="settings-group-header">
                                    <div>
                                        <h2>Provider</h2>
                                        <p class="settings-group-hint">Switch providers or repair credentials.</p>
                                    </div>
                                    <span class="settings-group-count">2</span>
                                </div>
                                <button class="settings-action" data-icon="P" data-command="/setting provider">
                                    <span class="settings-action-main">
                                        <span class="settings-action-title">Provider</span>
                                        <span class="settings-action-description">Switch saved provider for this session.</span>
                                    </span>
                                    <span id="settings-provider-value" class="settings-action-value">-</span>
                                </button>
                                <button class="settings-action" data-icon="K" data-command="/setting onboarding">
                                    <span class="settings-action-main">
                                        <span class="settings-action-title">Onboarding</span>
                                        <span class="settings-action-description">Add or repair provider credentials.</span>
                                    </span>
                                    <span class="settings-action-value">setup</span>
                                </button>
                            </section>
                            <section class="settings-group wide">
                                <div class="settings-group-header">
                                    <div>
                                        <h2>Workspace</h2>
                                        <p class="settings-group-hint">Project files, permissions, budget, tools, terminals, and plugins.</p>
                                    </div>
                                    <span class="settings-group-count">7</span>
                                </div>
                                <button class="settings-action" data-icon="W" data-command="/setting workspace">
                                    <span class="settings-action-main">
                                        <span class="settings-action-title">Workspace Files</span>
                                        <span class="settings-action-description">Create or review .nanoagent project files.</span>
                                    </span>
                                    <span class="settings-action-value">init</span>
                                </button>
                                <button class="settings-action" data-icon="$" data-command="/setting budget">
                                    <span class="settings-action-main">
                                        <span class="settings-action-title">Budget</span>
                                        <span class="settings-action-description">Configure local or cloud budget controls.</span>
                                    </span>
                                    <span class="settings-action-value">open</span>
                                </button>
                                <button class="settings-action" data-icon="L" data-command="/setting permissions">
                                    <span class="settings-action-main">
                                        <span class="settings-action-title">Permissions</span>
                                        <span class="settings-action-description">Edit modes, sandbox, and session overrides.</span>
                                    </span>
                                    <span class="settings-action-value">policy</span>
                                </button>
                                <button class="settings-action" data-icon="R" data-command="/setting rules">
                                    <span class="settings-action-main">
                                        <span class="settings-action-title">Rules</span>
                                        <span class="settings-action-description">Inspect effective tool permission rules.</span>
                                    </span>
                                    <span class="settings-action-value">view</span>
                                </button>
                                <button class="settings-action" data-icon="T" data-command="/setting tools">
                                    <span class="settings-action-main">
                                        <span class="settings-action-title">Tools</span>
                                        <span class="settings-action-description">Show MCP servers, custom tools, and dynamic tool status.</span>
                                    </span>
                                    <span class="settings-action-value">view</span>
                                </button>
                                <button class="settings-action" data-icon=">_" data-command="/terminals">
                                    <span class="settings-action-main">
                                        <span class="settings-action-title">Background Terminals</span>
                                        <span class="settings-action-description">List running terminals and stop them with /terminals stop.</span>
                                    </span>
                                    <span class="settings-action-value">view</span>
                                </button>
                                <button class="settings-action" data-icon="+" data-action="plugins">
                                    <span class="settings-action-main">
                                        <span class="settings-action-title">Plugins</span>
                                        <span class="settings-action-description">Browse marketplaces and install or remove plugins.</span>
                                    </span>
                                    <span class="settings-action-value">manage</span>
                                </button>
                            </section>
                            <section class="settings-group">
                                <div class="settings-group-header">
                                    <div>
                                        <h2>Extension</h2>
                                        <p class="settings-group-hint">Open the native VS Code extension settings.</p>
                                    </div>
                                    <span class="settings-group-count">1</span>
                                </div>
                                <button class="settings-action" data-icon="VS" data-action="vscodeSettings">
                                    <span class="settings-action-main">
                                        <span class="settings-action-title">VS Code Extension Settings</span>
                                        <span class="settings-action-description">Edit command, args, working directory, auto-start, and log level.</span>
                                    </span>
                                    <span class="settings-action-value">vscode</span>
                                </button>
                            </section>
                        </div>
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
                    <section class="section section-scroll">
                        <div class="section-header">
                            <h2>Changed files</h2>
                            <span id="changed-files-count" class="section-count">0 files</span>
                        </div>
                        <div id="changed-files-list" class="changed-files-list"></div>
                    </section>
                </aside>
                <div class="composer">
                    <div id="prompt-queue" class="prompt-queue hidden"></div>
                    <div id="suggestions" class="suggestions hidden"></div>
                    <div class="composer-row">
                        <textarea id="chat-input" class="chat-input" rows="1" placeholder="Message NanoAgent, type / for commands, or @ for files"></textarea>
                        <div class="composer-toolbar">
                            <div class="toolbar-left">
                                <button id="add-context-button" class="icon-button" title="Read workspace file" aria-label="Read workspace file">+</button>
                                <select id="profile-select" class="profile-select" title="Profile" aria-label="Profile"></select>
                                <button id="model-button" class="model-pill" title="Choose model" aria-label="Choose model"><span id="model-button-label">Model</span></button>
                                <div class="status-pill" title="Process status">
                                    <span id="status-text">Stopped</span>
                                </div>
                                <span id="context-meter" class="context-meter" title="Context usage" hidden></span>
                            </div>
                            <div class="toolbar-right">
                                <button id="stop-button" class="danger-button" disabled>Stop</button>
                                <button id="send-button" class="primary-button" title="Send message" aria-label="Send message">&#8593;</button>
                            </div>
                        </div>
                    </div>
                </div>
            </section>
        </main>
    </div>

    <div id="agent-menu" class="agent-menu hidden" role="menu"></div>

    <div id="modal-backdrop" class="modal-backdrop hidden">
        <div class="modal">
            <h2 id="modal-title"></h2>
            <p id="modal-description" class="modal-description"></p>
            <div id="modal-body"></div>
            <div id="modal-actions" class="modal-actions"></div>
        </div>
    </div>
    <script nonce="${nonce}" src="${scriptUri}"></script>
</body>
</html>`;
}


export function getNonce(): string {
    const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    let result = '';
    const array = new Uint8Array(32);
    crypto.getRandomValues(array);
    for (let i = 0; i < array.length; i++) {
        result += chars[array[i] % chars.length];
    }
    return result;
}
