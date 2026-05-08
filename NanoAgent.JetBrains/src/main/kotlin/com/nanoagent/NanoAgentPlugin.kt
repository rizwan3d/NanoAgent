package com.nanoagent

import com.intellij.ide.AppLifecycleListener
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.Service
import com.intellij.openapi.diagnostic.Logger
import com.nanoagent.acp.AcpClient
import com.nanoagent.services.LogService
import com.nanoagent.services.LogLevel
import com.nanoagent.services.NanoAgentProcessManager

/**
 * Entry point for the NanoAgent JetBrains plugin.
 *
 * Manages the ACP client lifecycle, provides access to NanoAgent services,
 * and serves as the application-level singleton (like extension.ts in VS Code).
 */
@Service(Service.Level.APP)
class NanoAgentPlugin : AppLifecycleListener {

    private val logger = Logger.getInstance(NanoAgentPlugin::class.java)
    private val logService = LogService()
    private val processManagers = mutableMapOf<String, NanoAgentProcessManager>()

    companion object {
        private const val DEFAULT_NANOAI_COMMAND = "nanoai"
        private const val DEFAULT_LOG_LEVEL = "info"

        /**
         * Get the NanoAgent service instance.
         */
        fun getInstance(): NanoAgentPlugin =
            ApplicationManager.getApplication().getService(NanoAgentPlugin::class.java)
    }

    /**
     * Get the shared log service for the plugin.
     */
    fun getLogService(): LogService = logService

    /**
     * Create a new process manager for the NanoAgent CLI.
     */
    fun createProcessManager(
        nanoaiCommand: String = DEFAULT_NANOAI_COMMAND,
        backendArgs: List<String> = emptyList()
    ): NanoAgentProcessManager {
        return NanoAgentProcessManager(
            nanoaiCommand = nanoaiCommand,
            backendArgs = backendArgs
        )
    }

    /**
     * Create a new ACP client connected to the NanoAgent CLI.
     */
    fun createClient(backendArgs: List<String> = emptyList()): AcpClient {
        return AcpClient(nanoaiPath = DEFAULT_NANOAI_COMMAND, backendArgs = backendArgs)
    }

    override fun appStarted() {
        logService.info("NanoAgent plugin initialized")
        logger.info("NanoAgent plugin initialized (v${NanoAgentPlugin::class.java.`package`?.implementationVersion ?: "0.1.0"})")
    }

    override fun appClosing() {
        // Clean up all process managers
        processManagers.values.forEach { manager ->
            try {
                manager.stop().get()
            } catch (e: Exception) {
                logger.warn("Error stopping NanoAgent process during shutdown", e)
            }
        }
        processManagers.clear()
        logService.info("NanoAgent plugin shutting down")
        logger.info("NanoAgent plugin shut down")
    }
}
