package com.nanoagent.ui

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.project.Project
import com.intellij.ui.JBColor
import com.intellij.ui.components.JBScrollPane
import com.intellij.util.ui.JBUI
import com.intellij.util.ui.components.BorderLayoutPanel
import com.nanoagent.NanoAgentPlugin
import com.nanoagent.acp.*
import java.awt.BorderLayout
import java.awt.Dimension
import java.io.File
import javax.swing.*
import javax.swing.text.*

/**
 * Chat panel for interacting with NanoAgent.
 */
class ChatPanel(private val project: Project) : BorderLayoutPanel() {

    private val logger = Logger.getInstance(ChatPanel::class.java)
    private val agent = NanoAgentPlugin.getInstance()
    private var client: AcpClient? = null
    private var sessionId: String? = null
    private var connected = false

    // UI Components
    private val chatHistory = JEditorPane()
    private val chatScrollPane = JBScrollPane(chatHistory)
    private val inputField = JTextPane()
    private val sendButton = JButton("Send")
    private val connectButton = JButton("Connect")
    private val statusLabel = JLabel("Disconnected")
    private val inputContainer = BorderLayoutPanel()

    init {
        layout = BorderLayout()
        buildUI()
    }

    private fun buildUI() {
        // Chat history
        chatHistory.contentType = "text/html"
        chatHistory.isEditable = false
        chatHistory.background = JBColor.background()
        chatHistory.putClientProperty(JEditorPane.HONOR_DISPLAY_PROPERTIES, true)

        // Input area
        inputField.preferredSize = Dimension(Int.MAX_VALUE, 60)
        inputField.minimumSize = Dimension(0, 40)
        inputField.addKeyListener(object : java.awt.event.KeyAdapter() {
            override fun keyPressed(e: java.awt.event.KeyEvent) {
                if (e.keyCode == java.awt.event.KeyEvent.VK_ENTER) {
                    if (e.isControlDown || e.isShiftDown) {
                        // Allow multi-line with Ctrl+Enter
                    } else {
                        e.consume()
                        sendMessage()
                    }
                }
            }
        })

        // Send button
        sendButton.isEnabled = false
        sendButton.addActionListener { sendMessage() }

        // Connect button
        connectButton.addActionListener { toggleConnection() }

        // Status label
        statusLabel.border = JBUI.Borders.empty(4, 8)

        // Top toolbar
        val toolbar = JPanel(BorderLayout())
        toolbar.add(connectButton, BorderLayout.WEST)
        toolbar.add(statusLabel, BorderLayout.CENTER)

        // Input panel
        inputContainer.addToCenter(inputField)
        inputContainer.addToRight(sendButton)
        inputContainer.border = JBUI.Borders.empty(4)

        // Assemble
        addToTop(toolbar)
        addToCenter(chatScrollPane)
        addToBottom(inputContainer)

        // Initial state
        updateConnectionUI(false)

        appendSystemMessage("Welcome to NanoAgent! Click Connect to start.")
    }

    private fun toggleConnection() {
        if (connected) {
            disconnect()
        } else {
            connect()
        }
    }

    private fun connect() {
        connectButton.isEnabled = false
        connectButton.text = "Connecting..."

        ApplicationManager.getApplication().executeOnPooledThread {
            try {
                val newClient = agent.createClient()
                val projectDir = project.basePath ?: System.getProperty("user.dir")

                newClient.onAgentMessage = { text ->
                    SwingUtilities.invokeLater { appendAgentMessage(text) }
                }
                newClient.onReasoningMessage = { text ->
                    SwingUtilities.invokeLater { appendReasoningMessage(text) }
                }
                newClient.onSessionInfo = { info ->
                    SwingUtilities.invokeLater {
                        sessionId = info.sessionId
                        appendSystemMessage("Session: ${info.sessionId}")
                    }
                }
                newClient.onToolCall = { call ->
                    SwingUtilities.invokeLater {
                        appendSystemMessage("Tool: ${call.title ?: call.toolCallId} (${call.status})")
                    }
                }
                newClient.onToolCallUpdate = { id, success, content ->
                    SwingUtilities.invokeLater {
                        val status = if (success) "completed" else "failed"
                        appendSystemMessage("Tool $id: $status")
                    }
                }
                newClient.onPlan = { entries ->
                    SwingUtilities.invokeLater {
                        val active = entries.firstOrNull { it.status == "in_progress" }
                        if (active != null) {
                            appendSystemMessage("Plan: ${active.content}")
                        }
                    }
                }
                newClient.onDisconnect = { error ->
                    SwingUtilities.invokeLater {
                        updateConnectionUI(false)
                        if (error != null) {
                            appendSystemMessage("Disconnected: ${error.message}")
                        }
                    }
                }

                // Start ACP connection
                val initResult = newClient.start(projectDir).get()
                val version = initResult.protocolVersion

                // Create a session
                val sid = newClient.createSession(projectDir).get()

                client = newClient
                sessionId = sid
                connected = true

                SwingUtilities.invokeLater {
                    updateConnectionUI(true)
                    appendSystemMessage("Connected (ACP v$version) | Session: $sid")
                }

            } catch (e: Exception) {
                logger.warn("Failed to connect to NanoAgent", e)
                SwingUtilities.invokeLater {
                    updateConnectionUI(false)
                    appendSystemMessage("Connection failed: ${e.message}")
                }
            }
        }
    }

    private fun disconnect() {
        client?.let { c ->
            ApplicationManager.getApplication().executeOnPooledThread {
                try {
                    sessionId?.let { c.closeSession(it).get() }
                } catch (_: Exception) {
                }
                c.close()
            }
        }
        client = null
        sessionId = null
        connected = false
        updateConnectionUI(false)
        appendSystemMessage("Disconnected")
    }

    private fun sendMessage() {
        val text = inputField.text.trim()
        if (text.isEmpty()) return

        val c = client ?: return
        if (!c.isActive || sessionId == null) return

        inputField.text = ""
        sendButton.isEnabled = false
        appendUserMessage(text)

        ApplicationManager.getApplication().executeOnPooledThread {
            try {
                val stopReason = c.sendPrompt(text).get()
                SwingUtilities.invokeLater {
                    sendButton.isEnabled = true
                    if (stopReason == "cancelled") {
                        appendSystemMessage("Prompt was cancelled")
                    }
                }
            } catch (e: Exception) {
                logger.warn("Failed to send prompt", e)
                SwingUtilities.invokeLater {
                    sendButton.isEnabled = true
                    appendSystemMessage("Error: ${e.message}")
                }
            }
        }
    }

    private fun updateConnectionUI(connected: Boolean) {
        connectButton.text = if (connected) "Disconnect" else "Connect"
        sendButton.isEnabled = connected
        inputField.isEnabled = connected
        statusLabel.text = if (connected) "Connected" else "Disconnected"
        statusLabel.foreground = if (connected) JBColor.GREEN else JBColor.RED
    }

    private fun appendUserMessage(text: String) {
        val safeHtml = htmlEscape(text)
        appendToHistory(
            """
            <div style='margin:8px 0;text-align:right'>
                <div style='display:inline-block;background:#4a90d9;color:white;padding:8px 12px;border-radius:12px 12px 4px 12px;max-width:80%;text-align:left'>
                    <b>You</b><br>$safeHtml
                </div>
            </div>
            """.trimIndent()
        )
    }

    private fun appendAgentMessage(text: String) {
        val safeHtml = htmlEscape(text)
        appendToHistory(
            """
            <div style='margin:8px 0'>
                <div style='display:inline-block;background:#2d2d2d;color:#e0e0e0;padding:8px 12px;border-radius:12px 12px 12px 4px;max-width:80%'>
                    <b style='color:#4a90d9'>NanoAgent</b><br>$safeHtml
                </div>
            </div>
            """.trimIndent()
        )
    }

    private fun appendReasoningMessage(text: String) {
        val safeHtml = htmlEscape(text)
        appendToHistory(
            """
            <div style='margin:6px 0'>
                <div style='display:inline-block;background:#242424;color:#a8a8a8;border-left:3px solid #4a90d9;padding:7px 10px;border-radius:6px;max-width:80%'>
                    <b style='color:#8ab4f8'>Thinking</b><br>$safeHtml
                </div>
            </div>
            """.trimIndent()
        )
    }

    private fun appendSystemMessage(text: String) {
        val safeHtml = htmlEscape(text)
        appendToHistory(
            """
            <div style='margin:4px 0;text-align:center'>
                <span style='color:#888;font-size:0.9em;font-style:italic'>$safeHtml</span>
            </div>
            """.trimIndent()
        )
    }

    private fun appendToHistory(html: String) {
        val doc = chatHistory.document as HTMLDocument
        val editorKit = chatHistory.editorKit as HTMLEditorKit

        try {
            val reader = java.io.StringReader(
                "<div style='font-family:system-ui,sans-serif;font-size:14px'>$html</div>"
            )
            editorKit.read(reader, doc, doc.length)
            // Scroll to bottom
            SwingUtilities.invokeLater {
                chatHistory.caretPosition = doc.length
            }
        } catch (e: Exception) {
            logger.warn("Failed to append to chat history", e)
        }
    }

    private fun htmlEscape(text: String): String {
        return text
            .replace("&", "&amp;")
            .replace("<", "&lt;")
            .replace(">", "&gt;")
            .replace("\"", "&quot;")
            .replace("'", "&#39;")
            .replace("\n", "<br>")
    }
}
