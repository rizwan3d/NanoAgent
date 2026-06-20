import { buildDiffModel } from './diffModel';
import { CHAT_COMMANDS } from './chatCommands';

declare function acquireVsCodeApi(): { postMessage(message: unknown): void; getState(): unknown; setState(state: unknown): void };

const api = acquireVsCodeApi();
(window as unknown as Record<string, unknown>).__nanoAgentVsCodeApi = api;

window.addEventListener('error', event => {
    api.postMessage({
        command: 'webviewLog',
        level: 'error',
        message: 'Window error event',
        details: event && event.error && event.error.stack ? String(event.error.stack) : String(event && event.message ? event.message : 'Unknown error')
    });
});

window.addEventListener('unhandledrejection', event => {
    const reason = event ? event.reason : undefined;
    api.postMessage({
        command: 'webviewLog',
        level: 'error',
        message: 'Unhandled promise rejection',
        details: reason && reason.stack ? String(reason.stack) : String(reason ?? 'Unknown rejection')
    });
});

api.postMessage({ command: 'webviewLog', level: 'info', message: 'Main script starting' });
const commandSuggestions = CHAT_COMMANDS;
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

        const fileReferencePattern = /(^|[\s("'<>\[])([A-Za-z]:[\\/][^\s"'<>|]+|(?:\.{1,2}[\\/])?(?:(?:[^\s"'<>:|\\/]+)[\\/])+[^\s"'<>:|\\/]+\.[A-Za-z][A-Za-z0-9]{0,15}|[^\s"'<>:|\\/]+\.[A-Za-z][A-Za-z0-9]{0,15})(?::(\d{1,7})(?::(\d{1,5}))?)?/g;

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

                const heading = line.match(/^(#{1,6})\s+(.+)$/);
                if (heading) {
                    const level = Math.min(6, heading[1].length);
                    const element = document.createElement('h' + level);
                    appendMarkdownInline(element, heading[2].replace(/\s+#+\s*$/, ''));
                    container.appendChild(element);
                    index += 1;
                    continue;
                }

                if (/^\s*>\s?/.test(line)) {
                    const quoteLines = [];
                    while (index < lines.length && /^\s*>\s?/.test(lines[index])) {
                        quoteLines.push(lines[index].replace(/^\s*>\s?/, ''));
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
                    if (getMarkdownFence(current) || /^(#{1,6})\s+/.test(current) || /^\s*>\s?/.test(current) || getMarkdownListInfo(current)) {
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
                return { marker: backtickFence, language: trimmed.slice(backtickFence.length).trim().split(/\s+/)[0] || '' };
            }
            if (trimmed.startsWith('~~~')) {
                return { marker: '~~~', language: trimmed.slice(3).trim().split(/\s+/)[0] || '' };
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
            const unordered = String(line || '').match(/^\s*[-+*]\s+(.+)$/);
            if (unordered) {
                return { ordered: false, text: unordered[1] };
            }
            const ordered = String(line || '').match(/^\s*\d+[.)]\s+(.+)$/);
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
            return String(value || '').replace(/[),.;\]}]+$/, '');
        }

        function isLikelyFileReference(candidate, sourceText, startIndex) {
            if (!candidate || candidate.length > 320) {
                return false;
            }

            const before = sourceText.slice(Math.max(0, startIndex - 8), startIndex).toLowerCase();
            if (before.includes('://')) {
                return false;
            }

            return candidate.includes('.') || candidate.includes('/') || candidate.includes('\\');
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

            const output = Array.isArray(call.content) ? call.content.join('\n') : '';
            if (output) {
                lines.push('');
                lines.push('Output');
                lines.push(output);
            }

            return lines.join('\n');
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

            const output = Array.isArray(call.content) ? call.content.join('\n') : '';
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
            const text = String(output || '').replace(/\r\n/g, '\n');
            const trimmed = text.trim();
            if (!trimmed) {
                return null;
            }

            const backtickFence = String.fromCharCode(96).repeat(3);
            const exactFence = readDelimitedCodeFence(trimmed, backtickFence) || readDelimitedCodeFence(trimmed, '~~~');
            if (exactFence) {
                return exactFence;
            }

            const labelMatch = trimmed.match(/^(?:read|opened|contents?|content|file|output)[^\n]*:\s*\n([\s\S]+)$/i);
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

            const firstLineEnd = value.indexOf('\n');
            if (firstLineEnd < 0) {
                return null;
            }

            const firstLine = value.slice(marker.length, firstLineEnd).trim();
            const rest = value.slice(firstLineEnd + 1);
            const closing = '\n' + marker;
            const closingIndex = rest.lastIndexOf(closing);
            if (closingIndex < 0 || rest.slice(closingIndex + closing.length).trim()) {
                return null;
            }

            return {
                language: normalizeLanguageName(firstLine.split(/\s+/)[0] || ''),
                content: rest.slice(0, closingIndex)
            };
        }

        function inferReadOutputPath(output) {
            const text = String(output || '');
            const patterns = [
                /(?:read|opened|contents? of|file|path):\s*([^\n]+)/i,
                /^\s*[-•]?\s*file:\s+([^\n]+)/im
            ];

            for (const pattern of patterns) {
                const match = text.match(pattern);
                if (match) {
                    const path = trimFileReference(match[1].trim());
                    if (path && isLikelyFileReference(path, path, 0)) {
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
            return /(^|\b)(list|ls|tree|directory|workspace files|files)(\b|$)/.test(haystack);
        }

        function looksLikeDirectoryListOutput(output) {
            const text = String(output || '');
            return /(^|\n)\s*[-•]\s*(file|directory):\s+/i.test(text)
                || /(^|\n)\s*[-•]?\s*listed\b.*\(\d+\s+entr(?:y|ies)\)/i.test(text);
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

            if (/\b(list|ls|tree|search|grep|find|scan|glob)\b/.test(haystack)) {
                return false;
            }

            if (/\b(read_file|read-file|readfile|fs_read|fs\.read|read_file_system|filesystem[._:-]read|filesystem.*readfile)\b/.test(haystack)) {
                return true;
            }

            if (/\b(read|open|cat|show|view)\s+(?:a\s+)?(?:workspace\s+)?file\b/.test(haystack)) {
                return true;
            }

            if (/\bcat\s+[^\n]+/.test(haystack) && hasLikelyToolFilePath(call)) {
                return true;
            }

            return /\bread\b/.test(haystack) && hasLikelyToolFilePath(call);
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
            const entryMatch = text.match(/^(\s*[-•]\s*)(directory|file):\s+(.+)$/i);
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

            const summaryMatch = text.match(/^(\s*[-•]?\s*)(listed|found|matched|created|updated|deleted|read|wrote)(\b.*)$/i);
            if (summaryMatch) {
                appendSyntaxSpan(container, summaryMatch[1], 'syntax-muted');
                appendSyntaxSpan(container, summaryMatch[2], 'syntax-keyword');
                appendLinkifiedText(container, summaryMatch[3]);
                return;
            }

            const moreMatch = text.match(/^(\s*\.{3}\s*\+\d+\s+.+)$/);
            if (moreMatch) {
                appendSyntaxSpan(container, moreMatch[1], 'syntax-muted');
                return;
            }

            appendLinkifiedText(container, text);
        }

        function splitCodeLines(text) {
            const lines = String(text || '').replace(/\r\n/g, '\n').split('\n');
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
            const headingMatch = text.match(/^(\s{0,3}#{1,6}\s+)(.*)$/);
            if (headingMatch) {
                appendSyntaxSpan(container, headingMatch[1], 'syntax-keyword');
                appendSyntaxCodeTokens(container, headingMatch[2], 'markdown');
                return;
            }

            const quoteMatch = text.match(/^(\s*>+\s?)(.*)$/);
            if (quoteMatch) {
                appendSyntaxSpan(container, quoteMatch[1], 'syntax-comment');
                appendSyntaxCodeTokens(container, quoteMatch[2], 'markdown');
                return;
            }

            const listMatch = text.match(/^(\s*)([-*+]\s+|\d+\.\s+)(.*)$/);
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
            const tokenPattern = /("(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|\b[A-Za-z_$][\w$]*\b|\b\d+(?:\.\d+)?\b)/g;
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

            if (/^\d/.test(token)) {
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

            if (/^\s*\(/.test(tail || '')) {
                return 'syntax-function';
            }

            if (/^\s*:/.test(tail || '') || /^\s*=/.test(tail || '')) {
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
            const output = Array.isArray(call && call.content) ? call.content.join('\n') : '';
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
            String(text || '').split(/\r?\n/).forEach(line => {
                const fileMatch = line.match(/^\*\*\*\s+(Add|Update|Delete) File:\s+(.+)$/);
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
            String(text || '').split(/\r?\n/).forEach(line => {
                const gitMatch = line.match(/^diff --git\s+a\/(.+?)\s+b\/(.+)$/);
                if (gitMatch) {
                    current = { path: gitMatch[2].trim(), lines: [] };
                    files.push(current);
                    return;
                }

                const newFileMatch = line.match(/^\+\+\+\s+(?:b\/)?(.+)$/);
                if (newFileMatch && !line.includes('/dev/null')) {
                    if (!current) {
                        current = { path: newFileMatch[1].trim(), lines: [] };
                        files.push(current);
                    } else {
                        current.path = newFileMatch[1].trim();
                    }
                    return;
                }

                const oldFileMatch = line.match(/^---\s+(?:a\/)?(.+)$/);
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
            const lines = String(content || '').replace(/\r\n/g, '\n').split('\n');
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

            if (text.includes('*** Begin Patch') || text.includes('diff --git') || /^@@\s/m.test(text)) {
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

            const lineCount = normalized.split(/\r?\n/).filter(Boolean).length;
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
            const hunk = text.match(/@@\s+-(\d+)(?:,\d+)?\s+\+(\d+)(?:,\d+)?\s+@@/);
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
            return lines.join('\n');
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
            const match = /(^|\s)@([^\s@]*)$/.exec(before);
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

            const token = value.split(/\s+/, 1)[0].toLowerCase();
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
                const before = inputField.value.slice(0, caret).replace(/@[^\s@]*$/, suggestion.insertText + ' ');
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
