package com.nanoagent.actions

import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.openapi.wm.ToolWindowManager
import com.nanoagent.services.LogService
import com.nanoagent.ui.ChatPanel
import java.io.BufferedReader
import java.io.InputStreamReader

/**
 * Editor integration actions for NanoAgent.
 *
 * Mirrors VS Code's chat.ts commands:
 * - Send Selection
 * - Send Current File
 * - Review Current File
 * - Review Git Diff
 * - Explain Selection
 * - Plan Changes
 * - Apply Suggested Changes
 */

abstract class EditorActionBase : AnAction(), DumbAware {
    protected val logService = LogService()

    protected fun getChatPanel(project: Project): ChatPanel? {
        val toolWindow = ToolWindowManager.getInstance(project).getToolWindow("NanoAgent") ?: return null
        toolWindow.activate(null, true, true)
        val content = toolWindow.contentManager.getContent(0) ?: return null
        return content.component as? ChatPanel
    }

    protected fun getSelectedText(e: AnActionEvent): Triple<String, String, String>? {
        val editor = e.getData(CommonDataKeys.EDITOR) ?: return null
        val file = e.getData(CommonDataKeys.VIRTUAL_FILE) ?: return null
        val selectionModel = editor.selectionModel
        if (!selectionModel.hasSelection()) return null

        val text = selectionModel.selectedText ?: return null
        return Triple(file.name, file.path, text)
    }

    protected fun getCurrentFileText(e: AnActionEvent): Pair<String, String>? {
        val file = e.getData(CommonDataKeys.VIRTUAL_FILE) ?: return null
        val text = try {
            val inputStream = file.inputStream
            val reader = BufferedReader(InputStreamReader(inputStream))
            val content = reader.readText()
            reader.close()
            content
        } catch (ex: Exception) {
            return null
        }
        return Pair(file.name, text)
    }

    protected fun getGitDiff(project: Project): String? {
        return try {
            val process = ProcessBuilder("git", "diff", "--no-ext-diff", "HEAD")
                .directory(java.io.File(project.basePath ?: return null))
                .redirectErrorStream(true)
                .start()
            val output = process.inputStream.bufferedReader().readText()
            process.waitFor()
            if (output.isBlank()) null else output
        } catch (e: Exception) {
            null
        }
    }
}

class SendSelectionAction : EditorActionBase() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val (fileName, path, text) = getSelectedText(e) ?: run {
            com.intellij.openapi.ui.Messages.showWarningDialog(
                project, "Select code or text before sending it to NanoAgent.", "No Selection"
            )
            return
        }
        val prompt = "Use this selection as context:\n\nFile: $fileName\n\n```\n$text\n```"
        getChatPanel(project)?.let { /* send via session manager */ }
    }

    override fun update(e: AnActionEvent) {
        val editor = e.getData(CommonDataKeys.EDITOR)
        e.presentation.isEnabledAndVisible = editor != null && editor.selectionModel.hasSelection()
    }
}

class ExplainSelectionAction : EditorActionBase() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val (fileName, _, text) = getSelectedText(e) ?: run {
            com.intellij.openapi.ui.Messages.showWarningDialog(
                project, "Select code or text before asking NanoAgent to explain it.", "No Selection"
            )
            return
        }
        val prompt = "Explain this selection:\n\nFile: $fileName\n\n```\n$text\n```"
        getChatPanel(project)?.let { /* send via session manager */ }
    }

    override fun update(e: AnActionEvent) {
        val editor = e.getData(CommonDataKeys.EDITOR)
        e.presentation.isEnabledAndVisible = editor != null && editor.selectionModel.hasSelection()
    }
}

class SendCurrentFileAction : EditorActionBase() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val (fileName, text) = getCurrentFileText(e) ?: run {
            com.intellij.openapi.ui.Messages.showWarningDialog(
                project, "Open a file before sending it to NanoAgent.", "No File"
            )
            return
        }
        val prompt = "Use this file as context:\n\nFile: $fileName\n\n```\n$text\n```"
        getChatPanel(project)?.let { /* send via session manager */ }
    }

    override fun update(e: AnActionEvent) {
        e.presentation.isEnabledAndVisible = e.getData(CommonDataKeys.VIRTUAL_FILE) != null
    }
}

class ReviewCurrentFileAction : EditorActionBase() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val (fileName, text) = getCurrentFileText(e) ?: run {
            com.intellij.openapi.ui.Messages.showWarningDialog(
                project, "Open a file before asking NanoAgent to review it.", "No File"
            )
            return
        }
        val prompt = "Review this file for bugs, regressions, and missing tests:\n\nFile: $fileName\n\n```\n$text\n```"
        getChatPanel(project)?.let { /* send via session manager */ }
    }

    override fun update(e: AnActionEvent) {
        e.presentation.isEnabledAndVisible = e.getData(CommonDataKeys.VIRTUAL_FILE) != null
    }
}

class ReviewGitDiffAction : EditorActionBase() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val diff = getGitDiff(project)
        if (diff == null) {
            com.intellij.openapi.ui.Messages.showInfoMessage(
                project, "No git diff found for NanoAgent to review.", "No Git Diff"
            )
            return
        }
        val prompt = "Review this git diff for bugs, regressions, and missing tests:\n\n```diff\n$diff\n```"
        getChatPanel(project)?.let { /* send via session manager */ }
    }

    override fun update(e: AnActionEvent) {
        e.presentation.isEnabledAndVisible = e.project != null
    }
}

class PlanChangesAction : EditorActionBase() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val prompt = "Plan the following change before editing:\n\n"
        getChatPanel(project)?.let { /* send via session manager */ }
    }

    override fun update(e: AnActionEvent) {
        e.presentation.isEnabledAndVisible = e.project != null
    }
}

class ApplySuggestedChangesAction : EditorActionBase() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val prompt = "Apply the suggested changes from the previous NanoAgent response."
        getChatPanel(project)?.let { /* send via session manager */ }
    }

    override fun update(e: AnActionEvent) {
        e.presentation.isEnabledAndVisible = e.project != null
    }
}
