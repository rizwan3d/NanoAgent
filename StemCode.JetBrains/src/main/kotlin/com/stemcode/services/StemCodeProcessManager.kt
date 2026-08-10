package com.stemcode.services

import com.intellij.openapi.diagnostic.Logger
import com.stemcode.acp.AcpClient
import java.io.File
import java.util.concurrent.CompletableFuture
import java.util.concurrent.TimeUnit

/**
 * Manages the lifecycle of the stemcode ACP process.
 *
 * Mirrors VS Code's StemCodeProcessManager with status tracking:
 * stopped, starting, running, error.
 * Supports start, stop, restart operations with proper cleanup.
 */
enum class ProcessStatus { STOPPED, STARTING, RUNNING, ERROR }

class StemCodeProcessManager(
    private val stemcodeCommand: String = "stemcode",
    private val backendArgs: List<String> = emptyList()
) {
    private val logger = Logger.getInstance(StemCodeProcessManager::class.java)
    private var acpClient: AcpClient? = null
    private var status: ProcessStatus = ProcessStatus.STOPPED
    private var isRestarting = false

    // Callbacks
    var onStatusChanged: ((ProcessStatus) -> Unit)? = null

    fun getStatus(): ProcessStatus = status
    fun getClient(): AcpClient? = acpClient
    fun isRunning(): Boolean = acpClient?.isActive == true

    /**
     * Start the StemCode ACP process.
     */
    fun start(workingDirectory: String? = null): CompletableFuture<Unit> {
        if (acpClient != null) {
            logger.warn("StemCode process is already running.")
            return CompletableFuture.completedFuture(Unit)
        }

        setStatus(ProcessStatus.STARTING)
        logger.info("Starting StemCode process: $stemcodeCommand --acp ${backendArgs.joinToString(" ")}")

        val future = CompletableFuture<Unit>()
        val client = AcpClient(stemcodePath = stemcodeCommand, backendArgs = backendArgs)

        try {
            val initFuture = client.start(workingDirectory)
            initFuture.whenComplete { _, error ->
                if (error != null) {
                    logger.error("Failed to start StemCode process", error)
                    acpClient = null
                    setStatus(ProcessStatus.ERROR)
                    future.completeExceptionally(error)
                } else {
                    acpClient = client
                    setStatus(ProcessStatus.RUNNING)
                    logger.info("StemCode process started successfully")
                    future.complete(Unit)
                }
            }
        } catch (e: Exception) {
            logger.error("Exception while starting StemCode process", e)
            setStatus(ProcessStatus.ERROR)
            future.completeExceptionally(e)
        }

        return future
    }

    /**
     * Stop the StemCode ACP process.
     */
    fun stop(): CompletableFuture<Unit> {
        val client = acpClient ?: return CompletableFuture.completedFuture(Unit)

        logger.info("Stopping StemCode process...")
        val future = CompletableFuture<Unit>()

        try {
            client.close()
            acpClient = null
            if (!isRestarting) {
                setStatus(ProcessStatus.STOPPED)
            }
            logger.info("StemCode process stopped.")
            future.complete(Unit)
        } catch (e: Exception) {
            logger.error("Error stopping StemCode process", e)
            acpClient = null
            setStatus(ProcessStatus.STOPPED)
            future.completeExceptionally(e)
        }

        return future
    }

    /**
     * Restart the StemCode ACP process.
     */
    fun restart(workingDirectory: String? = null): CompletableFuture<Unit> {
        logger.info("Restarting StemCode process...")
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
