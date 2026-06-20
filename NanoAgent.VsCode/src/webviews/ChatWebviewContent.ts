import { buildDiffModel } from './diffView';

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
    { command: '/plugin', usage: '/plugin [marketplace add|install|list|uninstall]', description: 'Manage plugin marketplaces and installs.', insertText: '/plugin ' },
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

export function getChatWebviewContent(nonce: string) {
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
            --link: var(--vscode-textLink-foreground, #3794ff);

            --surface-0: var(--app-bg);
            --surface-1: color-mix(in srgb, var(--panel-bg) 92%, var(--app-bg));
            --surface-2: color-mix(in srgb, var(--input-bg) 86%, var(--app-bg));
            --surface-3: color-mix(in srgb, var(--input-bg) 72%, var(--fg) 4%);
            --surface-hover: var(--vscode-list-hoverBackground, color-mix(in srgb, var(--fg) 7%, transparent));
            --surface-active: var(--vscode-list-activeSelectionBackground, color-mix(in srgb, var(--focus) 18%, transparent));
            --border-subtle: color-mix(in srgb, var(--border) 72%, transparent);
            --border-strong: color-mix(in srgb, var(--fg) 18%, var(--border));
            --muted-soft: color-mix(in srgb, var(--muted) 82%, transparent);

            --radius-xs: 5px;
            --radius-sm: 7px;
            --radius-md: 10px;
            --radius-lg: 14px;
            --radius-xl: 18px;
            --space-1: 4px;
            --space-2: 6px;
            --space-3: 8px;
            --space-4: 10px;
            --space-5: 12px;
            --space-6: 14px;
            --shadow-overlay: 0 18px 44px rgba(0, 0, 0, 0.38);
            --shadow-soft: 0 10px 28px rgba(0, 0, 0, 0.24);
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
            font-family: var(--vscode-font-family, "Segoe UI", system-ui, sans-serif);
            font-size: var(--vscode-font-size, 13px);
            line-height: 1.45;
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
        textarea:disabled,
        select:disabled {
            cursor: not-allowed;
            opacity: 0.52;
        }

        button:focus-visible,
        input:focus-visible,
        select:focus-visible,
        textarea:focus-visible,
        summary:focus-visible,
        .file-link:focus-visible,
        .agent-thread-item:focus-visible,
        .modal-option:focus-visible,
        .suggestion:focus-visible {
            outline: 1px solid var(--focus);
            outline-offset: 2px;
        }

        ::selection {
            color: var(--vscode-editor-selectionForeground, var(--fg));
            background: var(--vscode-editor-selectionBackground, color-mix(in srgb, var(--focus) 36%, transparent));
        }

        ::-webkit-scrollbar {
            width: 10px;
            height: 10px;
        }

        ::-webkit-scrollbar-track {
            background: transparent;
        }

        ::-webkit-scrollbar-thumb {
            border: 3px solid transparent;
            border-radius: 999px;
            background: color-mix(in srgb, var(--fg) 17%, transparent);
            background-clip: padding-box;
        }

        ::-webkit-scrollbar-thumb:hover {
            background: color-mix(in srgb, var(--fg) 30%, transparent);
            background-clip: padding-box;
        }

        .icon-button,
        .ghost-button,
        .danger-button,
        .primary-button,
        .model-pill,
        .profile-select,
        .modal-option,
        .settings-action,
        .suggestion,
        .agent-thread-item,
        .file-link,
        .composer-row {
            transition:
                background-color 0.14s ease,
                border-color 0.14s ease,
                color 0.14s ease,
                opacity 0.14s ease,
                transform 0.14s ease;
        }

        .workbench {
            display: grid;
            grid-template-rows: minmax(0, 1fr);
            width: 100%;
            height: 100vh;
            min-width: 0;
            background:
                radial-gradient(circle at top right, color-mix(in srgb, var(--focus) 7%, transparent), transparent 34%),
                var(--app-bg);
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
            grid-template-rows: auto minmax(0, 1fr) auto auto;
            min-width: 0;
            min-height: 0;
        }

        .chat-header {
            grid-row: 1;
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: var(--space-3);
            min-width: 0;
            min-height: 42px;
            padding: 8px 14px 7px;
            border-bottom: 1px solid var(--border-subtle);
            background: color-mix(in srgb, var(--app-bg) 94%, var(--panel-bg));
        }

        .chat-header-copy {
            display: grid;
            gap: 1px;
            min-width: 0;
        }

        .chat-kicker {
            color: var(--muted);
            font-size: 10px;
            font-weight: 700;
            letter-spacing: 0.45px;
            line-height: 1.1;
            text-transform: uppercase;
        }

        .chat-section-title {
            max-width: min(460px, 56vw);
            color: var(--fg);
            font-size: 12px;
            font-weight: 650;
            line-height: 1.25;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .chat-header-actions {
            display: inline-flex;
            align-items: center;
            justify-content: flex-end;
            gap: var(--space-2);
            flex: 0 0 auto;
        }

        .top-action {
            border-color: var(--border-subtle);
            background: color-mix(in srgb, var(--surface-2) 62%, transparent);
        }

        .top-action:hover,
        .top-action.active {
            border-color: color-mix(in srgb, var(--focus) 45%, var(--border-subtle));
            background: var(--surface-hover);
        }

        .top-action.active {
            color: var(--focus);
        }

        .messages,
        .settings-page {
            grid-row: 2;
        }

        .messages.hidden,
        .settings-page.hidden,
        .suggestions.hidden,
        .modal-backdrop.hidden,
        .agent-menu.hidden {
            display: none;
        }

        .messages {
            display: flex;
            flex-direction: column;
            gap: 14px;
            min-height: 0;
            padding: 18px 14px 10px;
            overflow-y: auto;
            scroll-behavior: smooth;
        }

        .message-card {
            display: grid;
            gap: var(--space-1);
            max-width: min(860px, 94%);
            white-space: pre-wrap;
            word-break: break-word;
            overflow-wrap: anywhere;
        }

        .message-card.user {
            align-self: flex-end;
            max-width: min(680px, 92%);
            padding: 0;
            border: 0;
            border-radius: 0;
            color: var(--input-fg);
            background: transparent;
        }

        .message-card.assistant {
            align-self: flex-start;
            max-width: min(860px, 94%);
            padding: 0;
        }

        .message-card.reasoning,
        .message-card.tool,
        .message-card.metrics,
        .message-card.system {
            align-self: flex-start;
            max-width: min(780px, 92%);
        }

        .message-card.reasoning {
            color: var(--muted);
        }

        .message-card.tool {
            padding: 0;
            border: 0;
            border-radius: 0;
            color: var(--muted);
            background: transparent;
        }

        .message-card.tool.completed,
        .message-card.tool.failed {
            border: 0;
        }

        .message-card.metrics {
            color: var(--muted-soft);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 11px;
            opacity: 0.9;
        }

        .message-card.system {
            align-self: center;
            max-width: 96%;
            padding: 0;
            border: 0;
            border-radius: 0;
            color: color-mix(in srgb, var(--warning) 92%, var(--fg));
            background: transparent;
        }

        .message-label {
            display: none;
        }

        .message-text {
            color: inherit;
            font-size: 13px;
            line-height: 1.52;
        }

        .assistant .message-text {
            color: var(--fg);
        }

        .markdown-rendered {
            color: inherit;
            white-space: normal;
            word-break: break-word;
            overflow-wrap: anywhere;
        }

        .markdown-rendered :first-child {
            margin-top: 0;
        }

        .markdown-rendered :last-child {
            margin-bottom: 0;
        }

        .markdown-rendered p {
            margin: 0 0 0.58em;
        }

        .markdown-rendered h1,
        .markdown-rendered h2,
        .markdown-rendered h3,
        .markdown-rendered h4,
        .markdown-rendered h5,
        .markdown-rendered h6 {
            margin: 0.72em 0 0.34em;
            color: inherit;
            font-size: inherit;
            line-height: inherit;
            font-weight: 700;
        }

        .markdown-rendered ul,
        .markdown-rendered ol {
            margin: 0.34em 0 0.64em 1.35em;
            padding: 0;
        }

        .markdown-rendered li {
            margin: 0.16em 0;
            padding: 0;
        }

        .markdown-rendered blockquote {
            margin: 0.45em 0 0.65em;
            padding: 0 0 0 0.9em;
            border: 0;
            color: inherit;
        }

        .markdown-rendered code,
        .markdown-rendered pre {
            border: 0;
            border-radius: 0;
            color: inherit;
            background: transparent;
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
        }

        .markdown-rendered code {
            padding: 0;
            font-size: 0.96em;
        }

        .markdown-rendered pre {
            max-height: none;
            margin: 0.45em 0 0.7em;
            padding: 0;
            overflow: auto;
            font-size: 12px;
            line-height: 1.48;
            white-space: pre;
            overflow-wrap: normal;
        }

        .markdown-rendered a {
            color: var(--link);
            text-decoration: none;
            text-underline-offset: 2px;
        }

        .markdown-rendered a:hover {
            color: var(--vscode-textLink-activeForeground, #4daafc);
            text-decoration: underline;
        }


        .thinking-details,
        .tool-call-details {
            min-width: 0;
            overflow: visible;
            border: 0;
            border-radius: 0;
            background: transparent;
        }

        .thinking-details,
        .tool-call-details,
        .message-card.tool.completed .tool-call-details,
        .message-card.tool.failed .tool-call-details {
            border-left: 0;
        }

        .thinking-details > summary,
        .tool-call-details > summary {
            list-style: none;
        }

        .thinking-details > summary::-webkit-details-marker,
        .tool-call-details > summary::-webkit-details-marker {
            display: none;
        }

        .inline-call-summary {
            display: flex;
            align-items: center;
            gap: var(--space-3);
            min-width: 0;
            padding: 0;
            color: var(--muted);
            cursor: pointer;
            user-select: none;
        }

        .inline-call-summary:hover .inline-call-title,
        .inline-call-summary:hover .inline-call-icon,
        .inline-call-summary:hover .inline-call-chevron {
            color: var(--fg);
        }

        .inline-call-icon {
            flex: 0 0 auto;
            width: 16px;
            color: var(--muted-soft);
            font-size: 12px;
            line-height: 1;
            text-align: center;
        }

        .inline-call-icon:empty {
            display: none;
        }

        .inline-call-title {
            min-width: 0;
            color: var(--muted);
            font-size: 12px;
            font-weight: 650;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .inline-call-meta {
            flex: 0 0 auto;
            color: var(--muted);
            font-size: 10px;
            letter-spacing: 0.35px;
            text-transform: uppercase;
        }

        .inline-call-chevron {
            flex: 0 0 auto;
            margin-left: auto;
            color: var(--muted-soft);
            font-size: 12px;
            transition: transform 0.14s ease;
        }

        .thinking-details[open] .inline-call-chevron,
        .tool-call-details[open] .inline-call-chevron {
            transform: rotate(180deg);
        }

        .inline-call-content {
            display: grid;
            gap: var(--space-3);
            padding: 6px 0 0 24px;
        }

        .tool-call-details > .inline-call-content {
            padding-left: 0;
        }

        .thinking-details pre,
        .tool-output-pre {
            max-height: 260px;
            margin: 0;
            padding: 0;
            overflow: auto;
            border: 0;
            border-radius: 0;
            color: var(--fg);
            background: transparent;
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 12px;
            line-height: 1.48;
            white-space: pre-wrap;
            overflow-wrap: anywhere;
        }

        .thinking-details pre {
            color: var(--muted);
        }

        .tool-output-pre {
            white-space: pre;
            overflow-wrap: normal;
        }

        .vscode-code-block {
            display: grid;
            max-width: 100%;
            overflow: hidden;
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-md);
            background: color-mix(in srgb, var(--surface-1) 96%, #000000 4%);
        }

        .vscode-code-header {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: var(--space-3);
            min-width: 0;
            padding: 6px 9px;
            border-bottom: 1px solid var(--border-subtle);
            background: color-mix(in srgb, var(--surface-2) 88%, transparent);
            font-size: 11px;
        }

        .vscode-code-header-main {
            display: inline-flex;
            align-items: center;
            gap: var(--space-2);
            min-width: 0;
        }

        .vscode-code-header-main .file-link,
        .vscode-code-title {
            min-width: 0;
            overflow: hidden;
            color: var(--muted);
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .vscode-code-actions {
            display: inline-flex;
            align-items: center;
            gap: var(--space-2);
            flex: 0 0 auto;
        }

        .vscode-code-language,
        .vscode-code-line-count {
            color: var(--muted);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 10px;
            letter-spacing: 0.25px;
            text-transform: uppercase;
        }

        .vscode-code-copy-button {
            display: inline-grid;
            place-items: center;
            width: 20px;
            height: 20px;
            padding: 0;
            border: 0;
            border-radius: var(--radius-xs);
            color: var(--muted);
            background: transparent;
            font-size: 13px;
            line-height: 1;
        }

        .vscode-code-copy-button:hover {
            color: var(--fg);
            background: var(--surface-hover);
        }

        .vscode-code-body {
            max-height: 360px;
            margin: 0;
            padding: 0;
            overflow: auto;
            border: 0;
            border-radius: 0;
            color: var(--vscode-editor-foreground, var(--fg));
            background: var(--vscode-editor-background, color-mix(in srgb, var(--app-bg) 91%, #000000 9%));
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 12px;
            line-height: 1.48;
            white-space: pre;
        }

        .vscode-code-line {
            display: grid;
            grid-template-columns: minmax(42px, max-content) minmax(0, 1fr);
            min-width: max-content;
        }

        .vscode-code-line.no-line-number {
            grid-template-columns: minmax(0, 1fr);
            min-width: 0;
        }

        .vscode-code-block.tool-output .vscode-code-body {
            max-height: 320px;
            white-space: pre-wrap;
        }

        .vscode-code-block.read-file-output .vscode-code-body {
            max-height: 520px;
        }

        .vscode-code-block.tool-output .vscode-code-line-content,
        .vscode-code-line.no-line-number .vscode-code-line-content {
            padding: 1px 12px;
            white-space: pre-wrap;
            overflow-wrap: anywhere;
        }

        .vscode-code-line:hover {
            background: color-mix(in srgb, var(--vscode-editorLineHighlightBackground, var(--fg)) 8%, transparent);
        }

        .vscode-code-line-number {
            padding: 1px 9px;
            color: var(--vscode-editorLineNumber-foreground, var(--muted-soft));
            text-align: right;
            user-select: none;
        }

        .vscode-code-line-content {
            min-width: 0;
            padding: 1px 12px 1px 0;
            white-space: pre;
        }

        .syntax-comment {
            color: var(--vscode-editorLineNumber-foreground, var(--muted));
            font-style: italic;
        }

        .syntax-keyword {
            color: var(--vscode-symbolIcon-keywordForeground, #c586c0);
        }

        .syntax-string {
            color: var(--vscode-symbolIcon-stringForeground, #ce9178);
        }

        .syntax-number,
        .syntax-constant {
            color: var(--vscode-symbolIcon-numberForeground, #b5cea8);
        }

        .syntax-function {
            color: var(--vscode-symbolIcon-functionForeground, #dcdcaa);
        }

        .syntax-property {
            color: var(--vscode-symbolIcon-propertyForeground, #9cdcfe);
        }

        .syntax-muted {
            color: var(--muted);
        }

        .tool-message {
            display: grid;
            gap: var(--space-3);
            min-width: 0;
        }

        .tool-message-kind,
        .tool-pending {
            color: var(--muted);
            font-size: 11px;
        }

        .tool-message-kind {
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .tool-arguments summary {
            color: var(--muted);
            font-size: 11px;
            cursor: pointer;
        }

        .diff-view {
            display: grid;
            gap: 0;
            max-width: 100%;
            overflow: hidden;
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-md);
            background: color-mix(in srgb, var(--surface-1) 96%, #000000 4%);
        }

        .diff-view + .diff-view {
            margin-top: var(--space-2);
        }

        .diff-header {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: var(--space-3);
            min-width: 0;
            padding: 6px 9px;
            border-bottom: 1px solid var(--border-subtle);
            background: color-mix(in srgb, var(--surface-2) 88%, transparent);
            font-size: 11px;
        }

        .diff-header-main {
            display: inline-flex;
            align-items: center;
            gap: var(--space-2);
            min-width: 0;
        }

        .diff-header-main .file-link {
            min-width: 0;
            overflow: hidden;
            color: var(--muted);
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .diff-header-actions {
            display: inline-flex;
            align-items: center;
            gap: var(--space-2);
            flex: 0 0 auto;
        }

        .diff-stat {
            flex: 0 0 auto;
            color: var(--muted);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
        }

        .diff-copy-button {
            display: inline-grid;
            place-items: center;
            width: 20px;
            height: 20px;
            padding: 0;
            border: 0;
            border-radius: var(--radius-xs);
            color: var(--muted);
            background: transparent;
            font-size: 13px;
            line-height: 1;
        }

        .diff-copy-button:hover {
            color: var(--fg);
            background: var(--surface-hover);
        }

        .diff-body {
            max-height: 320px;
            margin: 0;
            padding: 0;
            overflow: auto;
            border: 0;
            border-radius: 0;
            background: color-mix(in srgb, var(--app-bg) 91%, #000000 9%);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 12px;
            line-height: 1.45;
            white-space: pre;
        }

        .diff-created .diff-body,
        .diff-add-only .diff-body {
            border-left: 4px solid color-mix(in srgb, var(--ok) 88%, transparent);
        }

        .diff-deleted .diff-body,
        .diff-del-only .diff-body {
            border-left: 4px solid color-mix(in srgb, var(--danger) 88%, transparent);
        }

        .diff-line {
            display: grid;
            grid-template-columns: minmax(42px, max-content) 18px minmax(0, 1fr);
            min-width: max-content;
            white-space: pre;
        }

        .diff-line-number {
            padding: 1px 8px 1px 9px;
            color: var(--muted-soft);
            text-align: right;
            user-select: none;
        }

        .diff-line-sign {
            padding: 1px 4px 1px 2px;
            color: var(--muted-soft);
            user-select: none;
        }

        .diff-line-code {
            min-width: 0;
            padding: 1px 10px 1px 0;
            color: inherit;
            white-space: pre-wrap;
            overflow-wrap: anywhere;
        }

        .diff-add {
            color: color-mix(in srgb, var(--ok) 84%, var(--fg));
            background: color-mix(in srgb, var(--ok) 12%, transparent);
        }

        .diff-del {
            color: color-mix(in srgb, var(--danger) 86%, var(--fg));
            background: color-mix(in srgb, var(--danger) 13%, transparent);
        }

        .diff-meta {
            color: var(--muted);
            background: color-mix(in srgb, var(--focus) 10%, transparent);
        }

        .diff-add .diff-line-sign,
        .diff-add .diff-line-number {
            color: color-mix(in srgb, var(--ok) 82%, var(--muted));
        }

        .diff-del .diff-line-sign,
        .diff-del .diff-line-number {
            color: color-mix(in srgb, var(--danger) 82%, var(--muted));
        }

        .tool-pending {
            color: var(--muted);
            font-size: 12px;
        }

        .file-link {
            color: var(--link);
            text-decoration: none;
            text-underline-offset: 2px;
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
            padding: 0;
            border: 0;
            border-radius: 0;
            color: var(--muted);
            background: transparent;
            box-shadow: none;
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
                opacity: 0.22;
            }

            40% {
                opacity: 1;
            }
        }

        .empty-state {
            display: flex;
            flex-direction: column;
            gap: var(--space-3);
            align-items: center;
            margin: auto;
            max-width: 430px;
            padding: 22px 20px;
            border: 0;
            border-radius: 0;
            color: var(--muted);
            text-align: center;
            line-height: 1.5;
            background: transparent;
        }

        .empty-title {
            color: var(--fg);
            font-size: 19px;
            font-weight: 650;
            letter-spacing: 0.1px;
        }

        .empty-hint {
            max-width: 320px;
            font-size: 13px;
        }

        .empty-keys {
            display: flex;
            flex-wrap: wrap;
            align-items: center;
            justify-content: center;
            gap: var(--space-2);
            margin-top: 4px;
            font-size: 11px;
        }

        .empty-dot {
            opacity: 0.45;
        }

        .empty-keys kbd {
            min-width: 18px;
            padding: 2px 6px;
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-xs);
            color: var(--fg);
            background: color-mix(in srgb, var(--surface-2) 86%, transparent);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 10px;
            font-weight: 600;
        }

        .side-pane {
            grid-row: 3;
            display: none;
            grid-template-columns: repeat(2, minmax(0, 1fr));
            gap: var(--space-3);
            min-width: 0;
            max-height: 176px;
            padding: 0 14px 8px;
            overflow: hidden;
            background: transparent;
        }

        .side-pane.visible {
            display: grid;
        }

        .section {
            min-width: 0;
            min-height: 0;
            padding: 9px;
            overflow: hidden;
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-md);
            background: color-mix(in srgb, var(--surface-1) 90%, transparent);
        }

        .section-scroll {
            overflow-y: auto;
        }

        .section-header {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: var(--space-3);
            margin-bottom: 8px;
        }

        .section-header h2 {
            margin: 0;
            color: var(--fg);
            font-size: 10px;
            line-height: 1.2;
            font-weight: 700;
            letter-spacing: 0.45px;
            text-transform: uppercase;
        }

        .section-count {
            color: var(--muted);
            font-size: 10px;
            white-space: nowrap;
        }

        .context-grid {
            display: grid;
            grid-template-columns: repeat(2, minmax(0, 1fr));
            gap: var(--space-2);
        }

        .context-item {
            min-width: 0;
            padding: 7px;
            border: 1px solid transparent;
            border-radius: var(--radius-sm);
            background: color-mix(in srgb, var(--surface-2) 78%, transparent);
        }

        .context-item.wide {
            grid-column: 1 / -1;
        }

        .context-label {
            color: var(--muted);
            font-size: 9px;
            letter-spacing: 0.35px;
            text-transform: uppercase;
        }

        .context-value {
            margin-top: 3px;
            color: var(--fg);
            font-size: 11px;
            line-height: 1.35;
            overflow-wrap: anywhere;
        }

        .plan-list,
        .tool-list,
        .agent-thread-list {
            display: grid;
            gap: var(--space-2);
        }

        .plan-item {
            display: grid;
            grid-template-columns: auto 1fr;
            gap: var(--space-3);
            align-items: start;
            padding: 7px;
            border: 1px solid transparent;
            border-radius: var(--radius-sm);
            background: color-mix(in srgb, var(--surface-2) 78%, transparent);
        }

        .plan-dot {
            width: 7px;
            height: 7px;
            margin-top: 5px;
            border-radius: 50%;
            background: var(--muted);
        }

        .plan-item.completed .plan-dot {
            background: var(--ok);
        }

        .plan-item.in_progress .plan-dot {
            background: var(--warning);
            box-shadow: 0 0 0 3px color-mix(in srgb, var(--warning) 12%, transparent);
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
            font-size: 9px;
            letter-spacing: 0.35px;
            text-transform: uppercase;
        }

        .agent-thread-item {
            width: 100%;
            display: grid;
            gap: var(--space-1);
            padding: 7px;
            border: 1px solid transparent;
            border-radius: var(--radius-sm);
            color: inherit;
            text-align: left;
            background: color-mix(in srgb, var(--surface-2) 78%, transparent);
            cursor: pointer;
        }

        .agent-thread-item:hover,
        .agent-thread-item.active {
            border-color: color-mix(in srgb, var(--focus) 44%, var(--border-subtle));
            background: var(--surface-hover);
        }

        .agent-thread-item.active {
            box-shadow: inset 2px 0 0 var(--focus);
        }

        .agent-thread-topline {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: var(--space-3);
        }

        .agent-thread-name {
            color: var(--fg);
            font-size: 12px;
            font-weight: 650;
        }

        .agent-thread-status {
            color: var(--muted);
            font-size: 9px;
            letter-spacing: 0.35px;
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
            padding-top: 5px;
            color: var(--fg);
            border-top: 1px solid var(--border-subtle);
            white-space: pre-wrap;
        }

        .tool-card {
            display: grid;
            gap: var(--space-2);
            padding: 7px;
            border: 1px solid transparent;
            border-left: 2px solid var(--warning);
            border-radius: var(--radius-sm);
            background: color-mix(in srgb, var(--surface-2) 78%, transparent);
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
            gap: var(--space-3);
        }

        .tool-title {
            min-width: 0;
            color: var(--fg);
            font-size: 12px;
            font-weight: 650;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .tool-status {
            flex: 0 0 auto;
            color: var(--warning);
            font-size: 9px;
            font-weight: 700;
            letter-spacing: 0.35px;
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
            padding: 8px;
            overflow: auto;
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-sm);
            color: var(--fg);
            background: color-mix(in srgb, var(--app-bg) 92%, #000000 8%);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 11px;
            line-height: 1.5;
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
            padding: 16px;
            overflow-y: auto;
            background:
                radial-gradient(circle at top left, color-mix(in srgb, var(--focus) 10%, transparent), transparent 38%),
                transparent;
        }

        .settings-shell {
            display: grid;
            gap: 14px;
            width: min(980px, 100%);
            margin: 0 auto;
        }

        .settings-hero {
            display: grid;
            grid-template-columns: minmax(0, 1fr) auto;
            gap: var(--space-5);
            align-items: start;
            padding: 16px;
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-xl);
            background:
                linear-gradient(135deg, color-mix(in srgb, var(--surface-2) 88%, transparent), color-mix(in srgb, var(--surface-1) 94%, transparent)),
                var(--surface-1);
            box-shadow: 0 12px 30px rgba(0, 0, 0, 0.16);
        }

        .settings-header {
            display: grid;
            gap: var(--space-3);
            min-width: 0;
        }

        .settings-kicker {
            width: max-content;
            max-width: 100%;
            padding: 3px 8px;
            border: 1px solid color-mix(in srgb, var(--focus) 28%, var(--border-subtle));
            border-radius: 999px;
            color: color-mix(in srgb, var(--focus) 70%, var(--fg));
            background: color-mix(in srgb, var(--focus) 10%, transparent);
            font-size: 10px;
            font-weight: 700;
            letter-spacing: 0.45px;
            text-transform: uppercase;
        }

        .settings-title {
            min-width: 0;
        }

        .settings-title h1 {
            margin: 0;
            color: var(--fg);
            font-size: 20px;
            line-height: 1.15;
            font-weight: 700;
            letter-spacing: -0.25px;
        }

        .settings-summary {
            max-width: 720px;
            margin-top: 6px;
            color: var(--muted);
            font-size: 12px;
            line-height: 1.45;
            overflow-wrap: anywhere;
        }

        .settings-quick {
            display: flex;
            flex-wrap: wrap;
            gap: var(--space-2);
            align-items: center;
        }

        .settings-chip {
            display: inline-flex;
            align-items: center;
            min-height: 22px;
            padding: 2px 8px;
            border: 1px solid var(--border-subtle);
            border-radius: 999px;
            color: var(--muted);
            background: color-mix(in srgb, var(--surface-2) 72%, transparent);
            font-size: 11px;
            line-height: 1;
            white-space: nowrap;
        }

        .settings-hero-actions {
            display: flex;
            align-items: center;
            justify-content: flex-end;
            gap: var(--space-2);
        }

        .settings-groups {
            display: grid;
            grid-template-columns: repeat(2, minmax(260px, 1fr));
            gap: 12px;
        }

        .settings-group {
            display: grid;
            align-content: start;
            gap: var(--space-2);
            min-width: 0;
            padding: 12px;
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-lg);
            background: color-mix(in srgb, var(--surface-1) 88%, transparent);
        }

        .settings-group.wide {
            grid-column: 1 / -1;
        }

        .settings-group-header {
            display: flex;
            align-items: flex-start;
            justify-content: space-between;
            gap: var(--space-4);
            margin-bottom: 2px;
        }

        .settings-group h2 {
            margin: 0;
            color: var(--fg);
            font-size: 12px;
            font-weight: 700;
            letter-spacing: 0.2px;
        }

        .settings-group-hint {
            margin: 3px 0 0;
            color: var(--muted);
            font-size: 11px;
            line-height: 1.35;
        }

        .settings-group-count {
            flex: 0 0 auto;
            min-width: 22px;
            padding: 2px 7px;
            border-radius: 999px;
            color: var(--muted);
            background: color-mix(in srgb, var(--surface-2) 76%, transparent);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 10px;
            text-align: center;
        }

        .settings-action {
            position: relative;
            display: grid;
            grid-template-columns: 30px minmax(0, 1fr) auto 12px;
            gap: var(--space-3);
            align-items: center;
            width: 100%;
            min-height: 46px;
            padding: 8px 9px;
            border: 1px solid transparent;
            border-radius: var(--radius-md);
            color: var(--fg);
            text-align: left;
            background: color-mix(in srgb, var(--surface-2) 56%, transparent);
        }

        .settings-action::before {
            content: attr(data-icon);
            display: inline-grid;
            place-items: center;
            width: 28px;
            height: 28px;
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-sm);
            color: var(--fg);
            background: color-mix(in srgb, var(--surface-3) 78%, transparent);
            font-size: 11px;
            font-weight: 700;
            line-height: 1;
        }

        .settings-action::after {
            content: '›';
            color: var(--muted-soft);
            font-size: 18px;
            line-height: 1;
        }

        .settings-action:hover,
        .settings-action:focus-visible {
            border-color: color-mix(in srgb, var(--focus) 42%, var(--border-subtle));
            background: var(--surface-hover);
            transform: translateY(-1px);
        }

        .settings-action:hover::before,
        .settings-action:focus-visible::before {
            border-color: color-mix(in srgb, var(--focus) 34%, var(--border-subtle));
            color: color-mix(in srgb, var(--focus) 72%, var(--fg));
            background: color-mix(in srgb, var(--focus) 12%, var(--surface-3));
        }

        .settings-action-main {
            display: grid;
            gap: 2px;
            min-width: 0;
        }

        .settings-action-title {
            color: var(--fg);
            font-size: 13px;
            font-weight: 650;
            overflow-wrap: anywhere;
        }

        .settings-action-description {
            color: var(--muted);
            font-size: 11px;
            line-height: 1.35;
            overflow-wrap: anywhere;
        }

        .settings-action-value {
            max-width: min(220px, 30vw);
            padding: 3px 8px;
            border: 1px solid color-mix(in srgb, var(--border-subtle) 84%, transparent);
            border-radius: 999px;
            color: var(--muted);
            background: color-mix(in srgb, var(--app-bg) 34%, transparent);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 10px;
            overflow: hidden;
            text-align: right;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .composer {
            grid-row: 4;
            position: relative;
            display: grid;
            gap: var(--space-3);
            padding: 8px 14px 14px;
            background: linear-gradient(to top, var(--app-bg) 70%, transparent);
        }

        .suggestions {
            position: absolute;
            left: 14px;
            right: 14px;
            bottom: calc(100% - 8px);
            z-index: 4;
            max-height: min(280px, 45vh);
            overflow-y: auto;
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-lg);
            background: color-mix(in srgb, var(--surface-1) 98%, #000000 2%);
            box-shadow: var(--shadow-overlay);
        }

        .suggestion {
            display: grid;
            gap: 2px;
            padding: 9px 11px;
            border-bottom: 1px solid var(--border-subtle);
            cursor: pointer;
        }

        .suggestion:last-child {
            border-bottom: 0;
        }

        .suggestion.active,
        .suggestion:hover {
            background: var(--surface-hover);
        }

        .suggestion.active {
            box-shadow: inset 2px 0 0 var(--focus);
        }

        .suggestion-usage {
            color: var(--fg);
            font-size: 12px;
            font-weight: 650;
            overflow-wrap: anywhere;
        }

        .suggestion-description {
            color: var(--muted);
            font-size: 11px;
            line-height: 1.35;
        }

        .composer-row {
            display: grid;
            grid-template-rows: auto auto;
            gap: 7px;
            padding: 8px;
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-xl);
            background: color-mix(in srgb, var(--input-bg) 92%, var(--app-bg));
            box-shadow: 0 0 0 1px color-mix(in srgb, var(--fg) 2%, transparent);
        }

        .composer-row:focus-within {
            border-color: color-mix(in srgb, var(--focus) 70%, var(--border));
            background: color-mix(in srgb, var(--input-bg) 96%, var(--app-bg));
            box-shadow: 0 0 0 3px color-mix(in srgb, var(--focus) 12%, transparent);
        }

        .chat-input {
            width: 100%;
            min-height: 34px;
            max-height: 110px;
            padding: 6px 8px 2px;
            resize: none;
            border: 0;
            outline: none;
            color: var(--input-fg);
            background: transparent;
            line-height: 1.45;
            overflow-y: auto;
        }

        .chat-input::placeholder {
            color: color-mix(in srgb, var(--vscode-input-placeholderForeground, var(--muted)) 88%, transparent);
        }

        .composer-toolbar {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: var(--space-2);
            min-width: 0;
        }

        .toolbar-left,
        .toolbar-right,
        .status-cluster {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            min-width: 0;
        }

        .toolbar-left {
            flex: 0 1 auto;
            flex-wrap: nowrap;
            overflow: hidden;
        }

        .toolbar-right {
            flex: 0 0 auto;
            justify-content: flex-end;
        }

        .status-pill,
        .model-pill,
        .profile-select,
        .ghost-button,
        .danger-button,
        .icon-button {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            gap: var(--space-2);
            min-height: 27px;
            border: 1px solid transparent;
            border-radius: var(--radius-sm);
            padding: 4px 8px;
            color: var(--fg);
            background: transparent;
            white-space: nowrap;
        }

        .profile-select {
            min-width: 72px;
            max-width: 132px;
            color: color-mix(in srgb, var(--warning) 88%, var(--fg));
            background: color-mix(in srgb, var(--warning) 8%, transparent);
            font-weight: 650;
            outline: none;
            cursor: pointer;
        }

        .icon-button,
        .composer .primary-button {
            width: 29px;
            min-width: 29px;
            height: 29px;
            padding: 0;
            border-radius: 999px;
            font-size: 16px;
            line-height: 1;
        }

        .model-pill {
            max-width: 210px;
            min-width: 0;
            border-color: color-mix(in srgb, var(--border-subtle) 70%, transparent);
            background: color-mix(in srgb, var(--surface-2) 52%, transparent);
            cursor: pointer;
        }

        .model-pill span {
            overflow: hidden;
            text-overflow: ellipsis;
        }

        .status-pill {
            margin-left: 0;
            color: var(--muted);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 10px;
        }

        .context-meter {
            flex: 0 0 auto;
            padding: 3px 7px;
            border-radius: 999px;
            color: var(--muted);
            background: color-mix(in srgb, var(--surface-2) 70%, transparent);
            font-family: var(--vscode-editor-font-family, Consolas, monospace);
            font-size: 10px;
            white-space: nowrap;
        }

        .context-meter.context-meter-warn {
            color: var(--warning);
            background: color-mix(in srgb, var(--warning) 14%, transparent);
        }

        .ghost-button:hover,
        .model-pill:hover,
        .profile-select:hover,
        .icon-button:hover {
            border-color: color-mix(in srgb, var(--fg) 12%, transparent);
            background: var(--surface-hover);
        }

        .access-pill {
            color: var(--warning);
            font-weight: 650;
        }

        .danger-button {
            color: var(--danger);
        }

        .danger-button:hover {
            background: color-mix(in srgb, var(--danger) 14%, transparent);
        }

        .primary-button {
            min-height: 32px;
            border-radius: var(--radius-sm);
            padding: 0 12px;
            color: var(--button-fg);
            font-weight: 650;
            background: var(--button-bg);
        }

        .primary-button:not(:disabled):hover {
            background: var(--button-hover);
        }

        .composer .primary-button {
            color: var(--button-fg);
            background: var(--button-bg);
        }

        .composer .primary-button:not(:disabled):hover {
            transform: translateY(-1px);
            background: var(--button-hover);
        }

        .composer .primary-button.stop-mode {
            color: var(--vscode-button-secondaryForeground, var(--fg));
            background: color-mix(in srgb, var(--danger) 28%, var(--surface-2));
            border-color: color-mix(in srgb, var(--danger) 42%, transparent);
            font-size: 12px;
        }

        .composer .primary-button.stop-mode:not(:disabled):hover {
            background: color-mix(in srgb, var(--danger) 38%, var(--surface-2));
        }

        .toolbar-right #stop-button {
            display: none;
        }

        .modal-backdrop {
            position: fixed;
            inset: 0;
            z-index: 20;
            display: grid;
            place-items: center;
            padding: 18px;
            background: rgba(0, 0, 0, 0.54);
        }

        .modal {
            width: min(580px, 100%);
            max-height: min(720px, 92vh);
            display: grid;
            gap: 13px;
            padding: 15px;
            overflow: hidden;
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-lg);
            background: color-mix(in srgb, var(--surface-1) 98%, #000000 2%);
            box-shadow: var(--shadow-overlay);
        }

        .modal h2 {
            margin: 0;
            color: var(--fg);
            font-size: 16px;
            font-weight: 650;
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
            gap: var(--space-2);
            max-height: 390px;
            overflow-y: auto;
        }

        .modal-option {
            display: grid;
            gap: 3px;
            padding: 10px 11px;
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-md);
            color: var(--fg);
            text-align: left;
            background: color-mix(in srgb, var(--surface-2) 82%, transparent);
        }

        .modal-option:hover,
        .modal-option.active {
            border-color: color-mix(in srgb, var(--focus) 50%, var(--border-subtle));
            background: var(--surface-hover);
        }

        .modal-option.active {
            box-shadow: inset 2px 0 0 var(--focus);
        }

        .modal-option strong {
            font-size: 13px;
            overflow-wrap: anywhere;
        }

        .modal-option span {
            color: var(--muted);
            font-size: 10px;
            letter-spacing: 0.35px;
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
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-md);
            outline: none;
            color: var(--input-fg);
            background: var(--input-bg);
        }

        .modal-input:focus {
            border-color: var(--focus);
            box-shadow: 0 0 0 3px color-mix(in srgb, var(--focus) 12%, transparent);
        }

        .plugin-panel {
            display: grid;
            gap: 14px;
            max-height: 60vh;
            overflow-y: auto;
        }

        .plugin-section {
            display: grid;
            gap: var(--space-2);
        }

        .plugin-form {
            display: grid;
            grid-template-columns: minmax(0, 1fr) auto;
            gap: var(--space-2);
            align-items: center;
        }

        .plugin-row {
            display: flex;
            gap: var(--space-3);
            align-items: stretch;
        }

        .plugin-row .modal-option {
            flex: 1 1 auto;
        }

        .plugin-check {
            display: flex;
            gap: var(--space-2);
            align-items: center;
            color: var(--muted);
            font-size: 11px;
        }

        .modal-actions {
            display: flex;
            justify-content: flex-end;
            gap: var(--space-3);
        }

        .agent-menu {
            position: fixed;
            z-index: 30;
            display: grid;
            gap: 2px;
            min-width: 230px;
            max-width: min(340px, 90vw);
            padding: 7px;
            border: 1px solid var(--border-subtle);
            border-radius: var(--radius-lg);
            background: color-mix(in srgb, var(--surface-1) 98%, #000000 2%);
            box-shadow: var(--shadow-overlay);
        }

        .agent-menu-header {
            padding: 8px 10px 4px;
            color: var(--muted);
            font-size: 10px;
            font-weight: 700;
            letter-spacing: 0.45px;
            text-transform: uppercase;
        }

        .agent-menu-item {
            display: flex;
            align-items: center;
            gap: var(--space-3);
            width: 100%;
            padding: 7px 10px;
            border-radius: var(--radius-sm);
            color: var(--fg);
            text-align: left;
            background: transparent;
            font-size: 12px;
        }

        .agent-menu-item:hover {
            background: var(--surface-hover);
        }

        .agent-menu-check {
            flex: 0 0 auto;
            width: 14px;
            color: var(--focus);
            text-align: center;
        }

        .agent-menu-label {
            flex: 1 1 auto;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .agent-menu-sep {
            height: 1px;
            margin: 5px 6px;
            background: var(--border-subtle);
        }

        .agent-menu-models {
            max-height: 220px;
            overflow-y: auto;
        }

        @media (max-width: 760px) {
            .chat-header {
                min-height: 40px;
                padding: 7px 12px 6px;
            }

            .chat-section-title {
                max-width: 52vw;
            }

            .messages {
                padding: 12px;
            }

            .message-card,
            .message-card.assistant,
            .message-card.reasoning,
            .message-card.tool,
            .message-card.metrics {
                max-width: 100%;
            }

            .message-card.user {
                max-width: 92%;
            }

            .side-pane {
                grid-template-columns: 1fr;
                max-height: 230px;
                overflow-y: auto;
                padding: 0 12px 8px;
            }

            .settings-page {
                padding: 12px;
            }

            .settings-shell {
                gap: 12px;
            }

            .settings-hero {
                grid-template-columns: minmax(0, 1fr);
                padding: 14px;
            }

            .settings-hero-actions {
                justify-content: flex-start;
            }

            .settings-groups {
                grid-template-columns: minmax(0, 1fr);
                gap: 10px;
            }

            .settings-group,
            .settings-group.wide {
                grid-column: auto;
                padding: 10px;
            }

            .settings-action {
                grid-template-columns: 28px minmax(0, 1fr) auto 10px;
                min-height: 42px;
                padding: 7px 8px;
            }

            .settings-action::before {
                width: 26px;
                height: 26px;
            }

            .settings-action-value {
                max-width: 30vw;
                justify-self: end;
                text-align: right;
            }

            .composer {
                padding: 8px 12px 12px;
            }

            .suggestions {
                left: 12px;
                right: 12px;
            }

            .composer-toolbar {
                align-items: center;
                flex-wrap: nowrap;
            }

            .toolbar-left,
            .toolbar-right {
                width: auto;
                flex-wrap: nowrap;
            }

            .profile-select {
                min-width: 64px;
                max-width: 92px;
            }

            .model-pill {
                max-width: 128px;
            }

            .status-pill,
            .context-meter {
                display: none;
            }
        }
    </style>
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
                </aside>
                <div class="composer">
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

    <script nonce="${nonce}">
        (function () {
            try {
                const api = acquireVsCodeApi();
                window.__nanoAgentVsCodeApi = api;
                const status = document.getElementById('status-text');
                if (status) {
                    status.textContent = 'Loading';
                }

                api.postMessage({ command: 'webviewLog', level: 'info', message: 'Prelude script started' });

                window.addEventListener('error', event => {
                    api.postMessage({
                        command: 'webviewLog',
                        level: 'error',
                        message: 'Window error event',
                        details: event && event.error && event.error.stack
                            ? String(event.error.stack)
                            : String(event && event.message ? event.message : 'Unknown error')
                    });
                });

                window.addEventListener('unhandledrejection', event => {
                    const reason = event ? event.reason : undefined;
                    api.postMessage({
                        command: 'webviewLog',
                        level: 'error',
                        message: 'Unhandled promise rejection',
                        details: reason && reason.stack
                            ? String(reason.stack)
                            : String(reason ?? 'Unknown rejection')
                    });
                });
            } catch (error) {
                const status = document.getElementById('status-text');
                if (status) {
                    status.textContent = 'Prelude error';
                }
            }
        })();
    </script>

    <script nonce="${nonce}">
        const api = window.__nanoAgentVsCodeApi || acquireVsCodeApi();
        api.postMessage({ command: 'webviewLog', level: 'info', message: 'Main script starting' });
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
        const sessionsButton = document.getElementById('sessions-button');
        const sendButton = document.getElementById('send-button');
        const stopButton = document.getElementById('stop-button');
        const modelButton = document.getElementById('model-button');
        const modelButtonLabel = document.getElementById('model-button-label');
        const agentMenu = document.getElementById('agent-menu');
        const REASONING_LEVELS = [
            { label: 'None', value: 'none' },
            { label: 'Minimal', value: 'minimal' },
            { label: 'Low', value: 'low' },
            { label: 'Medium', value: 'medium' },
            { label: 'High', value: 'high' },
            { label: 'Extra High', value: 'xhigh' },
            { label: 'Max', value: 'max' }
        ];
        const profileSelect = document.getElementById('profile-select');
        const sectionTitle = document.getElementById('section-title');
        const statusText = document.getElementById('status-text');
        const statusPill = statusText ? statusText.closest('.status-pill') : null;
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
            renderMessageText(body, text, role);
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

        function renderMessageText(container, text, role) {
            if (role === 'assistant' || role === 'reasoning') {
                renderMarkdownText(container, text);
                return;
            }

            renderLinkifiedText(container, text);
        }

        function renderMarkdownText(container, text) {
            container.textContent = '';
            const markdown = document.createElement('div');
            markdown.className = 'markdown-rendered';
            appendMarkdownBlocks(markdown, String(text || ''));
            container.appendChild(markdown);
        }

        function appendMarkdownBlocks(container, text) {
            const newline = getMarkdownNewline();
            const lines = String(text || '').replaceAll(String.fromCharCode(13) + newline, newline).split(newline);
            let index = 0;

            while (index < lines.length) {
                const line = lines[index];
                if (!line || line.trim().length === 0) {
                    index += 1;
                    continue;
                }

                const fence = getMarkdownFence(line);
                if (fence) {
                    const block = readMarkdownCodeFence(lines, index, fence);
                    container.appendChild(createMarkdownCodeBlock(block.content, block.language));
                    index = block.nextIndex;
                    continue;
                }

                const heading = line.match(/^(#{1,6})\\s+(.+)$/);
                if (heading) {
                    const level = Math.min(6, heading[1].length);
                    const element = document.createElement('h' + level);
                    appendMarkdownInline(element, heading[2].replace(/\\s+#+\\s*$/, ''));
                    container.appendChild(element);
                    index += 1;
                    continue;
                }

                if (/^\\s*>\\s?/.test(line)) {
                    const quoteLines = [];
                    while (index < lines.length && /^\\s*>\\s?/.test(lines[index])) {
                        quoteLines.push(lines[index].replace(/^\\s*>\\s?/, ''));
                        index += 1;
                    }
                    const quote = document.createElement('blockquote');
                    appendMarkdownBlocks(quote, quoteLines.join(newline));
                    container.appendChild(quote);
                    continue;
                }

                const listInfo = getMarkdownListInfo(line);
                if (listInfo) {
                    const list = document.createElement(listInfo.ordered ? 'ol' : 'ul');
                    while (index < lines.length) {
                        const itemInfo = getMarkdownListInfo(lines[index]);
                        if (!itemInfo || itemInfo.ordered !== listInfo.ordered) {
                            break;
                        }
                        const item = document.createElement('li');
                        appendMarkdownInline(item, itemInfo.text);
                        list.appendChild(item);
                        index += 1;
                    }
                    container.appendChild(list);
                    continue;
                }

                const paragraph = [];
                while (index < lines.length) {
                    const current = lines[index];
                    if (!current || current.trim().length === 0) {
                        break;
                    }
                    if (getMarkdownFence(current) || /^(#{1,6})\\s+/.test(current) || /^\\s*>\\s?/.test(current) || getMarkdownListInfo(current)) {
                        break;
                    }
                    paragraph.push(current);
                    index += 1;
                }

                const p = document.createElement('p');
                appendMarkdownInline(p, paragraph.join(newline));
                container.appendChild(p);
            }
        }

        function getMarkdownNewline() {
            return String.fromCharCode(10);
        }

        function getMarkdownFence(line) {
            const trimmed = String(line || '').trim();
            const backtickFence = String.fromCharCode(96).repeat(3);
            if (trimmed.startsWith(backtickFence)) {
                return { marker: backtickFence, language: trimmed.slice(backtickFence.length).trim().split(/\\s+/)[0] || '' };
            }
            if (trimmed.startsWith('~~~')) {
                return { marker: '~~~', language: trimmed.slice(3).trim().split(/\\s+/)[0] || '' };
            }
            return null;
        }

        function readMarkdownCodeFence(lines, startIndex, fence) {
            const newline = getMarkdownNewline();
            const codeLines = [];
            let index = startIndex + 1;
            while (index < lines.length) {
                const current = String(lines[index] || '');
                if (current.trim().startsWith(fence.marker)) {
                    return {
                        content: codeLines.join(newline),
                        language: fence.language,
                        nextIndex: index + 1
                    };
                }
                codeLines.push(current);
                index += 1;
            }
            return {
                content: codeLines.join(newline),
                language: fence.language,
                nextIndex: index
            };
        }

        function createMarkdownCodeBlock(content, language) {
            const pre = document.createElement('pre');
            pre.className = 'markdown-code-block';
            if (language) {
                pre.dataset.language = language;
            }
            const code = document.createElement('code');
            code.textContent = String(content || '');
            pre.appendChild(code);
            return pre;
        }

        function getMarkdownListInfo(line) {
            const unordered = String(line || '').match(/^\\s*[-+*]\\s+(.+)$/);
            if (unordered) {
                return { ordered: false, text: unordered[1] };
            }
            const ordered = String(line || '').match(/^\\s*\\d+[.)]\\s+(.+)$/);
            if (ordered) {
                return { ordered: true, text: ordered[1] };
            }
            return null;
        }

        function appendMarkdownInline(container, text) {
            const value = String(text || '');
            let index = 0;
            const tick = String.fromCharCode(96);
            const specials = '[' + tick + '*_~';

            while (index < value.length) {
                const char = value[index];

                if (char === tick) {
                    const end = value.indexOf(tick, index + 1);
                    if (end > index + 1) {
                        const code = document.createElement('code');
                        code.textContent = value.slice(index + 1, end);
                        container.appendChild(code);
                        index = end + 1;
                        continue;
                    }
                }

                if (value.startsWith('~~', index)) {
                    const end = value.indexOf('~~', index + 2);
                    if (end > index + 2) {
                        const del = document.createElement('del');
                        appendMarkdownInline(del, value.slice(index + 2, end));
                        container.appendChild(del);
                        index = end + 2;
                        continue;
                    }
                }

                if (value.startsWith('**', index) || value.startsWith('__', index)) {
                    const marker = value.slice(index, index + 2);
                    const end = value.indexOf(marker, index + 2);
                    if (end > index + 2) {
                        const strong = document.createElement('strong');
                        appendMarkdownInline(strong, value.slice(index + 2, end));
                        container.appendChild(strong);
                        index = end + 2;
                        continue;
                    }
                }

                if ((char === '*' || char === '_') && value[index + 1] !== char) {
                    const end = value.indexOf(char, index + 1);
                    if (end > index + 1) {
                        const em = document.createElement('em');
                        appendMarkdownInline(em, value.slice(index + 1, end));
                        container.appendChild(em);
                        index = end + 1;
                        continue;
                    }
                }

                if (char === '[') {
                    const closeLabel = value.indexOf(']', index + 1);
                    const openTarget = closeLabel >= 0 && value[closeLabel + 1] === '(' ? closeLabel + 1 : -1;
                    const closeTarget = openTarget >= 0 ? value.indexOf(')', openTarget + 1) : -1;
                    if (closeLabel > index + 1 && openTarget >= 0 && closeTarget > openTarget + 1) {
                        container.appendChild(createMarkdownLink(value.slice(index + 1, closeLabel), value.slice(openTarget + 1, closeTarget)));
                        index = closeTarget + 1;
                        continue;
                    }
                }

                let next = value.length;
                for (const special of specials) {
                    const found = value.indexOf(special, index + 1);
                    if (found >= 0 && found < next) {
                        next = found;
                    }
                }
                appendLinkifiedText(container, value.slice(index, next));
                index = next;
            }
        }

        function createMarkdownLink(label, target) {
            const cleanTarget = String(target || '').trim();
            const fileTarget = trimFileReference(cleanTarget);
            if (fileTarget && isLikelyFileReference(fileTarget, cleanTarget, 0)) {
                return createFileReferenceLink(label || fileTarget, fileTarget);
            }

            const link = document.createElement('a');
            link.href = /^(https?:|mailto:)/i.test(cleanTarget) ? cleanTarget : '#';
            link.textContent = label || cleanTarget;
            link.title = cleanTarget;
            if (/^(https?:|mailto:)/i.test(cleanTarget)) {
                link.target = '_blank';
                link.rel = 'noopener noreferrer';
            } else {
                link.addEventListener('click', event => event.preventDefault());
            }
            return link;
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
            renderMarkdownText(body, nextText);
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
            if (!body) {
                return;
            }

            const previousDetails = body.querySelector('.thinking-details');
            const shouldOpen = previousDetails ? previousDetails.open : false;
            const currentText = activeReasoningMessage.dataset.text || '';
            const nextText = currentText + text;
            activeReasoningMessage.dataset.text = nextText;
            body.textContent = '';
            body.appendChild(createReasoningDetails(nextText, shouldOpen));
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

        function handleSendButtonClick() {
            if (promptState.isRunning === true) {
                if (promptState.isCancelling !== true) {
                    post({ command: 'cancelPrompt' });
                }
                return;
            }

            sendCurrentInput();
        }

        sendButton.addEventListener('click', handleSendButtonClick);
        addContextButton.addEventListener('click', () => {
            inputField.value = '/read ';
            inputField.focus();
            inputField.setSelectionRange(inputField.value.length, inputField.value.length);
            updateSuggestions();
            updateComposerState();
        });
        settingsButton.addEventListener('click', toggleSettingsPage);
        sessionsButton.addEventListener('click', () => post({ command: 'listSessions' }));
        settingsCloseButton.addEventListener('click', showChatPage);
        settingsPage.querySelectorAll('.settings-action').forEach(button => {
            button.addEventListener('click', () => runSettingsAction(button));
        });
        modelButton.addEventListener('click', toggleAgentMenu);
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
            } else if (message.command === 'pluginState') {
                showPluginPanel(message.marketplaces || [], message.installed || []);
            } else if (message.command === 'fileMentions') {
                if (message.query === fileMentionQuery) {
                    fileMentionResults = Array.isArray(message.files) ? message.files : [];
                    visibleSuggestions = buildFileSuggestions(fileMentionResults);
                    activeSuggestionIndex = 0;
                    renderSuggestions();
                }
            }
        });

        function toggleSettingsPage() {
            if (activeView === 'settings') {
                showChatPage();
                return;
            }

            showSettingsPage();
        }

        function showSettingsPage() {
            activeView = 'settings';
            messagesDiv.classList.add('hidden');
            settingsPage.classList.remove('hidden');
            settingsButton.classList.add('active');
            settingsButton.title = 'Back to chat';
            settingsButton.setAttribute('aria-label', 'Back to chat');
            hideSuggestions();
            renderSettingsSummary();
            updateActivityVisibility();
        }

        function showChatPage() {
            activeView = 'chat';
            settingsPage.classList.add('hidden');
            messagesDiv.classList.remove('hidden');
            settingsButton.classList.remove('active');
            settingsButton.title = 'Settings';
            settingsButton.setAttribute('aria-label', 'Settings');
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

            if (action === 'plugins') {
                post({ command: 'loadPlugins' });
                return;
            }

            const commandText = button.dataset.command || '';
            if (commandText) {
                post({ command: 'runSessionCommand', text: commandText });
            }
        }

        function updateStatusRail() {
            const running = promptState.isRunning === true;
            const statusLabel = formatStatusText(running);
            stopButton.disabled = true;
            stopButton.hidden = true;
            statusText.textContent = statusLabel;
            if (statusPill) {
                statusPill.hidden = statusLabel.length === 0;
            }
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
                return '';
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
            const running = promptState.isRunning === true;
            const cancelling = promptState.isCancelling === true;
            const hasText = inputField.value.trim().length > 0;
            inputField.disabled = running;
            sendButton.disabled = running ? cancelling : !hasText;
            sendButton.classList.toggle('stop-mode', running);
            sendButton.textContent = running ? '■' : '↑';
            sendButton.title = running
                ? cancelling ? 'Stopping response' : 'Stop response'
                : 'Send message';
            sendButton.setAttribute('aria-label', running
                ? cancelling ? 'Stopping response' : 'Stop response'
                : 'Send message');
            stopButton.disabled = true;
            stopButton.hidden = true;
            modelButton.disabled = running || !sessionInfo;
            profileSelect.disabled = running;
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
                option.textContent = profile.name;
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
                ? profileName + ' - ' + activeProfile.description
                : 'Profile: ' + profileName;
        }

        function renderModelSelect(models) {
            const activeModelId = sessionInfo && sessionInfo.modelId ? sessionInfo.modelId : '';
            const effort = activeReasoningLevel();
            modelButtonLabel.textContent = (activeModelId || 'Model') + (effort ? ' · ' + effort.label : '');
            modelButton.title = models.length > 0
                ? 'Model, reasoning & thinking. Current: ' + (activeModelId || models[0])
                : 'Model, reasoning & thinking';
            if (!agentMenu.classList.contains('hidden')) {
                renderAgentMenu();
            }
        }

        function activeReasoningLevel() {
            const effort = sessionInfo && sessionInfo.reasoningEffort
                ? String(sessionInfo.reasoningEffort).toLowerCase()
                : '';
            return REASONING_LEVELS.find(level => level.value === effort) || null;
        }

        function thinkingOn() {
            return String(sessionInfo && sessionInfo.thinkingMode || '').toLowerCase() === 'on';
        }

        function toggleAgentMenu() {
            if (agentMenu.classList.contains('hidden')) {
                openAgentMenu();
            } else {
                closeAgentMenu();
            }
        }

        function openAgentMenu() {
            if (!sessionInfo) {
                post({ command: 'selectModel' });
                return;
            }
            renderAgentMenu();
            agentMenu.classList.remove('hidden');
            const rect = modelButton.getBoundingClientRect();
            agentMenu.style.left = Math.max(8, rect.left) + 'px';
            agentMenu.style.bottom = (window.innerHeight - rect.top + 6) + 'px';
            setTimeout(() => document.addEventListener('mousedown', handleAgentMenuOutside), 0);
        }

        function closeAgentMenu() {
            agentMenu.classList.add('hidden');
            document.removeEventListener('mousedown', handleAgentMenuOutside);
        }

        function handleAgentMenuOutside(event) {
            if (!agentMenu.contains(event.target) && event.target !== modelButton && !modelButton.contains(event.target)) {
                closeAgentMenu();
            }
        }

        function addMenuHeader(text) {
            const header = document.createElement('div');
            header.className = 'agent-menu-header';
            header.textContent = text;
            agentMenu.appendChild(header);
        }

        function addMenuItem(label, checked, onClick) {
            const item = document.createElement('button');
            item.className = 'agent-menu-item';
            item.setAttribute('role', 'menuitemradio');
            const check = document.createElement('span');
            check.className = 'agent-menu-check';
            check.textContent = checked ? '✓' : '';
            const text = document.createElement('span');
            text.className = 'agent-menu-label';
            text.textContent = label;
            item.appendChild(check);
            item.appendChild(text);
            item.addEventListener('click', () => {
                closeAgentMenu();
                onClick();
            });
            return item;
        }

        function addMenuSeparator() {
            const sep = document.createElement('div');
            sep.className = 'agent-menu-sep';
            agentMenu.appendChild(sep);
        }

        function renderAgentMenu() {
            agentMenu.textContent = '';

            addMenuHeader('Reasoning');
            const activeEffort = activeReasoningLevel();
            REASONING_LEVELS.forEach(level => {
                agentMenu.appendChild(addMenuItem(
                    level.label,
                    activeEffort && activeEffort.value === level.value,
                    () => post({ command: 'runSessionCommand', text: '/reasoning ' + level.value })
                ));
            });

            addMenuSeparator();
            addMenuHeader('Thinking');
            const isOn = thinkingOn();
            agentMenu.appendChild(addMenuItem('Thinking on', isOn,
                () => post({ command: 'runSessionCommand', text: '/thinking on' })));
            agentMenu.appendChild(addMenuItem('Thinking off', !isOn,
                () => post({ command: 'runSessionCommand', text: '/thinking off' })));

            const models = getAvailableModelIds();
            if (models.length > 0) {
                addMenuSeparator();
                addMenuHeader('Model');
                const activeModelId = sessionInfo && sessionInfo.modelId ? sessionInfo.modelId : '';
                const modelGroup = document.createElement('div');
                modelGroup.className = 'agent-menu-models';
                models.forEach(modelId => {
                    modelGroup.appendChild(addMenuItem(modelId, modelId === activeModelId,
                        () => post({ command: 'changeModel', modelId })));
                });
                agentMenu.appendChild(modelGroup);
            }
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
            if (!body) {
                return;
            }

            const diffModel = getToolDiffModel(call);
            const previousDetails = body.querySelector('.tool-call-details');
            const shouldOpen = previousDetails ? previousDetails.open : shouldOpenToolCallByDefault(call, diffModel);
            article.className = 'message-card tool ' + normalizeClass(call.status || 'pending');
            article.dataset.text = text;
            body.textContent = '';
            body.appendChild(createToolMessageView(call, shouldOpen, diffModel));
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

        function createReasoningDetails(text, open) {
            const view = createInlineDisclosure({
                title: 'Thinking',
                meta: text ? summarizeLineCount(text) : '',
                icon: '◌',
                className: 'thinking-details',
                open
            });

            const markdown = document.createElement('div');
            markdown.className = 'thinking-markdown';
            renderMarkdownText(markdown, text);
            view.content.appendChild(markdown);
            return view.details;
        }

        function createToolMessageView(call, open, diffModel) {
            const presentation = getToolCallPresentation(call, diffModel);
            const view = createInlineDisclosure({
                title: presentation.title,
                meta: presentation.meta,
                icon: presentation.icon,
                className: 'tool-call-details',
                open
            });
            const container = document.createElement('div');
            container.className = 'tool-message';

            const output = Array.isArray(call.content) ? call.content.join('\\n') : '';
            if (diffModel.length > 0) {
                diffModel.forEach(file => container.appendChild(createDiffView(file)));
                const extraOutput = stripDiffLikeOutput(output);
                if (extraOutput) {
                    container.appendChild(createVsCodeToolOutputBlock(call, extraOutput));
                }
            } else {
                if (call.kind) {
                    const kind = document.createElement('div');
                    kind.className = 'tool-message-kind';
                    kind.textContent = call.kind;
                    kind.title = call.kind;
                    container.appendChild(kind);
                }

                if (output) {
                    container.appendChild(createToolOutputView(call, output));
                } else {
                    const pending = document.createElement('div');
                    pending.className = 'tool-pending';
                    pending.textContent = 'Waiting for output...';
                    container.appendChild(pending);
                }

                if (typeof call.rawInput !== 'undefined') {
                    const details = createDetails('Arguments', formatPayload(call.rawInput), false);
                    details.className = 'tool-arguments';
                    container.appendChild(details);
                }
            }

            view.content.appendChild(container);
            return view.details;
        }

        function getToolDiffModel(call) {
            let model = [];
            try {
                model = normalizeDiffModel(buildDiffModel(call));
            } catch (error) {
                model = [];
            }

            if (model.length > 0) {
                return model;
            }

            return createFallbackFileDiffModel(call);
        }

        function createToolOutputView(call, output) {
            if (isReadFileToolCall(call)) {
                const readOutput = normalizeReadFileToolOutput(call, output);
                return createVsCodeCodeBlock({
                    path: readOutput.path,
                    title: readOutput.title,
                    content: readOutput.content,
                    language: readOutput.language,
                    className: 'read-file-output',
                    copyTitle: 'Copy file contents'
                });
            }

            return createVsCodeToolOutputBlock(call, output);
        }

        function normalizeReadFileToolOutput(call, output) {
            const extracted = extractFencedCodePayload(output);
            const path = inferToolFilePath(call) || inferReadOutputPath(output);
            const content = extracted ? extracted.content : String(output || '');
            const language = extracted && extracted.language
                ? extracted.language
                : inferLanguageFromPath(path);

            return {
                path,
                title: path ? path : 'Read file output',
                content,
                language: language || 'text'
            };
        }

        function extractFencedCodePayload(output) {
            const text = String(output || '').replace(/\\r\\n/g, '\\n');
            const trimmed = text.trim();
            if (!trimmed) {
                return null;
            }

            const backtickFence = String.fromCharCode(96).repeat(3);
            const exactFence = readDelimitedCodeFence(trimmed, backtickFence) || readDelimitedCodeFence(trimmed, '~~~');
            if (exactFence) {
                return exactFence;
            }

            const labelMatch = trimmed.match(/^(?:read|opened|contents?|content|file|output)[^\\n]*:\\s*\\n([\\s\\S]+)$/i);
            if (labelMatch) {
                return readDelimitedCodeFence(labelMatch[1].trim(), backtickFence)
                    || readDelimitedCodeFence(labelMatch[1].trim(), '~~~');
            }

            return null;
        }

        function readDelimitedCodeFence(text, marker) {
            const value = String(text || '');
            if (!value.startsWith(marker)) {
                return null;
            }

            const firstLineEnd = value.indexOf('\\n');
            if (firstLineEnd < 0) {
                return null;
            }

            const firstLine = value.slice(marker.length, firstLineEnd).trim();
            const rest = value.slice(firstLineEnd + 1);
            const closing = '\\n' + marker;
            const closingIndex = rest.lastIndexOf(closing);
            if (closingIndex < 0 || rest.slice(closingIndex + closing.length).trim()) {
                return null;
            }

            return {
                language: normalizeLanguageName(firstLine.split(/\\s+/)[0] || ''),
                content: rest.slice(0, closingIndex)
            };
        }

        function inferReadOutputPath(output) {
            const text = String(output || '');
            const patterns = [
                /(?:read|opened|contents? of|file|path):\\s*([^\\n]+)/i,
                /^\\s*[-•]?\\s*file:\\s+([^\\n]+)/im
            ];

            for (const pattern of patterns) {
                const match = text.match(pattern);
                if (match) {
                    const path = trimFileReference(match[1].trim());
                    if (path && looksLikeFileReference(path)) {
                        return path;
                    }
                }
            }

            return '';
        }

        function createVsCodeToolOutputBlock(call, output) {
            const presentation = getToolOutputBlockPresentation(call, output);
            return createVsCodeCodeBlock({
                title: presentation.title,
                content: output,
                language: presentation.language,
                className: 'tool-output ' + presentation.className,
                lineNumbers: false,
                syntaxHighlight: false,
                linkify: true,
                copyTitle: 'Copy tool output'
            });
        }

        function getToolOutputBlockPresentation(call, output) {
            const haystack = getToolCallHaystack(call);

            if (isListToolCall(call) || looksLikeDirectoryListOutput(output)) {
                return { title: 'File listing', language: 'list', className: 'listing-output' };
            }

            if (/search|grep|find|match|rg|ripgrep/.test(haystack)) {
                return { title: 'Search results', language: 'results', className: 'search-output' };
            }

            if (/terminal|shell|bash|zsh|powershell|exec|spawn|run command|command/.test(haystack)) {
                return { title: 'Terminal output', language: 'shell', className: 'terminal-output' };
            }

            if (/write|create|edit|patch|update|delete|rename|move|copy/.test(haystack)) {
                return { title: 'Operation output', language: 'output', className: 'operation-output' };
            }

            return { title: 'Tool output', language: 'output', className: 'generic-output' };
        }

        function isListToolCall(call) {
            const haystack = getToolCallHaystack(call);
            return /(^|\\b)(list|ls|tree|directory|workspace files|files)(\\b|$)/.test(haystack);
        }

        function looksLikeDirectoryListOutput(output) {
            const text = String(output || '');
            return /(^|\\n)\\s*[-•]\\s*(file|directory):\\s+/i.test(text)
                || /(^|\\n)\\s*[-•]?\\s*listed\\b.*\\(\\d+\\s+entr(?:y|ies)\\)/i.test(text);
        }

        function getToolCallHaystack(call) {
            if (!call) {
                return '';
            }

            return [
                call.title,
                call.kind,
                formatPayload(call.rawInput || '')
            ].map(value => String(value || '').toLowerCase()).join(' ');
        }

        function createToolOutputPre(text) {
            const pre = document.createElement('pre');
            pre.className = 'tool-output-pre';
            renderLinkifiedText(pre, text);
            return pre;
        }

        function isReadFileToolCall(call) {
            if (!call) {
                return false;
            }

            const haystack = getToolCallHaystack(call);

            if (/\\b(list|ls|tree|search|grep|find|scan|glob)\\b/.test(haystack)) {
                return false;
            }

            if (/\\b(read_file|read-file|readfile|fs_read|fs\\.read|read_file_system|filesystem[._:-]read|filesystem.*readfile)\\b/.test(haystack)) {
                return true;
            }

            if (/\\b(read|open|cat|show|view)\\s+(?:a\\s+)?(?:workspace\\s+)?file\\b/.test(haystack)) {
                return true;
            }

            if (/\\bcat\\s+[^\\n]+/.test(haystack) && hasLikelyToolFilePath(call)) {
                return true;
            }

            return /\\bread\\b/.test(haystack) && hasLikelyToolFilePath(call);
        }

        function hasLikelyToolFilePath(call) {
            return Boolean(inferToolFilePath(call));
        }

        function inferToolFilePath(call) {
            const rawInput = call && call.rawInput && typeof call.rawInput === 'object'
                ? call.rawInput
                : null;

            if (rawInput) {
                const directPath = firstStringValueDeep(rawInput, [
                    'path',
                    'file',
                    'filename',
                    'fileName',
                    'file_name',
                    'filePath',
                    'filepath',
                    'file_path',
                    'absolutePath',
                    'absolute_path',
                    'relativePath',
                    'relative_path',
                    'target',
                    'targetFile',
                    'target_file',
                    'uri'
                ]);
                if (directPath) {
                    return trimFileReference(directPath);
                }
            }

            const values = [call && call.title, call && call.kind, formatPayload(rawInput || '')];
            for (const value of values) {
                const path = firstFileReferenceInText(value);
                if (path) {
                    return trimFileReference(path);
                }
            }

            return '';
        }

        function firstFileReferenceInText(text) {
            fileReferencePattern.lastIndex = 0;
            const match = fileReferencePattern.exec(String(text || ''));
            fileReferencePattern.lastIndex = 0;
            return match ? match[2] : '';
        }

        function createVsCodeCodeBlock(options) {
            const path = options && options.path ? String(options.path) : '';
            const titleText = options && options.title ? String(options.title) : 'Read file output';
            const content = String((options && options.content) || '');
            const language = options && options.language ? String(options.language) : inferLanguageFromPath(path);
            const lines = splitCodeLines(content);
            const showLineNumbers = !options || options.lineNumbers !== false;
            const syntaxHighlight = !options || options.syntaxHighlight !== false;
            const linkify = Boolean(options && options.linkify);
            const extraClassName = options && options.className ? ' ' + String(options.className).trim() : '';

            const wrap = document.createElement('div');
            wrap.className = ('vscode-code-block' + extraClassName).trim();

            const header = document.createElement('div');
            header.className = 'vscode-code-header';

            const headerMain = document.createElement('div');
            headerMain.className = 'vscode-code-header-main';
            if (path) {
                headerMain.appendChild(createFileReferenceLink(path, path));
            } else {
                const title = document.createElement('span');
                title.className = 'vscode-code-title';
                title.textContent = titleText;
                headerMain.appendChild(title);
            }
            header.appendChild(headerMain);

            const actions = document.createElement('div');
            actions.className = 'vscode-code-actions';

            if (language) {
                const lang = document.createElement('span');
                lang.className = 'vscode-code-language';
                lang.textContent = language;
                actions.appendChild(lang);
            }

            const lineCount = document.createElement('span');
            lineCount.className = 'vscode-code-line-count';
            lineCount.textContent = lines.length + (lines.length === 1 ? ' line' : ' lines');
            actions.appendChild(lineCount);

            const copyButton = document.createElement('button');
            copyButton.type = 'button';
            copyButton.className = 'vscode-code-copy-button';
            copyButton.title = options && options.copyTitle ? String(options.copyTitle) : 'Copy output';
            copyButton.setAttribute('aria-label', copyButton.title);
            copyButton.textContent = '⧉';
            copyButton.addEventListener('click', event => {
                event.preventDefault();
                event.stopPropagation();
                copyTextToClipboard(content);
            });
            actions.appendChild(copyButton);
            header.appendChild(actions);
            wrap.appendChild(header);

            const pre = document.createElement('pre');
            pre.className = 'vscode-code-body';
            lines.forEach((line, index) => {
                const row = document.createElement('div');
                row.className = showLineNumbers ? 'vscode-code-line' : 'vscode-code-line no-line-number';

                if (showLineNumbers) {
                    const lineNumber = document.createElement('span');
                    lineNumber.className = 'vscode-code-line-number';
                    lineNumber.textContent = String(index + 1);
                    row.appendChild(lineNumber);
                }

                const code = document.createElement('span');
                code.className = 'vscode-code-line-content';
                if (linkify) {
                    appendToolOutputLine(code, line);
                } else if (syntaxHighlight) {
                    appendSyntaxHighlightedLine(code, line, language);
                } else {
                    code.textContent = line;
                }
                row.appendChild(code);

                pre.appendChild(row);
            });
            wrap.appendChild(pre);
            return wrap;
        }

        function appendToolOutputLine(container, line) {
            const text = String(line || '');
            const entryMatch = text.match(/^(\\s*[-•]\\s*)(directory|file):\\s+(.+)$/i);
            if (entryMatch) {
                appendSyntaxSpan(container, entryMatch[1], 'syntax-muted');
                appendSyntaxSpan(container, entryMatch[2], 'syntax-property');
                container.appendChild(document.createTextNode(': '));
                const path = trimFileReference(entryMatch[3].trim());
                if (path) {
                    container.appendChild(createFileReferenceLink(path, path));
                }
                return;
            }

            const summaryMatch = text.match(/^(\\s*[-•]?\\s*)(listed|found|matched|created|updated|deleted|read|wrote)(\\b.*)$/i);
            if (summaryMatch) {
                appendSyntaxSpan(container, summaryMatch[1], 'syntax-muted');
                appendSyntaxSpan(container, summaryMatch[2], 'syntax-keyword');
                appendLinkifiedText(container, summaryMatch[3]);
                return;
            }

            const moreMatch = text.match(/^(\\s*\\.{3}\\s*\\+\\d+\\s+.+)$/);
            if (moreMatch) {
                appendSyntaxSpan(container, moreMatch[1], 'syntax-muted');
                return;
            }

            appendLinkifiedText(container, text);
        }

        function splitCodeLines(text) {
            const lines = String(text || '').replace(/\\r\\n/g, '\\n').split('\\n');
            if (lines.length > 1 && lines[lines.length - 1] === '') {
                lines.pop();
            }
            return lines.length > 0 ? lines : [''];
        }

        function normalizeLanguageName(language) {
            const value = String(language || '').trim().toLowerCase();
            const aliases = {
                js: 'javascript',
                jsx: 'javascript',
                mjs: 'javascript',
                cjs: 'javascript',
                ts: 'typescript',
                tsx: 'typescript',
                py: 'python',
                rb: 'ruby',
                cs: 'csharp',
                sh: 'shell',
                bash: 'shell',
                zsh: 'shell',
                ps1: 'powershell',
                yml: 'yaml',
                md: 'markdown'
            };
            return aliases[value] || value;
        }

        function inferLanguageFromPath(path) {
            const ext = String(path || '').split(/[?#]/)[0].split('.').pop().toLowerCase();
            const languages = {
                js: 'javascript',
                jsx: 'javascript',
                mjs: 'javascript',
                cjs: 'javascript',
                ts: 'typescript',
                tsx: 'typescript',
                json: 'json',
                jsonc: 'json',
                py: 'python',
                rb: 'ruby',
                go: 'go',
                rs: 'rust',
                java: 'java',
                c: 'c',
                h: 'c',
                cpp: 'cpp',
                cc: 'cpp',
                cxx: 'cpp',
                hpp: 'cpp',
                cs: 'csharp',
                php: 'php',
                html: 'html',
                htm: 'html',
                xml: 'xml',
                css: 'css',
                scss: 'scss',
                less: 'less',
                md: 'markdown',
                sh: 'shell',
                bash: 'shell',
                zsh: 'shell',
                fish: 'shell',
                ps1: 'powershell',
                yml: 'yaml',
                yaml: 'yaml',
                toml: 'toml',
                ini: 'ini',
                sql: 'sql'
            };
            return normalizeLanguageName(languages[ext] || ext || 'text');
        }

        function appendSyntaxHighlightedLine(container, line, language) {
            const text = String(line || '');
            const normalizedLanguage = normalizeLanguageName(language);

            if (normalizedLanguage === 'markdown') {
                appendMarkdownSyntaxHighlightedLine(container, text);
                return;
            }

            const commentStart = findSyntaxCommentStart(text, normalizedLanguage);
            const codeText = commentStart >= 0 ? text.slice(0, commentStart) : text;
            const commentText = commentStart >= 0 ? text.slice(commentStart) : '';

            appendSyntaxCodeTokens(container, codeText, normalizedLanguage);
            if (commentText) {
                appendSyntaxSpan(container, commentText, 'syntax-comment');
            }
        }

        function appendMarkdownSyntaxHighlightedLine(container, line) {
            const text = String(line || '');
            const headingMatch = text.match(/^(\\s{0,3}#{1,6}\\s+)(.*)$/);
            if (headingMatch) {
                appendSyntaxSpan(container, headingMatch[1], 'syntax-keyword');
                appendSyntaxCodeTokens(container, headingMatch[2], 'markdown');
                return;
            }

            const quoteMatch = text.match(/^(\\s*>+\\s?)(.*)$/);
            if (quoteMatch) {
                appendSyntaxSpan(container, quoteMatch[1], 'syntax-comment');
                appendSyntaxCodeTokens(container, quoteMatch[2], 'markdown');
                return;
            }

            const listMatch = text.match(/^(\\s*)([-*+]\\s+|\\d+\\.\\s+)(.*)$/);
            if (listMatch) {
                container.appendChild(document.createTextNode(listMatch[1]));
                appendSyntaxSpan(container, listMatch[2], 'syntax-keyword');
                appendSyntaxCodeTokens(container, listMatch[3], 'markdown');
                return;
            }

            const backtickFence = String.fromCharCode(96).repeat(3);
            if (text.trim().startsWith(backtickFence) || text.trim().startsWith('~~~')) {
                appendSyntaxSpan(container, text, 'syntax-keyword');
                return;
            }

            appendSyntaxCodeTokens(container, text, 'markdown');
        }

        function findSyntaxCommentStart(text, language) {
            const value = String(text || '');
            if (!value) {
                return -1;
            }

            if (language === 'html' || language === 'xml' || language === 'markdown') {
                return value.indexOf('<!--');
            }

            if (/python|ruby|shell|powershell|yaml|toml|ini/.test(language)) {
                return value.indexOf('#');
            }

            if (/css|scss|less/.test(language)) {
                return value.indexOf('/*');
            }

            return value.indexOf('//');
        }

        function appendSyntaxCodeTokens(container, text, language) {
            const tokenPattern = /("(?:\\\\.|[^"\\\\])*"|'(?:\\\\.|[^'\\\\])*'|\\b[A-Za-z_$][\\w$]*\\b|\\b\\d+(?:\\.\\d+)?\\b)/g;
            let lastIndex = 0;
            let match = tokenPattern.exec(text);

            while (match) {
                if (match.index > lastIndex) {
                    container.appendChild(document.createTextNode(text.slice(lastIndex, match.index)));
                }

                const token = match[0];
                const className = getSyntaxTokenClass(token, language, text.slice(match.index + token.length));
                if (className) {
                    appendSyntaxSpan(container, token, className);
                } else {
                    container.appendChild(document.createTextNode(token));
                }

                lastIndex = match.index + token.length;
                match = tokenPattern.exec(text);
            }

            if (lastIndex < text.length) {
                container.appendChild(document.createTextNode(text.slice(lastIndex)));
            }
        }

        function getSyntaxTokenClass(token, language, tail) {
            if (/^['"]/.test(token)) {
                return 'syntax-string';
            }

            if (/^\\d/.test(token)) {
                return 'syntax-number';
            }

            const word = token.toLowerCase();
            const keywords = syntaxKeywordsForLanguage(language);
            if (keywords.has(word)) {
                return 'syntax-keyword';
            }

            if (/^(true|false|null|undefined|none|nil)$/i.test(token)) {
                return 'syntax-constant';
            }

            if (/^\\s*\\(/.test(tail || '')) {
                return 'syntax-function';
            }

            if (/^\\s*:/.test(tail || '') || /^\\s*=/.test(tail || '')) {
                return 'syntax-property';
            }

            return '';
        }

        function syntaxKeywordsForLanguage(language) {
            const base = ['break', 'case', 'catch', 'class', 'const', 'continue', 'default', 'delete', 'do', 'else', 'enum', 'export', 'extends', 'finally', 'for', 'from', 'function', 'if', 'import', 'in', 'instanceof', 'let', 'new', 'return', 'static', 'super', 'switch', 'this', 'throw', 'try', 'typeof', 'var', 'void', 'while', 'with', 'yield', 'async', 'await'];
            const python = ['and', 'as', 'assert', 'async', 'await', 'break', 'class', 'continue', 'def', 'del', 'elif', 'else', 'except', 'finally', 'for', 'from', 'global', 'if', 'import', 'in', 'is', 'lambda', 'nonlocal', 'not', 'or', 'pass', 'raise', 'return', 'try', 'while', 'with', 'yield'];
            const csharp = ['abstract', 'as', 'base', 'bool', 'byte', 'char', 'checked', 'decimal', 'delegate', 'double', 'event', 'explicit', 'extern', 'false', 'fixed', 'float', 'foreach', 'implicit', 'int', 'interface', 'internal', 'is', 'lock', 'long', 'namespace', 'object', 'operator', 'out', 'override', 'params', 'private', 'protected', 'public', 'readonly', 'ref', 'sbyte', 'sealed', 'short', 'sizeof', 'stackalloc', 'string', 'struct', 'true', 'uint', 'ulong', 'unchecked', 'unsafe', 'ushort', 'using', 'virtual'];
            const java = ['abstract', 'boolean', 'byte', 'char', 'double', 'final', 'float', 'implements', 'int', 'interface', 'long', 'native', 'package', 'private', 'protected', 'public', 'short', 'strictfp', 'synchronized', 'throws', 'transient', 'volatile'];
            const go = ['chan', 'defer', 'fallthrough', 'func', 'go', 'import', 'interface', 'map', 'package', 'range', 'select', 'struct', 'type'];
            const rust = ['as', 'crate', 'dyn', 'extern', 'fn', 'impl', 'loop', 'match', 'mod', 'move', 'mut', 'pub', 'ref', 'self', 'Self', 'trait', 'type', 'unsafe', 'use', 'where'];
            const css = ['align-items', 'background', 'border', 'color', 'display', 'flex', 'font', 'gap', 'grid', 'height', 'margin', 'overflow', 'padding', 'position', 'width'];
            const sql = ['select', 'from', 'where', 'join', 'left', 'right', 'inner', 'outer', 'insert', 'update', 'delete', 'create', 'alter', 'drop', 'table', 'group', 'order', 'by', 'limit', 'having', 'and', 'or', 'not', 'null'];

            if (/python/.test(language)) {
                return new Set(python);
            }
            if (/csharp/.test(language)) {
                return new Set(base.concat(csharp));
            }
            if (/java/.test(language)) {
                return new Set(base.concat(java));
            }
            if (/go/.test(language)) {
                return new Set(base.concat(go));
            }
            if (/rust/.test(language)) {
                return new Set(base.concat(rust));
            }
            if (/css|scss|less/.test(language)) {
                return new Set(css);
            }
            if (/sql/.test(language)) {
                return new Set(sql);
            }
            return new Set(base);
        }

        function appendSyntaxSpan(container, text, className) {
            const span = document.createElement('span');
            span.className = className;
            span.textContent = text;
            container.appendChild(span);
        }

        function normalizeDiffModel(model) {
            if (Array.isArray(model)) {
                return model
                    .map(normalizeDiffFile)
                    .filter(file => file && file.lines.length > 0);
            }

            if (model && Array.isArray(model.files)) {
                return model.files
                    .map(normalizeDiffFile)
                    .filter(file => file && file.lines.length > 0);
            }

            return [];
        }

        function normalizeDiffFile(file) {
            if (!file || !Array.isArray(file.lines)) {
                return null;
            }

            const path = String(file.path || file.file || file.name || 'Changed file');
            const lines = file.lines.map(line => {
                if (typeof line === 'string') {
                    return normalizeDiffLine(line);
                }

                if (!line || typeof line !== 'object') {
                    return null;
                }

                const type = normalizeDiffLineType(line.type || line.kind || line.status);
                const text = typeof line.text === 'string'
                    ? line.text
                    : typeof line.content === 'string'
                        ? line.content
                        : '';
                return Object.assign({}, line, { type, text });
            }).filter(Boolean);

            return { path, lines };
        }

        function normalizeDiffLine(line) {
            const text = String(line || '');
            if (text.startsWith('+') && !text.startsWith('+++')) {
                return { type: 'add', text: text.slice(1) };
            }
            if (text.startsWith('-') && !text.startsWith('---')) {
                return { type: 'del', text: text.slice(1) };
            }
            if (text.startsWith('@@')) {
                return { type: 'meta', text };
            }
            return { type: 'context', text: text.startsWith(' ') ? text.slice(1) : text };
        }

        function normalizeDiffLineType(type) {
            const value = String(type || '').toLowerCase();
            if (value === 'add' || value === 'added' || value === 'insert' || value === 'inserted' || value === '+') {
                return 'add';
            }
            if (value === 'del' || value === 'delete' || value === 'deleted' || value === 'remove' || value === 'removed' || value === '-') {
                return 'del';
            }
            if (value === 'meta' || value === 'header' || value === 'hunk') {
                return 'meta';
            }
            return 'context';
        }

        function createFallbackFileDiffModel(call) {
            const rawInput = call && call.rawInput && typeof call.rawInput === 'object'
                ? call.rawInput
                : null;
            const output = Array.isArray(call && call.content) ? call.content.join('\\n') : '';
            const fallback = [];

            if (rawInput) {
                collectRawInputDiffs(rawInput, fallback);
            }

            if (fallback.length > 0) {
                return fallback;
            }

            const payloads = [];
            if (rawInput) {
                ['patch', 'diff', 'edits', 'input'].forEach(key => {
                    if (typeof rawInput[key] === 'string') {
                        payloads.push(rawInput[key]);
                    }
                });
            }
            if (output) {
                payloads.push(output);
            }

            for (const payload of payloads) {
                const parsed = parsePatchText(payload);
                if (parsed.length > 0) {
                    return parsed;
                }
            }

            return [];
        }

        function collectRawInputDiffs(rawInput, fallback) {
            if (!rawInput || typeof rawInput !== 'object') {
                return;
            }

            if (Array.isArray(rawInput.edits)) {
                rawInput.edits.forEach(edit => collectRawInputDiffs(edit, fallback));
            }

            const path = firstStringValue(rawInput, ['path', 'file', 'filePath', 'file_path', 'target', 'targetFile', 'target_file']);
            const content = firstStringValue(rawInput, ['content', 'text', 'newContent', 'new_content', 'body']);
            const patch = firstStringValue(rawInput, ['patch', 'diff']);

            if (patch) {
                parsePatchText(patch).forEach(file => fallback.push(file));
                return;
            }

            if (path && content && looksLikeFileWrite(rawInput)) {
                fallback.push({
                    path,
                    lines: splitDiffContent(content).map(line => ({ type: 'add', text: line }))
                });
            }
        }

        function firstStringValue(source, keys) {
            for (const key of keys) {
                if (typeof source[key] === 'string' && source[key].trim()) {
                    return source[key];
                }
            }
            return '';
        }

        function firstStringValueDeep(source, keys, depth) {
            if (!source || depth > 4) {
                return '';
            }

            const direct = !Array.isArray(source) && typeof source === 'object'
                ? firstStringValue(source, keys)
                : '';
            if (direct) {
                return direct;
            }

            const values = Array.isArray(source)
                ? source
                : typeof source === 'object'
                    ? Object.keys(source).map(key => source[key])
                    : [];

            for (const value of values) {
                if (!value || typeof value !== 'object') {
                    continue;
                }
                const found = firstStringValueDeep(value, keys, (depth || 0) + 1);
                if (found) {
                    return found;
                }
            }

            return '';
        }

        function looksLikeFileWrite(rawInput) {
            const op = String(rawInput.operation || rawInput.action || rawInput.command || rawInput.kind || '').toLowerCase();
            return !op || /write|create|add|save|replace|edit|update|patch/.test(op);
        }

        function parsePatchText(text) {
            const value = String(text || '');
            if (!value || (!value.includes('*** ') && !value.includes('@@') && !value.includes('diff --git'))) {
                return [];
            }

            if (value.includes('*** Begin Patch') || value.includes('*** Add File:') || value.includes('*** Update File:') || value.includes('*** Delete File:')) {
                return parseApplyPatchText(value);
            }

            return parseUnifiedDiffText(value);
        }

        function parseApplyPatchText(text) {
            const files = [];
            let current = null;
            String(text || '').split(/\\r?\\n/).forEach(line => {
                const fileMatch = line.match(/^\\*\\*\\*\\s+(Add|Update|Delete) File:\\s+(.+)$/);
                if (fileMatch) {
                    current = {
                        path: fileMatch[2].trim(),
                        operation: fileMatch[1].toLowerCase(),
                        lines: []
                    };
                    files.push(current);
                    return;
                }

                if (!current || line === '*** Begin Patch' || line === '*** End Patch') {
                    return;
                }

                if (line.startsWith('@@')) {
                    current.lines.push({ type: 'meta', text: line });
                    return;
                }

                current.lines.push(normalizeDiffLine(line));
            });

            return files.filter(file => file.lines.length > 0);
        }

        function parseUnifiedDiffText(text) {
            const files = [];
            let current = null;
            String(text || '').split(/\\r?\\n/).forEach(line => {
                const gitMatch = line.match(/^diff --git\\s+a\\/(.+?)\\s+b\\/(.+)$/);
                if (gitMatch) {
                    current = { path: gitMatch[2].trim(), lines: [] };
                    files.push(current);
                    return;
                }

                const newFileMatch = line.match(/^\\+\\+\\+\\s+(?:b\\/)?(.+)$/);
                if (newFileMatch && !line.includes('/dev/null')) {
                    if (!current) {
                        current = { path: newFileMatch[1].trim(), lines: [] };
                        files.push(current);
                    } else {
                        current.path = newFileMatch[1].trim();
                    }
                    return;
                }

                const oldFileMatch = line.match(/^---\\s+(?:a\\/)?(.+)$/);
                if (oldFileMatch && !line.includes('/dev/null') && !current) {
                    current = { path: oldFileMatch[1].trim(), lines: [] };
                    files.push(current);
                    return;
                }

                if (!current) {
                    return;
                }

                if (line.startsWith('@@')) {
                    current.lines.push({ type: 'meta', text: line });
                } else if (line.startsWith('+') && !line.startsWith('+++')) {
                    current.lines.push({ type: 'add', text: line.slice(1) });
                } else if (line.startsWith('-') && !line.startsWith('---')) {
                    current.lines.push({ type: 'del', text: line.slice(1) });
                } else if (line.startsWith(' ')) {
                    current.lines.push({ type: 'context', text: line.slice(1) });
                }
            });

            return files.filter(file => file.lines.length > 0);
        }

        function splitDiffContent(content) {
            const lines = String(content || '').replace(/\\r\\n/g, '\\n').split('\\n');
            if (lines.length > 1 && lines[lines.length - 1] === '') {
                lines.pop();
            }
            return lines;
        }

        function getToolCallPresentation(call, diffModel) {
            const title = String((call && call.title) || '').trim();
            const kind = String((call && call.kind) || '').trim();
            const haystack = (title + ' ' + kind + ' ' + formatPayload((call && call.rawInput) || '')).toLowerCase();
            const count = diffModel.length;

            if (count > 0) {
                const totals = countDiffTotals(diffModel);
                if (/patch|apply_patch|apply patch/.test(haystack)) {
                    return { title: 'Applied patch', meta: '', icon: '✎' };
                }
                if (totals.added > 0 && totals.removed === 0) {
                    return { title: count === 1 ? 'Created file' : 'Created files', meta: '', icon: '' };
                }
                if (totals.removed > 0 && totals.added === 0) {
                    return { title: count === 1 ? 'Deleted file' : 'Deleted files', meta: '', icon: '✎' };
                }
                return { title: count === 1 ? 'Edited a file' : 'Edited files', meta: '', icon: '✎' };
            }

            if (/create|write|new file|add file/.test(haystack)) {
                return { title: 'Created file', meta: call.status || 'pending', icon: '' };
            }
            if (/edit|replace|patch|update/.test(haystack)) {
                return { title: 'Edited a file', meta: call.status || 'pending', icon: '✎' };
            }
            if (/read|open/.test(haystack)) {
                return { title: 'Read file', meta: call.status || 'pending', icon: '' };
            }
            if (/list|ls|tree/.test(haystack)) {
                return { title: 'Listed files', meta: call.status || 'pending', icon: '' };
            }
            if (/search|grep|find/.test(haystack)) {
                return { title: 'Searched files', meta: call.status || 'pending', icon: '' };
            }

            return { title: title || (call && call.toolCallId) || 'Tool call', meta: call.status || 'pending', icon: '◇' };
        }

        function shouldOpenToolCallByDefault(call, diffModel) {
            if (diffModel.length > 0) {
                return true;
            }

            const title = String((call && call.title) || '').toLowerCase();
            const kind = String((call && call.kind) || '').toLowerCase();
            return /create|write|edit|replace|patch|delete|remove/.test(title + ' ' + kind) || isReadFileToolCall(call);
        }

        function countDiffTotals(files) {
            return files.reduce((totals, file) => {
                file.lines.forEach(line => {
                    if (line.type === 'add') { totals.added += 1; }
                    if (line.type === 'del') { totals.removed += 1; }
                });
                return totals;
            }, { added: 0, removed: 0 });
        }

        function stripDiffLikeOutput(output) {
            const text = String(output || '').trim();
            if (!text) {
                return '';
            }

            if (text.includes('*** Begin Patch') || text.includes('diff --git') || /^@@\\s/m.test(text)) {
                return '';
            }

            return text;
        }

        function createInlineDisclosure(options) {
            const details = document.createElement('details');
            details.className = options.className || '';
            details.open = Boolean(options.open);

            const summary = document.createElement('summary');
            summary.className = 'inline-call-summary';

            const icon = document.createElement('span');
            icon.className = 'inline-call-icon';
            icon.textContent = options.icon || '›';
            summary.appendChild(icon);

            const title = document.createElement('span');
            title.className = 'inline-call-title';
            title.textContent = options.title || 'Details';
            title.title = options.title || 'Details';
            summary.appendChild(title);

            if (options.meta) {
                const meta = document.createElement('span');
                meta.className = 'inline-call-meta';
                meta.textContent = options.meta;
                summary.appendChild(meta);
            }

            const chevron = document.createElement('span');
            chevron.className = 'inline-call-chevron';
            chevron.textContent = '⌄';
            summary.appendChild(chevron);
            details.appendChild(summary);

            const content = document.createElement('div');
            content.className = 'inline-call-content';
            details.appendChild(content);
            return { details, content };
        }

        function summarizeLineCount(text) {
            const normalized = String(text || '').trim();
            if (!normalized) {
                return '';
            }

            const lineCount = normalized.split(/\\r?\\n/).filter(Boolean).length;
            return lineCount === 1 ? '1 line' : lineCount + ' lines';
        }

        function createDiffView(file) {
            const wrap = document.createElement('div');
            let added = 0;
            let removed = 0;
            file.lines.forEach(line => {
                if (line.type === 'add') { added += 1; }
                if (line.type === 'del') { removed += 1; }
            });
            wrap.className = 'diff-view' + (added > 0 && removed === 0 ? ' diff-created diff-add-only' : '') + (removed > 0 && added === 0 ? ' diff-deleted diff-del-only' : '');

            const header = document.createElement('div');
            header.className = 'diff-header';

            const headerMain = document.createElement('div');
            headerMain.className = 'diff-header-main';
            const pathLink = createFileReferenceLink(file.path, file.path);
            headerMain.appendChild(pathLink);
            header.appendChild(headerMain);

            const actions = document.createElement('div');
            actions.className = 'diff-header-actions';
            const stat = document.createElement('span');
            stat.className = 'diff-stat';
            stat.textContent = '+' + added + ' -' + removed;
            actions.appendChild(stat);
            const copyButton = document.createElement('button');
            copyButton.type = 'button';
            copyButton.className = 'diff-copy-button';
            copyButton.title = 'Copy diff';
            copyButton.setAttribute('aria-label', 'Copy diff');
            copyButton.textContent = '⧉';
            copyButton.addEventListener('click', event => {
                event.preventDefault();
                event.stopPropagation();
                copyTextToClipboard(formatDiffForCopy(file));
            });
            actions.appendChild(copyButton);
            header.appendChild(actions);
            wrap.appendChild(header);

            const pre = document.createElement('pre');
            pre.className = 'diff-body';
            const state = createDiffLineNumberState();
            file.lines.forEach(line => {
                const row = document.createElement('div');
                row.className = 'diff-line diff-' + line.type;

                const lineNumber = document.createElement('span');
                lineNumber.className = 'diff-line-number';
                lineNumber.textContent = getDiffLineNumber(line, state);
                row.appendChild(lineNumber);

                const sign = document.createElement('span');
                sign.className = 'diff-line-sign';
                sign.textContent = line.type === 'add' ? '+' : line.type === 'del' ? '-' : line.type === 'meta' ? '' : ' ';
                row.appendChild(sign);

                const code = document.createElement('span');
                code.className = 'diff-line-code';
                code.textContent = line.text;
                row.appendChild(code);

                pre.appendChild(row);
            });
            wrap.appendChild(pre);
            return wrap;
        }

        function createDiffLineNumberState() {
            return { oldLine: 1, newLine: 1 };
        }

        function getDiffLineNumber(line, state) {
            if (typeof line.newLine === 'number' && line.newLine > 0) {
                return String(line.newLine);
            }
            if (typeof line.lineNumber === 'number' && line.lineNumber > 0) {
                return String(line.lineNumber);
            }

            const text = String(line.text || '');
            const hunk = text.match(/@@\\s+-(\\d+)(?:,\\d+)?\\s+\\+(\\d+)(?:,\\d+)?\\s+@@/);
            if (hunk) {
                state.oldLine = Number(hunk[1]);
                state.newLine = Number(hunk[2]);
                return '';
            }

            if (line.type === 'add') {
                const value = state.newLine;
                state.newLine += 1;
                return String(value);
            }

            if (line.type === 'del') {
                const value = state.oldLine;
                state.oldLine += 1;
                return String(value);
            }

            if (line.type === 'context') {
                const value = state.newLine;
                state.oldLine += 1;
                state.newLine += 1;
                return String(value);
            }

            return '';
        }

        function formatDiffForCopy(file) {
            const lines = ['--- ' + file.path, '+++ ' + file.path];
            file.lines.forEach(line => {
                const sign = line.type === 'add' ? '+' : line.type === 'del' ? '-' : line.type === 'meta' ? '' : ' ';
                lines.push(sign + line.text);
            });
            return lines.join('\\n');
        }

        function copyTextToClipboard(text) {
            if (navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
                navigator.clipboard.writeText(text).catch(() => copyTextFallback(text));
                return;
            }

            copyTextFallback(text);
        }

        function copyTextFallback(text) {
            const textarea = document.createElement('textarea');
            textarea.value = text;
            textarea.setAttribute('readonly', 'true');
            textarea.style.position = 'fixed';
            textarea.style.opacity = '0';
            textarea.style.pointerEvents = 'none';
            document.body.appendChild(textarea);
            textarea.select();
            try {
                document.execCommand('copy');
            } catch (error) {
                // Best effort only.
            }
            textarea.remove();
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
            const output = Array.isArray(call.content) ? call.content.join('\\n').trim() : '';
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

        function showPluginPanel(marketplaces, installed) {
            if (activeModalRequest) {
                return;
            }

            const busy = promptState.isRunning === true;

            modalTitle.textContent = 'Plugins';
            modalDescription.hidden = false;
            modalDescription.textContent =
                marketplaces.length + (marketplaces.length === 1 ? ' marketplace · ' : ' marketplaces · ') +
                installed.length + (installed.length === 1 ? ' installed' : ' installed');
            modalBody.textContent = '';
            modalActions.textContent = '';

            const panel = document.createElement('div');
            panel.className = 'plugin-panel';
            modalBody.appendChild(panel);

            function runPlugin(text) {
                if (promptState.isRunning === true) {
                    return;
                }
                post({ command: 'pluginAction', text: text });
            }

            function sectionLabel(text) {
                const label = document.createElement('div');
                label.className = 'context-label';
                label.textContent = text;
                return label;
            }

            function infoOption(name, metaText) {
                const info = document.createElement('div');
                info.className = 'modal-option';
                const strong = document.createElement('strong');
                strong.textContent = name;
                info.appendChild(strong);
                const meta = document.createElement('span');
                meta.textContent = metaText;
                info.appendChild(meta);
                return info;
            }

            // Installed plugins (with uninstall buttons)
            const installedSection = document.createElement('div');
            installedSection.className = 'plugin-section';
            installedSection.appendChild(sectionLabel('Installed plugins'));
            if (installed.length === 0) {
                const empty = document.createElement('div');
                empty.className = 'empty-panel';
                empty.textContent = 'No plugins installed yet.';
                installedSection.appendChild(empty);
            } else {
                installed.forEach(plugin => {
                    const row = document.createElement('div');
                    row.className = 'plugin-row';

                    const parts = [];
                    if (plugin.repository) {
                        parts.push(plugin.repository + '@' + plugin.ref);
                    }
                    if (plugin.marketplaceAlias) {
                        parts.push('via ' + plugin.marketplaceAlias);
                    }
                    const fileCount = Array.isArray(plugin.files) ? plugin.files.length : 0;
                    parts.push(fileCount + (fileCount === 1 ? ' file' : ' files'));
                    row.appendChild(infoOption(plugin.pluginId, parts.join(' · ')));

                    const uninstall = document.createElement('button');
                    uninstall.className = 'danger-button';
                    uninstall.textContent = 'Uninstall';
                    uninstall.disabled = busy;
                    uninstall.addEventListener('click', () => runPlugin('/plugin uninstall ' + plugin.pluginId));
                    row.appendChild(uninstall);

                    installedSection.appendChild(row);
                });
            }
            panel.appendChild(installedSection);

            // Install a plugin (id + marketplace + force + button)
            const installSection = document.createElement('div');
            installSection.className = 'plugin-section';
            installSection.appendChild(sectionLabel('Install a plugin'));
            if (marketplaces.length === 0) {
                const note = document.createElement('div');
                note.className = 'empty-panel';
                note.textContent = 'Add a marketplace below before installing.';
                installSection.appendChild(note);
            } else {
                const idInput = document.createElement('input');
                idInput.className = 'modal-input';
                idInput.placeholder = 'plugin-id';
                idInput.disabled = busy;
                installSection.appendChild(idInput);

                const select = document.createElement('select');
                select.className = 'modal-input';
                select.disabled = busy;
                marketplaces.forEach(market => {
                    const option = document.createElement('option');
                    option.value = market.alias;
                    option.textContent = market.alias + ' (' + market.repository + ')';
                    select.appendChild(option);
                });
                installSection.appendChild(select);

                const forceLabel = document.createElement('label');
                forceLabel.className = 'plugin-check';
                const force = document.createElement('input');
                force.type = 'checkbox';
                force.disabled = busy;
                forceLabel.appendChild(force);
                forceLabel.appendChild(document.createTextNode(' Overwrite existing files (--force)'));
                installSection.appendChild(forceLabel);

                const installButton = document.createElement('button');
                installButton.className = 'primary-button';
                installButton.textContent = 'Install';
                installButton.disabled = busy;
                installButton.addEventListener('click', () => {
                    const id = idInput.value.trim();
                    if (!id) {
                        idInput.focus();
                        return;
                    }
                    runPlugin('/plugin install ' + id + '@' + select.value + (force.checked ? ' --force' : ''));
                });
                installSection.appendChild(installButton);
            }
            panel.appendChild(installSection);

            // Marketplaces (list + add form)
            const marketSection = document.createElement('div');
            marketSection.className = 'plugin-section';
            marketSection.appendChild(sectionLabel('Marketplaces'));
            if (marketplaces.length === 0) {
                const empty = document.createElement('div');
                empty.className = 'empty-panel';
                empty.textContent = 'No marketplaces configured.';
                marketSection.appendChild(empty);
            } else {
                marketplaces.forEach(market => {
                    const row = document.createElement('div');
                    row.className = 'plugin-row';
                    row.appendChild(infoOption(market.alias, market.repository + '@' + market.ref));

                    const browse = document.createElement('button');
                    browse.className = 'ghost-button';
                    browse.textContent = 'Browse';
                    browse.disabled = busy;
                    browse.title = 'List plugins this marketplace offers (output in chat)';
                    browse.addEventListener('click', () => {
                        if (promptState.isRunning === true) {
                            return;
                        }
                        post({ command: 'runSessionCommand', text: '/plugin browse ' + market.alias });
                        closeModal();
                    });
                    row.appendChild(browse);

                    const remove = document.createElement('button');
                    remove.className = 'danger-button';
                    remove.textContent = 'Remove';
                    remove.disabled = busy;
                    remove.addEventListener('click', () => runPlugin('/plugin marketplace remove ' + market.alias));
                    row.appendChild(remove);

                    marketSection.appendChild(row);
                });
            }

            const addForm = document.createElement('div');
            addForm.className = 'plugin-form';
            const repoInput = document.createElement('input');
            repoInput.className = 'modal-input';
            repoInput.placeholder = 'owner/repo';
            repoInput.disabled = busy;
            addForm.appendChild(repoInput);
            const addButton = document.createElement('button');
            addButton.className = 'ghost-button';
            addButton.textContent = 'Add';
            addButton.disabled = busy;
            addButton.addEventListener('click', () => {
                const repo = repoInput.value.trim();
                if (!repo) {
                    repoInput.focus();
                    return;
                }
                runPlugin('/plugin marketplace add ' + repo);
            });
            addForm.appendChild(addButton);
            marketSection.appendChild(addForm);
            panel.appendChild(marketSection);

            const refresh = document.createElement('button');
            refresh.className = 'ghost-button';
            refresh.textContent = 'Refresh';
            refresh.addEventListener('click', () => post({ command: 'loadPlugins' }));

            const close = document.createElement('button');
            close.className = 'ghost-button';
            close.textContent = 'Close';
            close.addEventListener('click', closeModal);

            modalActions.appendChild(refresh);
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
        api.postMessage({ command: 'webviewLog', level: 'info', message: 'Posting ready from main script' });
        post({ command: 'ready' });
    </script>
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
