package com.stemcode

import com.intellij.ide.AppLifecycleListener
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.Service
import com.intellij.openapi.diagnostic.Logger
import com.stemcode.acp.AcpClient
import com.stemcode.services.LogService
import com.stemcode.services.LogLevel
import com.stemcode.services.StemCodeProcessManager

/**
 * Entry point for the StemCode JetBrains plugin.
 *
 * Manages the ACP client lifecycle, provides access to StemCode services,
 * and serves as the application-level singleton (like extension.ts in VS Code).
 */
@Service(Service.Level.APP)
class StemCodePlugin : AppLifecycleListener {

    private val logger = Logger.getInstance(StemCodePlugin::class.java)
    private val logService = LogService()
    private val processManagers = mutableMapOf<String, StemCodeProcessManager>()

    companion object {
        private const val DEFAULT_stemcode_COMMAND = "stemcode"
        private const val DEFAULT_LOG_LEVEL = "info"

        /**
         * Get the StemCode service instance.
         */
        fun getInstance(): StemCodePlugin =
            ApplicationManager.getApplication().getService(StemCodePlugin::class.java)
    }

    /**
     * Get the shared log service for the plugin.
     */
    fun getLogService(): LogService = logService

    /**
     * Create a new process manager for the StemCode CLI.
     */
    fun createProcessManager(
        stemcodeCommand: String = DEFAULT_stemcode_COMMAND,
        backendArgs: List<String> = emptyList()
    ): StemCodeProcessManager {
        return StemCodeProcessManager(
            stemcodeCommand = stemcodeCommand,
            backendArgs = ensureSurfaceArg(backendArgs)
        )
    }

    /**
     * Create a new ACP client connected to the StemCode CLI.
     */
    fun createClient(backendArgs: List<String> = emptyList()): AcpClient {
        return AcpClient(stemcodePath = DEFAULT_stemcode_COMMAND, backendArgs = ensureSurfaceArg(backendArgs))
    }

    override fun appStarted() {
        logService.info("StemCode plugin initialized")
        logger.info("StemCode plugin initialized (v${StemCodePlugin::class.java.`package`?.implementationVersion ?: "0.1.0"})")
    }

    override fun appClosing() {
        // Clean up all process managers
        processManagers.values.forEach { manager ->
            try {
                manager.stop().get()
            } catch (e: Exception) {
                logger.warn("Error stopping StemCode process during shutdown", e)
            }
        }
        processManagers.clear()
        logService.info("StemCode plugin shutting down")
        logger.info("StemCode plugin shut down")
    }

    private fun ensureSurfaceArg(backendArgs: List<String>): List<String> {
        val hasSurface = backendArgs.any { it == "--surface" || it.startsWith("--surface=") }
        return if (hasSurface) {
            backendArgs
        } else {
            backendArgs + listOf("--surface", "jetbrains")
        }
    }
}
