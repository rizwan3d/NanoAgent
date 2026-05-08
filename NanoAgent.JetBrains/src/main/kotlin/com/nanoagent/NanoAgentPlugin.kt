package com.nanoagent

import com.intellij.ide.AppLifecycleListener
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.Service
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.project.Project
import com.intellij.openapi.project.ProjectManagerListener
import com.nanoagent.acp.AcpClient
import com.nanoagent.ui.NanoAgentToolWindowFactory

/**
 * Entry point for the NanoAgent JetBrains plugin.
 *
 * Manages the ACP client lifecycle and provides access to the NanoAgent service.
 */
@Service(Service.Level.APP)
class NanoAgentPlugin : AppLifecycleListener {

    private val logger = Logger.getInstance(NanoAgentPlugin::class.java)

    companion object {
        private const val NANOAI_COMMAND = "nanoai"

        /**
         * Get the NanoAgent service instance.
         */
        fun getInstance(): NanoAgentPlugin =
            ApplicationManager.getApplication().getService(NanoAgentPlugin::class.java)
    }

    /**
     * Create a new ACP client connected to the NanoAgent CLI.
     */
    fun createClient(backendArgs: List<String> = emptyList()): AcpClient {
        return AcpClient(nanoaiPath = NANOAI_COMMAND, backendArgs = backendArgs)
    }

    override fun appStarted() {
        logger.info("NanoAgent plugin initialized")
    }

    override fun appClosing() {
        logger.info("NanoAgent plugin shutting down")
    }
}
