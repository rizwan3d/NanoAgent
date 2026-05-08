package com.nanoagent.services

import com.intellij.openapi.diagnostic.Logger
import com.nanoagent.acp.*
import java.util.concurrent.CompletableFuture
import java.util.concurrent.ConcurrentHashMap

/**
 * Manages ACP sessions, streaming responses, tool calls, plans, and client requests.
 *
 * Mirrors VS Code's SessionManager with full notification handling,
 * message chunk streaming, tool call updates, plan tracking, and
 * permission/text request support.
 */
class SessionManager(private val processManager: NanoAgentProcessManager) {
    private val logger = Logger.getInstance(SessionManager::class.java)

    private var acpClient: AcpClient? = null
    private var sessionId: String? = null
    private var currentSessionInfo: AcpSessionInfo? = null
    private val pendingClientRequests = ConcurrentHashMap<String, PendingClientRequest>()
    private var promptState = PromptState()
    private var currentSessionFuture: CompletableFuture<String>? = null

    // Callbacks
    var onProcessStatusChanged: ((ProcessStatus) -> Unit)? = null
    var onPromptStateChanged: ((PromptState) -> Unit)? = null
    var onSessionInfoChanged: ((AcpSessionInfo?) -> Unit)? = null
    var onMessageChunk: ((SessionMessageChunk) -> Unit)? = null
    var onToolCallUpdate: ((ToolCallInfo) -> Unit)? = null
    var onPlanUpdate: ((List<AcpPlanEntry>) -> Unit)? = null
    var onClientRequest: ((ClientRequest) -> Unit)? = null
    var onClientRequestResolved: ((String) -> Unit)? = null

    init {
        processManager.onStatusChanged = { status ->
            onProcessStatusChanged?.invoke(status)
            when (status) {
                ProcessStatus.RUNNING -> initializeSession()
                ProcessStatus.STOPPED, ProcessStatus.ERROR -> terminateSession()
                else -> {}
            }
        }

        if (processManager.isRunning()) {
            initializeSession()
        }
    }

    fun getSessionInfo(): AcpSessionInfo? = currentSessionInfo
    fun getPromptState(): PromptState = promptState
    fun getPendingClientRequests(): List<ClientRequest> =
        pendingClientRequests.values.map { it.request }

    /**
     * Ensure the ACP process is started and the client is initialized.
     */
    fun ensureStarted(): CompletableFuture<Unit> {
        return if (processManager.isRunning()) {
            CompletableFuture.completedFuture(Unit)
        } else {
            processManager.start().thenAccept { initializeSession() }
        }
    }

    /**
     * Ensure a session is ready (process + client + session).
     */
    fun ensureSessionReady(): CompletableFuture<*> {
        return ensureStarted()
            .thenCompose { ensureInitialized() }
            .thenCompose { ensureSessionCreated() }
    }

    /**
     * Send a prompt to the current session.
     */
    fun sendPrompt(text: String): CompletableFuture<String?> {
        val client = acpClient ?: return CompletableFuture.failedFuture(
            IllegalStateException("NanoAgent ACP client is not initialized.")
        )

        setPromptState(PromptState(isRunning = true, input = text))

        return ensureSessionCreated()
            .thenCompose { sid ->
                client.sendPrompt(text)
                    .thenApply { stopReason ->
                        setPromptState(PromptState(
                            isRunning = false,
                            lastStopReason = stopReason
                        ))
                        stopReason
                    }
            }
            .exceptionally { error ->
                val normalized = normalizeError(error)
                setPromptState(PromptState(
                    isRunning = false,
                    error = normalized.message
                ))
                throw normalized
            }
    }

    /**
     * Cancel the current prompt.
     */
    fun cancelPrompt() {
        val client = acpClient ?: return
        val sid = sessionId ?: return

        if (promptState.isRunning) {
            setPromptState(promptState.copy(isCancelling = true))
        }

        client.cancelPrompt(sid)
    }

    /**
     * Resolve a client permission/text request.
     */
    fun resolveClientRequest(id: String, resolution: ClientRequestResolution): Boolean {
        val pending = pendingClientRequests[id] ?: return false
        pending.resolve(resolution)
        pendingClientRequests.remove(id)
        onClientRequestResolved?.invoke(id)
        return true
    }

    // ---- Private implementation ----

    private fun initializeSession() {
        if (acpClient != null) return

        val client = processManager.getClient() ?: run {
            logger.error("Cannot initialize session: process client is not available")
            return
        }

        acpClient = client

        // Wire up ACP client callbacks
        client.onAgentMessage = { text ->
            onMessageChunk?.invoke(SessionMessageChunk(
                sessionId = sessionId ?: "",
                role = "assistant",
                text = text
            ))
        }

        client.onReasoningMessage = { text ->
            onMessageChunk?.invoke(SessionMessageChunk(
                sessionId = sessionId ?: "",
                role = "reasoning",
                text = text
            ))
        }

        client.onSessionInfo = { info ->
            currentSessionInfo = info
            onSessionInfoChanged?.invoke(info)
        }

        client.onToolCall = { call ->
            onToolCallUpdate?.invoke(ToolCallInfo(
                toolCallId = call.toolCallId ?: "",
                title = call.title,
                kind = call.kind,
                status = call.status ?: "pending",
                rawInput = call.rawInput,
                content = call.content?.mapNotNull { it.content?.text } ?: emptyList()
            ))
        }

        client.onToolCallUpdate = { id, success, content ->
            onToolCallUpdate?.invoke(ToolCallInfo(
                toolCallId = id,
                status = if (success) "completed" else "failed",
                content = if (content.isNotBlank()) listOf(content) else emptyList()
            ))
        }

        client.onPlan = { entries ->
            onPlanUpdate?.invoke(entries)
        }

        client.onPermissionRequest = { request, responder ->
            handlePermissionRequest(request, responder)
        }

        client.onTextPrompt = { request, responder ->
            handleTextRequest(request, responder)
        }

        client.onDisconnect = { error ->
            logger.warn("ACP client disconnected", error)
            terminateSession()
        }

        logger.info("ACP Session initialized")
    }

    private fun terminateSession() {
        acpClient?.let { client ->
            client.onAgentMessage = null
            client.onReasoningMessage = null
            client.onSessionInfo = null
            client.onToolCall = null
            client.onToolCallUpdate = null
            client.onPlan = null
            client.onPermissionRequest = null
            client.onTextPrompt = null
            client.onDisconnect = null
        }

        acpClient = null
        currentSessionInfo = null
        sessionId = null
        currentSessionFuture = null

        // Reject all pending client requests
        val error = RuntimeException("NanoAgent process stopped.")
        pendingClientRequests.values.forEach { it.reject(error) }
        pendingClientRequests.clear()

        setPromptState(PromptState())
        onSessionInfoChanged?.invoke(null)
        logger.info("ACP Session terminated")
    }

    private fun ensureInitialized(): CompletableFuture<*> {
        val client = acpClient ?: return CompletableFuture.failedFuture(
            IllegalStateException("ACP client not initialized.")
        )
        return client.start() // No-op if already started
    }

    private fun ensureSessionCreated(): CompletableFuture<String> {
        if (sessionId != null) {
            return CompletableFuture.completedFuture(sessionId!!)
        }

        if (currentSessionFuture != null) {
            return currentSessionFuture!!
        }

        val client = acpClient ?: return CompletableFuture.failedFuture(
            IllegalStateException("ACP client not initialized.")
        )

        currentSessionFuture = client.createSession(
            cwd = System.getProperty("user.dir") // Will be overridden by caller
        ).thenApply { sid ->
            sessionId = sid
            sid
        }.whenComplete { _, _ ->
            currentSessionFuture = null
        }

        return currentSessionFuture!!
    }

    private fun handlePermissionRequest(
        request: AcpPermissionRequest,
        responder: (String) -> Unit
    ) {
        val id = "perm-${System.nanoTime()}"
        val options = request.options.map { option ->
            PermissionOption(
                optionId = option.optionId,
                name = option.name,
                kind = option.kind
            )
        }

        val clientRequest = ClientRequest(
            id = id,
            kind = "permission",
            title = request.toolCall.title,
            description = request.toolCall.content
                ?.mapNotNull { it.content?.text }
                ?.joinToString("\n"),
            options = options,
            allowCancellation = true
        )

        val future = CompletableFuture<ClientRequestResolution>()
        pendingClientRequests[id] = PendingClientRequest(clientRequest, future)

        future.thenAccept { resolution ->
            when (resolution.outcome) {
                "selected" -> responder(resolution.optionId!!)
                "cancelled" -> responder("deny")
            }
        }

        onClientRequest?.invoke(clientRequest)
    }

    private fun handleTextRequest(
        request: AcpTextPromptRequest,
        responder: (String) -> Unit
    ) {
        val id = "text-${System.nanoTime()}"

        val clientRequest = ClientRequest(
            id = id,
            kind = "text",
            title = request.label,
            description = request.description,
            defaultValue = request.defaultValue,
            isSecret = request.secretFlag,
            allowCancellation = request.allowCancellation
        )

        val future = CompletableFuture<ClientRequestResolution>()
        pendingClientRequests[id] = PendingClientRequest(clientRequest, future)

        future.thenAccept { resolution ->
            when (resolution.outcome) {
                "submitted" -> responder(resolution.value!!)
                "cancelled" -> responder("")
            }
        }

        onClientRequest?.invoke(clientRequest)
    }

    private fun setPromptState(state: PromptState) {
        promptState = state
        onPromptStateChanged?.invoke(state)
    }

    private fun normalizeError(error: Throwable): Throwable {
        return error
    }
}

// ---- Data classes matching VS Code types ----

data class PromptState(
    val isRunning: Boolean = false,
    val isCancelling: Boolean = false,
    val input: String? = null,
    val lastStopReason: String? = null,
    val error: String? = null
)

data class SessionMessageChunk(
    val sessionId: String,
    val role: String,
    val text: String
)

data class ToolCallInfo(
    val toolCallId: String,
    val title: String? = null,
    val kind: String? = null,
    val status: String = "pending",
    val rawInput: Any? = null,
    val content: List<String> = emptyList()
)

data class ClientRequest(
    val id: String,
    val kind: String,
    val title: String?,
    val description: String? = null,
    val options: List<PermissionOption> = emptyList(),
    val defaultValue: String? = null,
    val isSecret: Boolean = false,
    val allowCancellation: Boolean = true,
    val defaultOptionId: String? = null,
    val autoSelectAfterMilliseconds: Long? = null
)

data class PermissionOption(
    val optionId: String,
    val name: String,
    val kind: String
)

sealed class ClientRequestResolution {
    data class Selected(val optionId: String) : ClientRequestResolution()
    data class Submitted(val value: String) : ClientRequestResolution()
    object Cancelled : ClientRequestResolution()
}

private data class PendingClientRequest(
    val request: ClientRequest,
    val resolve: (ClientRequestResolution) -> Unit,
    val reject: (Throwable) -> Unit = {}
)
