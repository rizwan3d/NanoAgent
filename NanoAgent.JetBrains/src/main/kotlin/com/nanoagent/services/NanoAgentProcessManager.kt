package com.nanoagent.services

import com.intellij.openapi.diagnostic.Logger
import com.nanoagent.acp.AcpClient
import java.io.File
import java.util.concurrent.CompletableFuture
import java.util.concurrent.TimeUnit

/**
 * Manages the lifecycle of the nanoai ACP process.
 *
 * Mirrors VS Code's NanoAgentProcessManager with status tracking:
 * stopped, starting, running, error.
 * Supports start, stop, restart operations with proper cleanup.
 */
enum class ProcessStatus { STOPPED, STARTING, RUNNING, ERROR }

class NanoAgentProcessManager(
    private val nanoaiCommand: String = "nanoai",
    private val backendArgs: List<String> = emptyList()
) {
    private val logger = Logger.getInstance(NanoAgentProcessManager::class.java)
    private var acpClient: AcpClient? = null
    private var status: ProcessStatus = ProcessStatus.STOPPED
    private var isRestarting = false

    // Callbacks
    var onStatusChanged: ((ProcessStatus) -> Unit)? = null

    fun getStatus(): ProcessStatus = status
    fun getClient(): AcpClient? = acpClient
    fun isRunning(): Boolean = acpClient?.isActive == true

    /**
     * Start the NanoAgent ACP process.
     */
    fun start(workingDirectory: String? = null): CompletableFuture<Unit> {
        if (acpClient != null) {
            logger.warn("NanoAgent process is already running.")
            return CompletableFuture.completedFuture(Unit)
        }

        setStatus(ProcessStatus.STARTING)
        logger.info("Starting NanoAgent process: $nanoaiCommand --acp ${backendArgs.joinToString(" ")}")

        val future = CompletableFuture<Unit>()
        val client = AcpClient(nanoaiPath = nanoaiCommand, backendArgs = backendArgs)

        try {
            val initFuture = client.start(workingDirectory)
            initFuture.whenComplete { _, error ->
                if (error != null) {
                    logger.error("Failed to start NanoAgent process", error)
                    acpClient = null
                    setStatus(ProcessStatus.ERROR)
                    future.completeExceptionally(error)
                } else {
                    acpClient = client
                    setStatus(ProcessStatus.RUNNING)
                    logger.info("NanoAgent process started successfully")
                    future.complete(Unit)
                }
            }
        } catch (e: Exception) {
            logger.error("Exception while starting NanoAgent process", e)
            setStatus(ProcessStatus.ERROR)
            future.completeExceptionally(e)
        }

        return future
    }

    /**
     * Stop the NanoAgent ACP process.
     */
    fun stop(): CompletableFuture<Unit> {
        val client = acpClient ?: return CompletableFuture.completedFuture(Unit)

        logger.info("Stopping NanoAgent process...")
        val future = CompletableFuture<Unit>()

        try {
            client.close()
            acpClient = null
            if (!isRestarting) {
                setStatus(ProcessStatus.STOPPED)
            }
            logger.info("NanoAgent process stopped.")
            future.complete(Unit)
        } catch (e: Exception) {
            logger.error("Error stopping NanoAgent process", e)
            acpClient = null
            setStatus(ProcessStatus.STOPPED)
            future.completeExceptionally(e)
        }

        return future
    }

    /**
     * Restart the NanoAgent ACP process.
     */
    fun restart(workingDirectory: String? = null): CompletableFuture<Unit> {
        logger.info("Restarting NanoAgent process...")
        isRestarting = true

        return stop()
            .thenCompose { start(workingDirectory) }
            .whenComplete { _, _ ->
                isRestarting = false
            }
    }

    private fun setStatus(newStatus: ProcessStatus) {
        status = newStatus
        onStatusChanged?.invoke(newStatus)
    }
}
