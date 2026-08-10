package com.stemcode.actions

import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.project.Project
import com.intellij.openapi.wm.ToolWindowManager
import com.stemcode.services.LogService
import com.stemcode.ui.ChatPanel

/**
 * Core StemCode actions registered via plugin.xml.
 *
 * Mirrors VS Code's command palette: Start, Stop, Restart, Open Chat,
 * Open Settings, Open Logs.
 */

class StartStemCodeAction : AnAction(), DumbAware {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        showToolWindow(project)
    }
}

class StopStemCodeAction : AnAction(), DumbAware {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        showToolWindow(project)
    }
}

class RestartStemCodeAction : AnAction(), DumbAware {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        showToolWindow(project)
    }
}

class OpenChatAction : AnAction(), DumbAware {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        showToolWindow(project)
    }

    override fun update(e: AnActionEvent) {
        e.presentation.isEnabledAndVisible = e.project != null
    }
}

class OpenSettingsAction : AnAction(), DumbAware {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        showToolWindow(project)
    }
}

class OpenLogsAction : AnAction(), DumbAware {
    override fun actionPerformed(e: AnActionEvent) {
        // Open the IDE's log console filtered for StemCode
        val project = e.project ?: return
        com.intellij.openapi.wm.ToolWindowManager.getInstance(project)
            .getToolWindow("StemCode")?.activate(null, true, true)
    }

    override fun update(e: AnActionEvent) {
        e.presentation.isEnabledAndVisible = e.project != null
    }
}

class NewChatAction : AnAction(), DumbAware {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        showToolWindow(project)
    }
}

// ---- Helper functions ----

private fun showToolWindow(project: Project) {
    val toolWindow = ToolWindowManager.getInstance(project).getToolWindow("StemCode")
    if (toolWindow != null) {
        toolWindow.activate(null, true, true)
    }
}

private fun getChatPanel(project: Project): ChatPanel? {
    val toolWindow = ToolWindowManager.getInstance(project).getToolWindow("StemCode")
    if (toolWindow == null) return null
    val content = toolWindow.contentManager.getContent(0) ?: return null
    return content.component as? ChatPanel
}
