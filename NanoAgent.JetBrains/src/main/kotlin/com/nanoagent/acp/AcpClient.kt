package com.nanoagent.acp

import com.google.gson.Gson
import com.google.gson.JsonElement
import com.google.gson.JsonParser
import com.intellij.openapi.diagnostic.Logger
import java.io.*
import java.util.concurrent.*
import java.util.concurrent.atomic.AtomicLong

/**
 * Client for the NanoAgent ACP (Agent Client Protocol).
 *
 * Communicates with `nanoai --acp` via JSON-RPC 2.0 over stdin/stdout.
 */
class AcpClient(
    private val nanoaiPath: String = "nanoai",
    private val backendArgs: List<String> = emptyList()
) : AutoCloseable {

    private val logger = Logger.getInstance(AcpClient::class.java)
    private val gson = Gson()
    private val requestIdCounter = AtomicLong(0)
    private val pendingRequests = ConcurrentHashMap<String, CompletableFuture<JsonElement>>()

    private var process: Process? = null
    private var writer: BufferedWriter? = null
    private var reader: BufferedReader? = null
    private var readerThread: Thread? = null
    private val shutdownLock = Any()
    private var shutdown = false

    // Session state
    private var currentSessionId: String? = null
    private var initialized = false
    var acpVersion: Int = 0
        private set
    var agentInfo: AcpAgentInfo? = null
        private set

    // Callbacks for notifications from the server
    var onAgentMessage: ((String) -> Unit)? = null
    var onReasoningMessage: ((String) -> Unit)? = null
    var onSessionInfo: ((AcpSessionInfo) -> Unit)? = null
    var onToolCall: ((AcpToolCallInfo) -> Unit)? = null
    var onToolCallUpdate: ((String, Boolean, String) -> Unit)? = null
    var onPlan: ((List<AcpPlanEntry>) -> Unit)? = null
    var onPermissionRequest: ((AcpPermissionRequest, (String) -> Unit) -> Unit)? = null
    var onTextPrompt: ((AcpTextPromptRequest, (String) -> Unit) -> Unit)? = null
    var onDisconnect: ((Throwable?) -> Unit)? = null

    /**
     * Start the NanoAgent process and perform the ACP initialize handshake.
     */
    fun start(workingDirectory: String? = null): CompletableFuture<AcpInitializeResult> {
        val future = CompletableFuture<AcpInitializeResult>()

        try {
            val command = buildList {
                add(nanoaiPath)
                add("--acp")
                addAll(backendArgs)
            }

            val pb = ProcessBuilder(command)
                .redirectErrorStream(false)

            if (workingDirectory != null) {
                pb.directory(File(workingDirectory))
            }

            process = pb.start()
            writer = process!!.outputStream.bufferedWriter()
            reader = process!!.inputStream.bufferedReader()
            val errorReader = process!!.errorStream.bufferedReader()

            // Read stderr in background
            Thread {
                try {
                    errorReader.lines().forEach { line ->
                        if (line.isNotBlank()) {
                            logger.warn("NanoAgent stderr: $line")
                        }
                    }
                } catch (_: IOException) {
                }
            }.also { it.isDaemon = true; it.start() }

            // Start response reader thread
            readerThread = Thread {
                try {
                    readResponses()
                } catch (e: IOException) {
                    if (!shutdown) {
                        logger.warn("ACP response reader stopped", e)
                        onDisconnect?.invoke(e)
                    }
                }
            }.also { it.isDaemon = true; it.start() }

            // Send initialize request
            sendRequest("initialize", mapOf("protocolVersion" to 1))
                .thenAccept { result ->
                    val initResult = gson.fromJson(result, AcpInitializeResult::class.java)
                    acpVersion = initResult.protocolVersion
                    agentInfo = initResult.agentInfo
                    initialized = true
                    future.complete(initResult)
                }
                .exceptionally { error ->
                    future.completeExceptionally(error)
                    null
                }

        } catch (e: Exception) {
            future.completeExceptionally(e)
        }

        return future
    }

    /**
     * Authenticate with the server (optional, based on auth methods).
     */
    fun authenticate(): CompletableFuture<JsonElement> {
        return sendRequest("authenticate", emptyMap())
    }

    /**
     * Create a new session in the given working directory.
     */
    fun createSession(cwd: String, mcpServers: List<Map<String, Any>>? = null): CompletableFuture<String> {
        val params = mutableMapOf<String, Any>("cwd" to cwd)
        mcpServers?.let { params["mcpServers"] = it }

        return sendRequest("session/new", params)
            .thenApply { result ->
                val sessionResult = gson.fromJson(result, AcpSessionNewResult::class.java)
                currentSessionId = sessionResult.sessionId
                sessionResult.sessionId
            }
    }

    /**
     * Load an existing session.
     */
    fun loadSession(cwd: String, sessionId: String, mcpServers: List<Map<String, Any>>? = null): CompletableFuture<Unit> {
        val params = mutableMapOf<String, Any>("cwd" to cwd, "sessionId" to sessionId)
        mcpServers?.let { params["mcpServers"] = it }

        return sendRequest("session/load", params)
            .thenApply {
                currentSessionId = sessionId
            }
    }

    /**
     * Send a text prompt to the current session.
     */
    fun sendPrompt(text: String, attachments: List<Map<String, Any>>? = null): CompletableFuture<String> {
        val sessionId = currentSessionId
            ?: return CompletableFuture.failedFuture(IllegalStateException("No active session"))

        val blocks = mutableListOf<Map<String, Any>>()
        blocks.add(mapOf("type" to "text", "text" to text))
        attachments?.forEach { blocks.add(it) }

        val params = mapOf<String, Any>(
            "sessionId" to sessionId,
            "prompt" to blocks
        )

        return sendRequest("session/prompt", params)
            .thenApply { result ->
                val promptResult = gson.fromJson(result, AcpSessionPromptResult::class.java)
                promptResult.stopReason
            }
    }

    /**
     * Close the current session.
     */
    fun closeSession(sessionId: String? = currentSessionId): CompletableFuture<JsonElement> {
        val sid = sessionId
            ?: return CompletableFuture.failedFuture(IllegalStateException("No active session"))

        val params = mapOf<String, Any>("sessionId" to sid)
        return sendRequest("session/close", params).whenComplete { _, _ ->
            if (sid == currentSessionId) {
                currentSessionId = null
            }
        }
    }

    /**
     * Cancel the current prompt in the given session.
     */
    fun cancelPrompt(sessionId: String) {
        sendNotification("session/cancel", mapOf("sessionId" to sessionId))
    }

    /**
     * Respond to a permission request from the server.
     */
    fun respondPermission(requestId: String, optionId: String) {
        val response = mapOf(
            "outcome" to mapOf(
                "outcome" to "selected",
                "optionId" to optionId
            )
        )
        completeClientRequest(requestId, gson.toJsonTree(response))
    }

    /**
     * Respond to a text prompt from the server.
     */
    fun respondText(requestId: String, value: String) {
        val response = mapOf(
            "outcome" to mapOf(
                "outcome" to "submitted",
                "value" to value
            )
        )
        completeClientRequest(requestId, gson.toJsonTree(response))
    }

    /**
     * Cancel a permission or text prompt from the server.
     */
    fun cancelClientRequest(requestId: String) {
        val response = mapOf(
            "outcome" to mapOf("outcome" to "cancelled")
        )
        completeClientRequest(requestId, gson.toJsonTree(response))
    }

    override fun close() {
        synchronized(shutdownLock) {
            if (shutdown) return
            shutdown = true
        }

        // Close all active sessions
        currentSessionId?.let { sid ->
            try {
                closeSession(sid).get(3, TimeUnit.SECONDS)
            } catch (_: Exception) {
            }
        }

        // Cancel all pending requests
        pendingRequests.values.forEach { it.completeExceptionally(CancellationException("ACP client shutting down")) }
        pendingRequests.clear()

        // Close process
        try {
            writer?.close()
        } catch (_: Exception) {
        }
        try {
            reader?.close()
        } catch (_: Exception) {
        }
        process?.destroyForcibly()
        process?.waitFor(3, TimeUnit.SECONDS)
    }

    private fun sendRequest(method: String, params: Map<String, Any>): CompletableFuture<JsonElement> {
        val id = requestIdCounter.incrementAndGet()
        val future = CompletableFuture<JsonElement>()
        pendingRequests["$id"] = future

        val request = mapOf(
            "jsonrpc" to "2.0",
            "id" to id,
            "method" to method,
            "params" to params
        )

        writeMessage(request)
        return future
    }

    private fun sendNotification(method: String, params: Map<String, Any>) {
        val notification = mapOf(
            "jsonrpc" to "2.0",
            "method" to method,
            "params" to params
        )
        writeMessage(notification)
    }

    private fun writeMessage(message: Map<String, Any>) {
        val json = gson.toJson(message)
        synchronized(writer!!) {
            writer!!.write(json)
            writer!!.newLine()
            writer!!.flush()
        }
    }

    private fun readResponses() {
        val reader = reader ?: return

        while (!shutdown) {
            val line = reader.readLine() ?: break
            if (line.isBlank()) continue

            try {
                val json = JsonParser.parseString(line).asJsonObject

                // Check if it's a response (has id and result/error, no method)
                val hasId = json.has("id") && !json.get("id").isJsonNull
                val hasMethod = json.has("method")
                val hasResult = json.has("result")
                val hasError = json.has("error")

                if (hasId && !hasMethod && (hasResult || hasError)) {
                    // This is a response to one of our requests
                    handleResponse(json)
                } else if (hasMethod) {
                    // This is a notification or client request from server
                    handleNotification(json)
                }
            } catch (e: Exception) {
                logger.warn("Failed to parse ACP message: $line", e)
            }
        }
    }

    private fun handleResponse(json: com.google.gson.JsonObject) {
        val id = json.get("id").asLong
        val future = pendingRequests.remove("$id")

        if (future == null) {
            logger.warn("Received response for unknown request id: $id")
            return
        }

        if (json.has("error")) {
            val error = json.getAsJsonObject("error")
            val code = error.get("code").asInt
            val message = error.get("message").asString
            future.completeExceptionally(RuntimeException("ACP error $code: $message"))
        } else {
            future.complete(json.get("result"))
        }
    }

    private fun handleNotification(json: com.google.gson.JsonObject) {
        val method = json.get("method").asString

        when (method) {
            "session/update" -> handleSessionUpdate(json)
            "session/request_permission" -> handlePermissionRequest(json)
            "session/request_text" -> handleTextRequest(json)
            else -> logger.debug("Unknown notification method: $method")
        }
    }

    private fun handleSessionUpdate(json: com.google.gson.JsonObject) {
        val params = json.getAsJsonObject("params")
        val update = params.getAsJsonObject("update")
        val updateKind = update.get("sessionUpdate").asString

        when (SessionUpdateKind.fromValue(updateKind)) {
            SessionUpdateKind.AGENT_MESSAGE_CHUNK -> {
                val content = update.getAsJsonObject("content")
                val text = content.get("text").asString
                if (isReasoningContent(content)) {
                    onReasoningMessage?.invoke(text)
                } else {
                    onAgentMessage?.invoke(text)
                }
            }
            SessionUpdateKind.AGENT_REASONING_CHUNK,
            SessionUpdateKind.REASONING_MESSAGE_CHUNK,
            SessionUpdateKind.THINKING_MESSAGE_CHUNK -> {
                val content = update.getAsJsonObject("content")
                val text = content.get("text").asString
                onReasoningMessage?.invoke(text)
            }
            SessionUpdateKind.USER_MESSAGE_CHUNK -> {
                val content = update.getAsJsonObject("content")
                val text = content.get("text").asString
                onAgentMessage?.invoke(text)
            }
            SessionUpdateKind.SESSION_INFO_UPDATE -> {
                try {
                    val info = gson.fromJson(update, AcpSessionInfo::class.java)
                    onSessionInfo?.invoke(info)
                } catch (e: Exception) {
                    logger.warn("Failed to parse session info update", e)
                }
            }
            SessionUpdateKind.TOOL_CALL -> {
                try {
                    val toolCall = gson.fromJson(update, AcpToolCallInfo::class.java)
                    onToolCall?.invoke(toolCall)
                } catch (e: Exception) {
                    logger.warn("Failed to parse tool call update", e)
                }
            }
            SessionUpdateKind.TOOL_CALL_UPDATE -> {
                val toolCallId = update.get("toolCallId").asString
                val status = update.get("status").asString
                val success = status == "completed"
                val content = if (update.has("content")) {
                    val contentArray = update.getAsJsonArray("content")
                    if (contentArray.size() > 0) {
                        val first = contentArray[0].asJsonObject
                        if (first.has("content")) {
                            first.getAsJsonObject("content").get("text")?.asString ?: ""
                        } else ""
                    } else ""
                } else ""
                onToolCallUpdate?.invoke(toolCallId, success, content)
            }
            SessionUpdateKind.PLAN -> {
                try {
                    val entries = update.getAsJsonArray("entries")
                        .map { gson.fromJson(it, AcpPlanEntry::class.java) }
                    onPlan?.invoke(entries)
                } catch (e: Exception) {
                    logger.warn("Failed to parse plan update", e)
                }
            }
            null -> logger.debug("Unknown session update kind: $updateKind")
        }
    }

    private fun isReasoningContent(content: com.google.gson.JsonObject): Boolean {
        val typeElement = content.get("type") ?: return false
        if (!typeElement.isJsonPrimitive) {
            return false
        }

        val type = typeElement.asString.trim().lowercase()
        return type == "reasoning" || type == "thinking" || type == "thought"
    }

    private fun handlePermissionRequest(json: com.google.gson.JsonObject) {
        val params = json.getAsJsonObject("params")
        val requestId = json.get("id").asString

        try {
            val permissionRequest = gson.fromJson(params, AcpPermissionRequest::class.java)
            onPermissionRequest?.invoke(permissionRequest) { optionId ->
                respondPermission(requestId, optionId)
            }
        } catch (e: Exception) {
            logger.warn("Failed to parse permission request", e)
            cancelClientRequest(requestId)
        }
    }

    private fun handleTextRequest(json: com.google.gson.JsonObject) {
        val params = json.getAsJsonObject("params")
        val requestId = json.get("id").asString

        try {
            val textRequest = gson.fromJson(params, AcpTextPromptRequest::class.java)
            onTextPrompt?.invoke(textRequest) { value ->
                respondText(requestId, value)
            }
        } catch (e: Exception) {
            logger.warn("Failed to parse text request", e)
            cancelClientRequest(requestId)
        }
    }

    private fun completeClientRequest(requestId: String, result: JsonElement) {
        val response = mapOf(
            "jsonrpc" to "2.0",
            "id" to requestId,
            "result" to result
        )
        writeMessage(response)
    }

    val isActive: Boolean
        get() = !shutdown && process?.isAlive == true
}
