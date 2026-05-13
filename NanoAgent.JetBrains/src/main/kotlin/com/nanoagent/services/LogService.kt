package com.nanoagent.services

import com.intellij.openapi.diagnostic.Logger

/**
 * Dedicated logging service for the NanoAgent plugin.
 *
 * Matches the capability of VS Code's LogService with configurable
 * log levels and structured output via the IDE's logging framework.
 */
enum class LogLevel { DEBUG, INFO, WARN, ERROR }

class LogService {
    private val logger = Logger.getInstance(LogService::class.java)

    companion object {
        private var level: LogLevel = LogLevel.INFO

        fun setLevel(newLevel: LogLevel) {
            level = newLevel
        }

        fun getLevel(): LogLevel = level
    }

    fun debug(message: String, data: Any? = null) {
        if (level.ordinal <= LogLevel.DEBUG.ordinal) {
            logger.debug(formatMessage(message, data))
        }
    }

    fun info(message: String, data: Any? = null) {
        if (level.ordinal <= LogLevel.INFO.ordinal) {
            logger.info(formatMessage(message, data))
        }
    }

    fun warn(message: String, data: Any? = null) {
        if (level.ordinal <= LogLevel.WARN.ordinal) {
            logger.warn(formatMessage(message, data))
        }
    }

    fun error(message: String, error: Throwable? = null) {
        if (level.ordinal <= LogLevel.ERROR.ordinal) {
            if (error != null) {
                logger.error(message, error)
            } else {
                logger.error(message)
            }
        }
    }

    private fun formatMessage(message: String, data: Any?): String {
        return if (data != null) {
            "$message | data=$data"
        } else {
            message
        }
    }
}
