# NanoAgent Product Documentation

**Product type:** Local-first AI coding agent for desktop and terminal workflows  
**Primary audience:** Users, founders, product managers, designers, support teams, and non-technical stakeholders  
**Source of truth:** Current repository inspection

## Executive Summary

NanoAgent is a local-first AI coding assistant that helps users work inside local software projects. It can inspect files, search code, make controlled edits, run build and test commands, manage AI provider and model settings, preserve conversation sections, and use repository-specific guidance. The product is available as a desktop app and as a terminal command named `nanoai`.

The product's main value is controlled local autonomy: users can ask an AI agent to perform real engineering work in a local workspace while permissions, sandboxing, approval prompts, profiles, and local memory keep the user in control.

Repository inspection found no hosted web app, public user API, database schema, user-account system, email system, or admin console in this codebase. Where behavior is not clear from the current code, it is marked as **Needs confirmation**.

## Table of Contents

1. [Product Name](#1-product-name)
2. [Product Summary](#2-product-summary)
3. [Target Users](#3-target-users)
4. [Core Features](#4-core-features)
5. [User Journeys](#5-user-journeys)
6. [Screens / Pages / Interfaces](#6-screens--pages--interfaces)
7. [Permissions and Roles](#7-permissions-and-roles)
8. [Data and Content Model](#8-data-and-content-model)
9. [Notifications, Emails, or Automations](#9-notifications-emails-or-automations)
10. [Settings and Configuration](#10-settings-and-configuration)
11. [Errors, Empty States, and Edge Cases](#11-errors-empty-states-and-edge-cases)
12. [Product Limitations](#12-product-limitations)
13. [Suggested Product Improvements](#13-suggested-product-improvements)
14. [Open Questions](#14-open-questions)

## Inspection Scope

The repository inspection covered the README, existing documentation, desktop interface, terminal interface, command surface, onboarding flow, model and provider setup, permission behavior, sandbox behavior, local storage, memory, MCP support, lifecycle hooks, custom profiles, workspace skills, tests, and install scripts.

No web routes, public product APIs, hosted admin screens, database schema, migrations, or email flows were found in the inspected codebase.

---

## 1. Product Name

**Product name:** NanoAgent

**Tagline / positioning:** A local coding agent for desktop and terminal workflows.

**CLI command:** `nanoai`

**Confidence:** Confirmed from the README, install scripts, terminal help, desktop title, and product identity.

---

## 2. Product Summary

NanoAgent helps users complete software engineering tasks in a local repository. A user can open a workspace, ask for help in natural language, let the agent inspect files, make edits, run commands, review code, plan changes, and preserve conversation history across local work sessions.

NanoAgent is for people who want AI assistance that can act directly in a local codebase without moving the full workflow into a hosted IDE. It is best understood as a local coding companion for implementation, code review, investigation, planning, and build/test loops.

The main problem it solves is workflow fragmentation. Developers often move between chat tools, editors, terminals, documentation, code review tools, and memory notes. NanoAgent combines those activities into a controlled local agent experience.

The core value proposition is:

- Work directly in local repositories.
- Choose the AI provider and active model.
- Use either a desktop app or terminal UI.
- Keep users in control through permissions and sandboxing.
- Preserve local conversation sections and workspace lessons.
- Adapt behavior through profiles, skills, workspace instructions, MCP tools, and hooks.

---

## 3. Target Users

### Individual Software Developers

Developers can open a local project, ask NanoAgent to inspect the codebase, fix bugs, add features, run validation commands, and explain outcomes.

### Engineering Leads

Engineering leads can use planning and review workflows to assess changes, identify risks, guide implementation plans, and check validation coverage.

### Code Reviewers

Reviewers can use a read-only review profile to look for bugs, regressions, unsafe changes, missing tests, and edge cases without making edits by default.

### Build and DevOps Engineers

Build-focused users can ask NanoAgent to run or inspect build, test, restore, lint, and toolchain commands, then capture reusable lessons from failures and fixes.

### Founders and Technical Product Owners

Founders can use NanoAgent to accelerate product iteration while keeping code local and approving meaningful changes before they happen.

### Support and Documentation Teams

Technical support or documentation users can inspect product behavior from a repository, summarize workflows, and use workspace instructions or skills for repeatable support tasks.

---

## 4. Core Features

### 4.1 First-Run Provider Onboarding

**User benefit:** Users can connect NanoAgent to their preferred AI provider during first launch.

**How the user uses it:** The product prompts the user to choose OpenAI, Google AI Studio, Anthropic, or an OpenAI-compatible provider. The user enters an API key. For a custom compatible provider, the user also enters a base URL.

**Expected outcome:** NanoAgent saves local provider configuration and can start a model-backed session.

**Observed limits and edge cases:** API keys cannot be empty. Custom base URLs must be absolute, use HTTP or HTTPS, and cannot include a query string or fragment. If local provider setup is incomplete, NanoAgent asks whether to reconfigure or cancel startup.

### 4.2 Provider Flexibility

**User benefit:** Users are not locked into one model vendor.

**How the user uses it:** Users select one of the supported provider types during onboarding or configure an OpenAI-compatible endpoint.

**Expected outcome:** NanoAgent sends model requests through the selected provider profile.

**Observed limits and edge cases:** Provider availability depends on the user's API key, account access, model availability, and network/API reliability.

### 4.3 Model Discovery and Switching

**User benefit:** Users can see and switch between available models for a session.

**How the user uses it:** NanoAgent discovers models from the configured provider. Users can view models and switch the active model through the desktop controls or `/models` and `/use` terminal commands.

**Expected outcome:** Subsequent prompts use the selected model.

**Observed limits and edge cases:** If the provider returns no usable models, discovery fails. Duplicate model IDs are ignored. A requested model can be not found or ambiguous. The product picks the configured preferred model when available, otherwise it falls back to the first returned model.

### 4.4 Desktop Workspace Management

**User benefit:** Users can visually open and return to local repositories.

**How the user uses it:** In the desktop app, the user clicks **+ Open** and selects a local folder. Recent workspaces appear in the Workspaces list.

**Expected outcome:** The selected folder becomes the active workspace for conversation, sections, commands, and file operations.

**Observed limits and edge cases:** Blank, invalid, or missing folders are ignored. Reopening an existing recent workspace selects it instead of creating a duplicate. No visible desktop action was found for removing or renaming recent workspaces.

### 4.5 Conversation Sections and History

**User benefit:** Users can resume previous work instead of starting over.

**How the user uses it:** The desktop app lists saved sections for the selected workspace. Users can select a section or click **+ New**. Terminal users can resume an existing section with a section ID.

**Expected outcome:** NanoAgent restores conversation history, section title, active model, profile, thinking mode, and related session state when available.

**Observed limits and edge cases:** Sections are local. Section IDs must be valid GUIDs. Sections from another workspace are blocked or skipped. Missing, unreadable, malformed, or corrupt section records are ignored. No visible desktop action was found for renaming, deleting, exporting, or sharing a section.

### 4.6 Chat-Based Coding Assistance

**User benefit:** Users can ask for coding work in natural language.

**How the user uses it:** The user types a prompt in the desktop composer or terminal UI, then runs it against the active workspace.

**Expected outcome:** NanoAgent responds with analysis, file changes when appropriate, tool activity, command results, validation summaries, or review findings.

**Observed limits and edge cases:** A prompt cannot run when no project is selected, the prompt is blank, or NanoAgent is already working. If the model returns no visible text, the UI shows a completed-with-no-output message.

### 4.7 Terminal Workflow

**User benefit:** Keyboard-first users can run NanoAgent without opening the desktop app.

**How the user uses it:** Users run `nanoai` for the interactive terminal UI, pass a prompt for one-shot mode, pipe prompt text from another command, or use terminal options for section, profile, and thinking settings.

**Expected outcome:** The terminal UI shows conversation history, live activity, provider/model status, progress, prompts, and section resume information.

**Observed limits and edge cases:** One-shot mode requires a non-empty prompt. `--interactive` requires terminal input. `--stdin` requires redirected standard input. Terminal rendering depends on terminal capabilities.

### 4.8 Terminal Utility Commands

**User benefit:** Users can perform quick local checks inside the terminal interface.

**How the user uses it:** The terminal UI supports commands such as `/clear`, `/ls`, and `/read <file>` in addition to backend commands.

**Expected outcome:** Users can clear the screen, list workspace files, or read a file after approving the read prompt.

**Observed limits and edge cases:** `/ls` returns up to 100 files and skips common generated folders. `/read` asks for permission and defaults to deny if the prompt auto-selects after its timeout. These commands are terminal-only and are not listed in the current `/help` output.

### 4.9 File Operations

**User benefit:** NanoAgent can inspect and change local project files without copy/paste.

**How the user uses it:** The agent can list directories, search file names, search text, read files, write files, delete files, and apply focused patches when permissions allow.

**Expected outcome:** Users get concrete repository changes or inspection results from the agent.

**Observed limits and edge cases:** File paths must stay inside the current workspace. File reads are limited to UTF-8 text and a maximum readable size. Directory and search results are capped. Writes overwrite by default unless overwrite is disabled. Patch format is strict, and patch context must match existing file content.

### 4.10 Shell Command Execution

**User benefit:** NanoAgent can build, test, lint, restore dependencies, probe toolchains, and run other workspace commands.

**How the user uses it:** The agent runs shell commands through the current workspace. Users can approve commands, allow safe command patterns, deny risky command patterns, or require escalation for commands that need access outside the configured sandbox.

**Expected outcome:** Build/test loops can be completed and summarized in the same session.

**Observed limits and edge cases:** Commands must use allowed command names. Dangerous command patterns are denied by default. Output is capped. Escalated sandbox requests require a justification. Strict shell sandboxing is OS-dependent; unsupported platforms fail closed unless escalation is approved or unrestricted mode is configured.

### 4.11 Permission Controls and Sandboxing

**User benefit:** Users control what the agent can read, change, run, access, or remember.

**How the user uses it:** NanoAgent evaluates actions against default permissions, configured rules, profile restrictions, sandbox mode, and session overrides. Users respond to approval prompts or add allow/deny overrides.

**Expected outcome:** Sensitive or risky actions are allowed, blocked, or routed through an approval prompt.

**Observed limits and edge cases:** Permission modes are Allow, Ask, and Deny. Sandbox modes are ReadOnly, WorkspaceWrite, and DangerFullAccess. Later matching rules win. Read-only profiles block edits and unsafe shell work. `.env`-style reads are denied by default, though session overrides can change behavior.

### 4.12 Approval Prompts

**User benefit:** Users can approve risky actions one time or remember a decision for the current agent.

**How the user uses it:** When NanoAgent requests approval, the user can choose Allow once, Allow for agent, Deny once, or Deny for agent.

**Expected outcome:** The requested action proceeds, is denied, or creates a session-scoped permission override.

**Observed limits and edge cases:** Approval prompts can be cancelled. The default approval option is selected automatically after a timeout. Session overrides do not appear to persist beyond the active session.

### 4.13 Agent Profiles

**User benefit:** Users can change NanoAgent's behavior to match the task.

**How the user uses it:** Users select profiles in the desktop controls or with `/profile` in the terminal.

**Expected outcome:** The active profile changes how NanoAgent approaches subsequent prompts.

**Observed limits and edge cases:** Built-in primary profiles are build, plan, and review. The build profile can edit and run normal toolchain work under permissions. Plan and review are read-only and use safe inspection behavior. Desktop profile options appear limited to the built-in primary profiles.

### 4.14 Subagent Delegation And Orchestration

**User benefit:** NanoAgent can hand off focused work to narrower agents and coordinate several independent subtasks as one workflow.

**How the user uses it:** A user or the primary agent can invoke a subagent such as `@general` or `@explore`, or a workspace-defined subagent. Primary agents can use `agent_delegate` for one handoff or `agent_orchestrate` for multiple focused handoffs.

**Expected outcome:** A focused subtask is completed or investigated and returned as a handoff to the main conversation. With orchestration, read-only subtasks can run in parallel, editing-capable subtasks are kept controlled, and delegated file edits are recorded for undo.

**Observed limits and edge cases:** Read-only profiles cannot delegate to implementation-capable subagents. Custom subagents must be defined locally and valid. Delegated agents inherit session permissions, workspace path, and working directory. Subagents cannot start nested delegation.

### 4.15 Workspace Custom Agents

**User benefit:** Teams can tailor NanoAgent to repeatable local workflows.

**How the user uses it:** Users define custom agents in the workspace's NanoAgent configuration area, including name, mode, description, edit behavior, shell behavior, and optional tool list.

**Expected outcome:** Custom primary agents or subagents become available in the session.

**Observed limits and edge cases:** Missing, unreadable, malformed, or duplicate custom agent definitions are ignored. Defaults are conservative: subagent mode, read-only edits, and safe shell inspection.

### 4.16 Planning Mode and Live Plans

**User benefit:** Users can ask NanoAgent to inspect first, reason through options, and make a concrete task plan.

**How the user uses it:** Users switch to the plan profile or the agent uses planning behavior during complex work. The agent can publish a live plan with pending, in-progress, and completed steps.

**Expected outcome:** Users get a clear implementation plan grounded in the current repository, with risks and validation steps.

**Observed limits and edge cases:** Planning mode is read-only. Plans must have at most one in-progress step and must keep statuses in order.

### 4.17 Workspace Instructions

**User benefit:** Teams can provide persistent repository guidance once instead of repeating it in every prompt.

**How the user uses it:** Users add supported workspace instruction documents at the workspace root or supported configuration location.

**Expected outcome:** NanoAgent includes those instructions in the model context for the workspace.

**Observed limits and edge cases:** Instruction content is length-limited and redacted for common secret patterns before being included. Empty or missing instruction documents are ignored.

### 4.18 Workspace Skills

**User benefit:** Teams can create task-specific playbooks that the agent loads only when relevant.

**How the user uses it:** Users define skills in the workspace's NanoAgent skills area. NanoAgent sees skill names and descriptions as routing signals, then loads full skill instructions only when a skill is selected.

**Expected outcome:** The agent can follow targeted guidance for recurring work such as testing, review, release, framework-specific implementation, or documentation.

**Observed limits and edge cases:** Skill descriptions and instruction bodies are length-limited. Duplicate names are deduplicated. Missing, unreadable, empty, or invalid skills are ignored.

### 4.19 Lesson Memory

**User benefit:** NanoAgent can avoid repeating local mistakes and remember useful workspace lessons.

**How the user uses it:** The agent can search, list, save, edit, or delete local lessons. It can also observe some failed tool or shell outcomes and later save a lesson when a successful fix is detected.

**Expected outcome:** Future turns can include relevant lessons as hypotheses to verify against current files and fresh tool output.

**Observed limits and edge cases:** Memory is local, can be disabled, has entry and prompt-size limits, and redacts secrets by default. Manual memory writes require approval by default. Lesson search/list is allowed without approval. Automatic observations are limited to trackable failures.

### 4.20 MCP Tool Integration

**User benefit:** Users can extend NanoAgent with external tool capabilities.

**How the user uses it:** Users configure MCP servers in user-level or workspace-level NanoAgent configuration. The terminal `/mcp` command shows configured servers and discovered tools.

**Expected outcome:** Available MCP tools become accessible to the agent with configured approval behavior.

**Observed limits and edge cases:** MCP servers can be disabled, unavailable, required, filtered by enabled/disabled tool lists, or timed out. If a required MCP server fails, startup can fail. No visual desktop MCP manager was found.

### 4.21 Web and Current-Information Tools

**User benefit:** NanoAgent can look up current or external information when local code is not enough.

**How the user uses it:** The agent can run web search, image search, page open, page find, screenshots, finance lookups, weather, sports schedules/standings, and time checks when permissions allow.

**Expected outcome:** NanoAgent can ground answers in external sources or current data.

**Observed limits and edge cases:** Web requests can fail and return warnings. Search/open/find results are capped and text extraction is limited. Network access is permission-controlled.

### 4.22 Lifecycle Hooks

**User benefit:** Teams can run local automation around agent actions.

**How the user uses it:** Users configure hooks for task, tool, file, shell, web, memory, delegation, and permission events.

**Expected outcome:** Local scripts can enforce checks, capture logs, or react to actions before or after they happen.

**Observed limits and edge cases:** Hooks can be filtered by event, tool, file path, or shell command pattern. Before hooks block by default when they fail. After hooks continue by default unless configured otherwise. Hooks have timeout and output limits.

### 4.23 Undo and Redo for File Edits

**User benefit:** Users have a safety net for tracked agent file changes.

**How the user uses it:** Users click Undo/Redo in the desktop app or use `/undo` and `/redo` in the terminal.

**Expected outcome:** The most recent tracked file edit transaction can be rolled back or reapplied.

**Observed limits and edge cases:** Undo/redo covers tracked file edit transactions. It does not guarantee reversal of untracked external command side effects.

### 4.24 Secret Redaction and Tool Audit Logging

**User benefit:** The product reduces the chance that credentials appear in stored or displayed artifacts.

**How the user uses it:** Secret redaction runs across many outputs and local records. Users can enable local tool audit logging through configuration.

**Expected outcome:** Common secret patterns are replaced with redacted values, and optional audit records can track completed tool calls.

**Observed limits and edge cases:** Redaction is pattern-based and should not be treated as a complete data-loss-prevention guarantee. Audit logging is disabled by default. No visible audit viewer was found.

### 4.25 Desktop Markdown and File References

**User benefit:** Desktop users can read formatted responses and open referenced files quickly.

**How the user uses it:** NanoAgent responses render markdown-like text, lists, code blocks, and detected file references. Clicking a detected file reference opens it through the operating system.

**Expected outcome:** Users can move from an answer to a local file more quickly.

**Observed limits and edge cases:** File opening failures are ignored silently. File reference detection is pattern-based and may miss or over-detect some paths.

---

## 5. User Journeys

### 5.1 First Run Setup

1. The user launches NanoAgent from the desktop app or terminal.
2. NanoAgent checks for existing local provider configuration and API key.
3. If setup is missing, the user selects a provider.
4. The user enters an API key.
5. If the user selected an OpenAI-compatible provider, the user enters a valid base URL.
6. NanoAgent saves local configuration and discovers available models.
7. The user arrives in a ready session.

### 5.2 Open a Workspace in Desktop

1. The user opens the desktop app.
2. The user clicks **+ Open**.
3. The user selects a local repository folder.
4. NanoAgent adds the workspace to the recent Workspaces list.
5. NanoAgent loads sections for that workspace if any exist.
6. The selected workspace appears in the top bar, conversation area, and controls panel.

### 5.3 Run a Coding Task in Desktop

1. The user selects a workspace.
2. The user types a prompt in the composer.
3. The user clicks **Run**.
4. NanoAgent shows working status and activity.
5. If an action requires approval, NanoAgent shows an approval prompt.
6. NanoAgent may inspect files, edit files, run commands, search web information, or update a plan depending on the task and permissions.
7. NanoAgent shows the response, tool output, progress note, and completion status.
8. If file edits were made, the user can use Undo if needed.

### 5.4 Resume Previous Work

1. The user selects a workspace.
2. The user chooses a section from the Sections list or starts terminal with a section ID.
3. NanoAgent restores the section's conversation history and settings.
4. The user continues from the prior context.

### 5.5 Switch Model, Thinking, or Profile

1. The user opens an active workspace session.
2. The user selects a model, thinking mode, or profile in desktop controls, or uses terminal commands.
3. The user applies the change.
4. NanoAgent uses the new session setting for subsequent prompts.

### 5.6 Plan Before Implementation

1. The user selects the plan profile or asks for a plan.
2. NanoAgent inspects relevant files and safe environment signals.
3. NanoAgent avoids edits and mutating shell work.
4. The user receives a plan with verified facts, assumptions, files/areas, validation steps, and risks.
5. The user can switch to build mode when ready to implement.

### 5.7 Review Without Editing

1. The user selects the review profile.
2. The user asks NanoAgent to review changes or inspect a risk area.
3. NanoAgent searches and reads code without editing by default.
4. The user receives findings, missing tests, assumptions, and residual risks.

### 5.8 Approve or Deny a Sensitive Action

1. NanoAgent requests an action such as editing a file, running a command, escalating sandbox permissions, writing memory, using network access, or calling an MCP tool.
2. The user chooses Allow once, Allow for agent, Deny once, or Deny for agent.
3. NanoAgent applies the decision.
4. If the user chose an agent-level decision, the override affects later matching requests in the current session.

### 5.9 Use Terminal One-Shot Mode

1. The user runs `nanoai` with a prompt argument, `--prompt`, or redirected standard input.
2. NanoAgent initializes provider and model configuration.
3. NanoAgent runs the prompt or slash command.
4. The response prints to the terminal.
5. The process exits with a success or error code.

### 5.10 Configure Advanced Workspace Behavior

1. The user adds workspace instructions, skills, custom agents, MCP servers, memory policy, audit policy, permission rules, or lifecycle hooks in local configuration.
2. The user starts or refreshes a session.
3. NanoAgent loads applicable local configuration.
4. The user uses prompts or commands to inspect and benefit from the configured behavior.

---

## 6. Screens / Pages / Interfaces

### Desktop Application

The desktop product is a single main window.

**Top bar:** Shows the NanoAgent brand, current project name/path, and status such as Ready or Working.

**Workspaces sidebar:** Lists recent local projects and includes **+ Open** for selecting a folder.

**Sections sidebar:** Lists saved sections for the selected workspace, including title, updated time, turn count, model, and workspace path. Includes **+ New** to start a new section.

**Conversation area:** Shows user messages, NanoAgent responses, tool messages, markdown-style formatting, file reference buttons, and status notes.

**Prompt composer:** Multi-line text input with **Run** for sending a prompt.

**Selection prompt overlay:** Shows approval or choice prompts with options, descriptions, default option indicators, optional countdown, and Cancel when allowed.

**Text/secret prompt overlay:** Collects user input such as API keys or base URLs. Secret prompts mask input.

**Controls panel:** Provides Refresh, model selection, thinking mode selection, profile selection, Help, Models, Permissions, Rules, Permission Override, Undo, Redo, and workspace details.

**Activity panel:** Shows status updates, command output summaries, tool activity, errors, and progress items.

**Working status strip:** Shows that NanoAgent is working, elapsed time, and estimated output tokens.

### Terminal Interface

The terminal interface supports an interactive full-screen conversation experience with:

- Prompt entry.
- Multi-line input.
- Live activity and status.
- Conversation scrolling.
- Modal prompts.
- Streaming assistant responses.
- Provider/model session information.
- Resume hints when exiting.

It also supports one-shot prompt mode and standard input mode.

### Terminal Commands

Confirmed user-facing terminal commands include:

- `/help` to list available backend commands.
- `/config` to show current provider, session, profile, thinking mode, active model, and config path.
- `/models` to list available models.
- `/use <model>` to switch model.
- `/profile <name>` to show or switch profiles.
- `/thinking [on|off]` to show or set thinking mode.
- `/permissions` to show permission summary.
- `/rules` to show effective permission rules.
- `/allow <tool-or-tag> [pattern]` to add a session allow override.
- `/deny <tool-or-tag> [pattern]` to add a session deny override.
- `/mcp` to show MCP servers and tools.
- `/undo` to roll back the most recent tracked file edit transaction.
- `/redo` to reapply the most recently undone file edit transaction.
- `/exit` to exit.

Terminal-only utility behavior also includes `/clear`, `/ls`, and `/read <file>`.

### Web Pages, Routes, and Public APIs

No hosted product pages, web routes, public API endpoints, controllers, or admin screens were found in the inspected repository.

### Database or Admin Interfaces

No database admin UI, database schema, migrations, or hosted data-management interface was found.

### VS Code Interface

A VS Code product surface could not be confirmed from the inspected repository contents. **Needs confirmation.**

---

## 7. Permissions and Roles

### Role Model

No account system, organization model, admin role, billing role, or role-based access control system was found. NanoAgent appears to be a local single-user product.

### Operational Access Levels

Access is controlled through local permission settings, sandbox mode, agent profiles, approval prompts, and session overrides.

### Permission Modes

**Allow:** The action can proceed.

**Ask:** The user must approve before the action proceeds.

**Deny:** The action is blocked.

### Sandbox Modes

**ReadOnly:** Blocks write-like actions and unsafe/mutating shell behavior.

**WorkspaceWrite:** Allows workspace-scoped write behavior under permissions.

**DangerFullAccess:** Runs without sandbox restrictions when configured or approved through escalation.

### Built-In Profiles

**Build:** Default hands-on coding profile. Can inspect, edit, run toolchain commands, and complete implementation work under permissions.

**Plan:** Read-only planning profile. Can inspect files and run safe probes, but does not edit or run mutating shell work.

**Review:** Read-only review profile. Focuses on findings, regressions, edge cases, and missing tests without edits by default.

**General:** Implementation-capable subagent for bounded delegated work.

**Explore:** Read-only subagent for focused codebase investigation.

**Orchestration:** Primary profiles can coordinate multiple subagent tasks with `agent_orchestrate`. The automatic strategy runs consecutive read-only tasks in parallel and runs editing-capable tasks one at a time.

### Built-In Safety Behavior

Confirmed safety behavior includes:

- Reads are generally allowed.
- File writes, deletes, patches, edits, agent delegation, MCP tools, and external-directory behavior generally require approval unless rules allow them.
- `.env`-style reads are denied by default.
- Known safe build/test command patterns can be allowed.
- Known dangerous shell command patterns are denied.
- Paths outside the workspace are denied.
- Planning and review profiles block edits and unsafe shell work.
- Sandbox escalation requires a justification and approval.

---

## 8. Data and Content Model

NanoAgent uses local files and platform credential storage rather than a central product database.

### Main Product Objects

**Provider configuration:** The selected provider profile, preferred model, and thinking mode stored locally.

**API key secret:** The provider API key stored through the operating system's credential storage where supported.

**Workspace:** A local folder selected by the user. Desktop remembers recent workspaces locally.

**Section:** A saved local conversation/work session tied to a workspace. A section contains identity, title, workspace path, model, profile, thinking mode, conversation turns, plan state, and session state.

**Conversation turn:** A user prompt and NanoAgent response, including related tool activity and estimated metrics.

**Model:** A provider-returned model ID that can be active for a session.

**Agent profile:** A behavior mode that controls purpose, editing ability, shell behavior, and tool access.

**Subagent:** A focused profile invoked for one delegated task.

**Permission rule:** A local allow/ask/deny rule matching a tool, tag, command, target, or pattern.

**Permission override:** A session-scoped rule created by the user from a command or approval prompt.

**Workspace instruction:** Persistent repository guidance included in the session context.

**Skill:** A local playbook with a name, description, and body instructions loaded on demand.

**MCP server:** A configured external tool server that can expose tools to NanoAgent.

**Lesson memory entry:** A local reusable lesson about a trigger, problem, lesson, tags, optional command/tool context, and fixed status.

**Lifecycle hook:** A local automation rule that runs a command around selected task/tool events.

**Tool audit record:** An optional local record of completed tool calls.

### Relationships

- A user selects a workspace.
- A workspace can have multiple sections.
- A section contains conversation turns and session state.
- A section uses one active provider profile, model, profile, and thinking mode at a time.
- A workspace can define instructions, skills, custom agents, memory, hooks, MCP configuration, and audit policy.
- Permission rules and profile restrictions govern agent actions inside a session.

### Database Model

No central database, schema, or migration system was found. Product state appears to be local-file based.

**Needs confirmation:** Whether any hosted sync, cloud storage, telemetry, or team storage exists outside this repository.

---

## 9. Notifications, Emails, or Automations

### Emails

No automated email flows were found.

### Push or System Notifications

No push, mobile, or operating-system notification flows were found.

### In-Product Prompts

NanoAgent shows selection prompts, confirmation prompts, text prompts, secret prompts, permission prompts, error messages, and live activity messages.

### Automatic Section Titles

NanoAgent starts background title generation after the first user prompt in a section. The generated title is short, local to the section, and persisted when successful.

### Automatic Lesson Memory

NanoAgent can observe some failed commands or tools and later save a resolved lesson when a successful follow-up indicates a reusable fix.

### Lifecycle Hooks

Users can configure local commands to run before or after task, tool, file, shell, web, memory, permission, and delegation events.

### Tool Audit Logging

When enabled, NanoAgent writes local audit records for completed tool calls. This is disabled by default.

### MCP Discovery

Configured MCP servers are discovered locally at startup or when tools initialize. Available MCP tools are surfaced to the agent and listed in the MCP command.

---

## 10. Settings and Configuration

### User-Facing Settings

Confirmed user-facing settings include:

- Provider selection.
- API key.
- Custom provider base URL.
- Active model.
- Thinking mode: on or off.
- Active profile.
- Workspace selection.
- Section selection and new section creation.
- Session permission overrides.
- Undo/redo for tracked edits.
- Local workspace instructions.
- Workspace skills.
- Workspace custom agents.
- MCP server configuration.
- Lesson memory settings.
- Tool audit settings.
- Lifecycle hook settings.
- Sandbox and permission policies.

### Desktop-Exposed Settings

The desktop app visibly exposes:

- Recent workspace selection.
- Section selection.
- New section creation.
- Model selection.
- Thinking on/off.
- Profile selection for build, plan, and review.
- Permission override creation.
- Help, Models, Permissions, and Rules commands.
- Undo and Redo.

### Terminal-Exposed Settings

The terminal exposes:

- CLI startup options for interactive mode, prompt input, section resume, profile, and thinking.
- Slash commands for configuration, model switching, profile switching, thinking, permissions, rules, MCP, undo, redo, and exit.
- One-turn subagent handoff with `@agent-name`, plus model-driven `agent_delegate` and `agent_orchestrate` tool use from primary profiles.

### Advanced Local Configuration

Advanced behavior is configured locally for:

- Conversation request timeout, history limits, and tool-round limits.
- Default model selection.
- Model discovery cache duration.
- Permission defaults and rule stack.
- Built-in and custom shell allow/deny command patterns.
- Sandbox mode.
- Memory caps and write policy.
- Tool audit caps and redaction policy.
- Lifecycle hooks, timeout, filters, and output caps.
- MCP transports, timeouts, tool filters, headers, environment settings, and approval modes.

### Configuration Mismatches

The current code supports thinking mode as on/off. The README still references broader thinking effort values. This needs product and documentation alignment.

---

## 11. Errors, Empty States, and Edge Cases

- **No workspace selected:** Desktop shows "No project open" and "No folder selected." Run and workspace commands are disabled.
- **Empty prompt:** The Run action is disabled.
- **Agent already working:** New prompts, section changes, and most commands are blocked until work finishes.
- **Invalid folder:** Desktop ignores blank or missing folder paths.
- **Duplicate workspace:** Desktop selects the existing workspace entry instead of adding a duplicate.
- **No saved sections:** The Sections list shows a count of zero and no section cards.
- **Malformed or unreadable sections:** Invalid section records are skipped.
- **Wrong-workspace section:** Sections for another workspace are skipped in desktop and can block resume in terminal.
- **Invalid section ID:** Resume fails when a section ID is not a valid GUID.
- **Incomplete provider setup:** NanoAgent asks whether to reconfigure or cancel startup.
- **Invalid API key input:** Empty API keys are rejected.
- **Invalid custom provider URL:** Empty, relative, non-HTTP, query-string, or fragment URLs are rejected.
- **No usable models:** Startup/model discovery can fail.
- **Unavailable model:** The user is told to use `/models` to see valid choices.
- **Ambiguous model request:** A partial model match can be ambiguous and requires a more specific model ID.
- **Unknown command:** The user is told to use `/help`.
- **No MCP servers:** The MCP command reports that no servers are configured.
- **No MCP tools:** The MCP command reports that no tools are available.
- **Permission required:** The user must approve the action before it proceeds.
- **Permission denied:** The action is blocked and a denial result is returned.
- **Path outside workspace:** File and shell working-directory operations are blocked.
- **Read-only profile or sandbox:** Edits and unsafe/mutating shell behavior are blocked.
- **Shell escalation without justification:** The request is rejected.
- **Unsupported shell sandbox:** The command fails closed with guidance to use approved escalation or full access.
- **Pseudo-terminal unsupported:** Shell execution returns a failure result instead of silently falling back.
- **Web request failure:** Web operations return warnings and empty results where possible.
- **Corrupt desktop settings:** Recent project settings are ignored and desktop starts with an empty recent list.
- **Raw backend errors in desktop:** Desktop may display exception messages directly as NanoAgent messages and activity errors.
- **File open failure from markdown:** Desktop silently ignores failed file-open attempts.

---

## 12. Product Limitations

### Confirmed Limitations From Current Codebase

- No account system, team management, organization roles, billing, or admin console was found.
- No hosted web app, public API, database schema, or migrations were found.
- No email, push notification, or reminder system was found.
- The desktop app is a single-window product rather than a multi-page application.
- Desktop has no visible dedicated Settings screen.
- Desktop recent workspaces can be added and selected, but no visible remove or rename action was found.
- Desktop sections can be created and selected, but no visible rename, delete, export, search, or share action was found.
- Desktop profile selection appears limited to build, plan, and review even though custom profiles can exist.
- Desktop does not expose visual management for skills, custom agents, MCP servers, memory, audit logging, lifecycle hooks, or full permission policies.
- Thinking mode is on/off in the code, while the README references broader thinking effort values.
- API keys are user-managed; no OAuth, hosted credential broker, or team credential management was found.
- Model discovery depends on provider availability and usable model-list responses.
- Strict OS-level shell sandboxing is not available on all platforms. Windows shell sandboxing for ReadOnly and WorkspaceWrite appears unsupported and fails closed unless escalation or full access is used.
- Local unreadable or malformed sections, skills, agents, memory, settings, and configuration can be skipped without a rich recovery UI.
- Audit logs are disabled by default and no visual audit viewer was found.
- Secret redaction is pattern-based and not a complete data-loss-prevention guarantee.
- No integrated desktop auto-updater was confirmed.
- A VS Code product surface could not be confirmed from inspected source.
- Undo/redo applies to tracked file edits, not arbitrary side effects from shell commands or external tools.

### Needs Confirmation

- Whether NanoAgent is intended as a solo-developer tool, team tool, open-source developer tool, commercial product, or a mix.
- Whether cloud sync, collaboration, enterprise policy, or hosted management exists outside this repository.
- Whether future desktop releases should expose all advanced local configuration visually.
- Whether the VS Code folder represents a planned extension, generated artifact, or unsupported product surface.

---

## 13. Suggested Product Improvements

### Product Clarity

- Align README, terminal help, desktop controls, and documentation around the current thinking mode model.
- Add a concise privacy section explaining what stays local and what is sent to the selected model provider.
- Add a platform support matrix for desktop installers, CLI support, shell sandbox behavior, and pseudo-terminal support.
- Clarify whether "sections" should be called sessions, threads, tasks, or sections in user-facing language.

### Onboarding and Setup

- Add a first-run setup checklist covering provider, API key, model discovery, workspace selection, permissions, and first recommended prompt.
- Add recovery guidance when provider setup is incomplete, model discovery fails, or configuration files are malformed.
- Add recommended provider/model guidance for first-time users. **Needs confirmation** from product owner.

### Desktop Usability

- Add a dedicated Settings screen for provider, model, profile, thinking, permissions, MCP, memory, hooks, and audit settings.
- Add recent workspace management: remove, rename display label, clear missing workspaces, and pin.
- Add section management: rename, delete, search, pin, duplicate, export, and share.
- Add a command palette for Help, Models, Permissions, Rules, MCP, Undo, Redo, Profile, Thinking, and section actions.
- Surface custom agents and skills in the desktop UI with validation warnings.

### Safety and Trust

- Add a clearer permission preview before edits, patches, shell escalation, memory writes, and MCP calls.
- Show a structured diff preview for file changes before approval.
- Add an audit viewer for tool calls, permission decisions, memory writes, hook failures, and MCP activity.
- Improve user-facing error messages so they provide recovery steps instead of raw exception text.

### Workflow Completeness

- Add a validation panel that summarizes commands run, pass/fail state, and remaining risks.
- Add transcript and section summary export.
- Add templates for workspace instructions, skills, custom agents, MCP servers, and lifecycle hooks.
- Add better discoverability for terminal-only commands such as `/clear`, `/ls`, and `/read`.

### Documentation

- Publish persona-based quickstarts for bug fixing, feature implementation, planning, code review, MCP setup, permissions, custom agents, workspace skills, and memory.
- Document retention behavior for sections, memory, audit logs, and recent projects.
- Document the difference between read-only review/planning, build mode, subagents, and permission overrides.

---

## 14. Open Questions

- What is the intended market positioning: solo developer tool, team tool, enterprise-controlled local agent, or open-source utility?
- What is the official privacy promise for prompts, code excerpts, tool output, model requests, memory, and audit logs?
- Which providers and models should be recommended to new users?
- Should desktop expose full management for providers, custom agents, skills, MCP, memory, hooks, audit logs, and permissions?
- Should "sections" remain the user-facing term, or should the product use sessions, threads, tasks, or another label?
- Should sections be shareable or strictly local?
- What is the expected retention policy for recent projects, sections, conversation history, memory, logs, and audit records?
- Should read-only planning and review be marketed as separate modes or as permission presets?
- Should team-level policy presets exist for safe commands, denied commands, MCP tools, memory writes, and sandbox mode?
- What should happen when provider model discovery fails: block startup, use cached models, or allow manual model entry?
- Should missing or corrupt local files be silently ignored or surfaced with recovery actions?
- Is a desktop auto-update flow planned?
- Is a VS Code extension planned or supported?
- Should MCP configuration include a template library, marketplace, or validation wizard?
- What auditability level is required for professional or enterprise use?
- Should one-shot terminal mode support richer structured output for automation?
