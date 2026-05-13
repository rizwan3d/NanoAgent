package com.nanoagent.acp

import com.google.gson.JsonElement
import com.google.gson.annotations.SerializedName

data class JsonRpcRequest(
    @SerializedName("jsonrpc") val jsonrpc: String = "2.0",
    val id: Any,
    val method: String,
    val params: Map<String, @JvmSuppressWildcards Any>? = null
)

data class JsonRpcResponse(
    @SerializedName("jsonrpc") val jsonrpc: String? = null,
    val id: Any? = null,
    val result: JsonElement? = null,
    val error: JsonRpcError? = null
)

data class JsonRpcError(
    val code: Int,
    val message: String,
    val data: JsonElement? = null
)

data class JsonRpcNotification(
    @SerializedName("jsonrpc") val jsonrpc: String? = null,
    val method: String,
    val params: Map<String, @JvmSuppressWildcards Any>? = null
)

data class AcpInitializeResult(
    val protocolVersion: Int,
    val agentCapabilities: AcpAgentCapabilities,
    val agentInfo: AcpAgentInfo,
    val authMethods: List<String>
)

data class AcpAgentCapabilities(
    val loadSession: Boolean,
    val promptCapabilities: AcpPromptCapabilities,
    val mcpCapabilities: AcpMcpCapabilities,
    val sessionCapabilities: AcpSessionCapabilities
)

data class AcpPromptCapabilities(
    val image: Boolean,
    val audio: Boolean,
    val embeddedContext: Boolean
)

data class AcpMcpCapabilities(
    val http: Boolean,
    val sse: Boolean
)

data class AcpSessionCapabilities(
    val close: Map<String, Any>
)

data class AcpAgentInfo(
    val name: String,
    val title: String,
    val version: String
)

data class AcpSessionNewResult(
    val sessionId: String
)

data class AcpSessionPromptResult(
    val stopReason: String
)

enum class SessionUpdateKind(val value: String) {
    SESSION_INFO_UPDATE("session_info_update"),
    AGENT_MESSAGE_CHUNK("agent_message_chunk"),
    AGENT_REASONING_CHUNK("agent_reasoning_chunk"),
    REASONING_MESSAGE_CHUNK("reasoning_message_chunk"),
    THINKING_MESSAGE_CHUNK("thinking_message_chunk"),
    USER_MESSAGE_CHUNK("user_message_chunk"),
    TOOL_CALL("tool_call"),
    TOOL_CALL_UPDATE("tool_call_update"),
    PLAN("plan");

    companion object {
        fun fromValue(value: String): SessionUpdateKind? =
            entries.firstOrNull { it.value == value }
    }
}

data class AgentMessageChunk(
    val type: String,
    val text: String
)

data class AcpSessionInfo(
    val sessionId: String,
    val sectionResumeCommand: String?,
    val providerName: String?,
    val modelId: String?,
    val activeModelContextWindowTokens: Int?,
    val availableModelIds: List<String>?,
    val availableAgentProfiles: List<String>?,
    val thinkingMode: String?,
    val agentProfileName: String?,
    val sectionTitle: String?
)

data class AcpToolCallInfo(
    val toolCallId: String?,
    val title: String?,
    val kind: String?,
    val status: String?,
    val rawInput: JsonElement? = null,
    val content: List<AcpToolCallContent>? = null
)

data class AcpToolCallContent(
    val type: String,
    val content: AcpContentItem? = null
)

data class AcpContentItem(
    val type: String,
    val text: String? = null
)

data class AcpPlanEntry(
    val content: String,
    val priority: String,
    val status: String
)

data class AcpPermissionRequest(
    val sessionId: String,
    val toolCall: AcpToolCallPermissionInfo,
    val options: List<AcpPermissionOption>
)

data class AcpToolCallPermissionInfo(
    val toolCallId: String,
    val title: String,
    val kind: String,
    val status: String,
    val content: List<AcpToolCallContent>? = null
)

data class AcpPermissionOption(
    val optionId: String,
    val name: String,
    val kind: String
)

data class AcpPermissionResponse(
    val outcome: AcpPermissionOutcome
)

data class AcpPermissionOutcome(
    val outcome: String,
    val optionId: String? = null
)

data class AcpTextPromptRequest(
    val sessionId: String,
    val label: String,
    val description: String? = null,
    val defaultValue: String? = null,
    @SerializedName("isSecret") val secretFlag: Boolean = false,
    val allowCancellation: Boolean = true
)

data class AcpTextPromptResponse(
    val outcome: AcpTextPromptOutcome
)

data class AcpTextPromptOutcome(
    val outcome: String,
    val value: String? = null
)

data class PromptBlock(
    val type: String,
    val text: String? = null,
    val data: String? = null,
    val mimeType: String? = null,
    val uri: String? = null,
    val title: String? = null,
    val name: String? = null,
    val resource: PromptResourceBlock? = null
)

data class PromptResourceBlock(
    val uri: String? = null,
    val mimeType: String? = null,
    val text: String? = null,
    val blob: String? = null
)
