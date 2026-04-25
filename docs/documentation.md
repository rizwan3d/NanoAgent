# NanoAgent Product Documentation

**Product type:** Local-first AI coding agent for desktop and terminal workflows  
**Audience:** Founders, product managers, designers, support teams, engineering leads, and users evaluating the product  
**Source of truth:** Current repository inspection

## Executive Summary

NanoAgent is a local-first coding assistant that helps software teams inspect code, make targeted edits, run build and test commands, manage model/provider settings, and preserve work across local workspace sections. The product is delivered as both a desktop application and a terminal experience. Its strongest value is controlled autonomy: users can let an AI agent work directly in a local repository while maintaining permissions, sandboxing, profile modes, and local memory controls.

## Inspection Scope

The repository inspection covered the product README, solution structure, desktop interface, terminal interface, command surface, onboarding flow, provider and model selection, permission and sandbox behavior, local workspace storage, memory, lifecycle hooks, MCP configuration, custom profiles, workspace skills, visible tests, and existing product-facing documentation.

No web routes, public product APIs, database schema, migrations, hosted admin screens, or email systems were found in the inspected repository surface.

Where behavior could not be confirmed from the current codebase, it is marked as **Needs confirmation**.

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

---

## 1. Product Name

**Product name:** NanoAgent

**Tagline / positioning:** A local coding agent for desktop and terminal workflows.

**Confidence:** Confirmed from the repository name, README, desktop window title, and user-facing product copy.

**Needs confirmation:** The public command naming should be standardized. Current materials show `nanoai` as the quick-start command and also refer to a `nano` command after installation.

---

## 2. Product Summary

### What the product does

NanoAgent helps users complete software engineering work inside a local workspace. It can read and search files, edit code, apply focused patches, run build and test commands, use configured model providers, remember local lessons, and operate from either a desktop UI or terminal UI.

### Who it is for

NanoAgent is primarily for software developers and technical teams who want an AI agent that can work directly with local repositories while remaining under explicit user control.

### Main problem it solves

Developers often lose time switching between chat assistants, editors, terminals, documentation, and code review workflows. NanoAgent consolidates those activities into a local agent experience that can inspect, act, validate, and remember workspace-specific lessons.

### Core value proposition

NanoAgent provides a local-first coding agent with practical autonomy, provider choice, workspace awareness, permission safeguards, session history, and terminal/desktop access.

### Product positioning

NanoAgent should be described as a local-first AI coding assistant rather than a cloud IDE, project management tool, or general chatbot.

---

## 3. Target Users

### Individual software developers

Developers can open local projects, ask the agent to inspect code, fix bugs, add features, run tests, and summarize outcomes.

### Engineering leads and senior developers

Leads can use planning and review profiles to assess changes, investigate risks, and guide safer implementation workflows.

### Code reviewers

Reviewers can run read-only review sessions focused on bugs, regressions, edge cases, missing validation, and missing tests.

### DevOps and build engineers

Build-focused users can ask NanoAgent to run build and test commands, inspect failures, and preserve reusable lessons about toolchain issues.

### Founders and technical product owners

Founders can use NanoAgent to accelerate product implementation while keeping source code local and controlling write permissions.

### Support or documentation teams with technical repositories

Technical support and documentation teams can inspect repository behavior, generate explanations, and use workspace instructions or skills for repeatable support workflows.

---

## 4. Core Features

### 4.1 First-run provider onboarding

**User benefit:** Gets users connected quickly to an AI model provider.

**How the user uses it:** On first launch, the user selects OpenAI, Google AI Studio, Anthropic, or an OpenAI-compatible provider. The user then enters the required API key and, for custom providers, a base URL.

**Expected outcome:** NanoAgent saves local provider settings and can start a model-backed session.

**Limits and edge cases:** API key cannot be empty. Custom base URL must be absolute, use HTTP or HTTPS, and cannot include a query string or fragment. Incomplete local setup triggers a reconfiguration prompt.

### 4.2 Model discovery and active model selection

**User benefit:** Lets users choose the model used for future prompts.

**How the user uses it:** The app discovers models from the configured provider, selects a usable model, and exposes model switching through desktop controls and terminal commands.

**Expected outcome:** The active session runs with the selected model.

**Limits and edge cases:** If the provider returns no usable models, model discovery fails. Duplicate model IDs are ignored. Availability depends on the provider account and network/API access.

### 4.3 Desktop workspace management

**User benefit:** Makes local projects easy to reopen and manage from a visual interface.

**How the user uses it:** Users click **Open** to select a local folder. Recent projects appear in the Workspaces list.

**Expected outcome:** The selected workspace becomes the active context for chat, file operations, commands, sections, and settings.

**Limits and edge cases:** Invalid or missing folders are ignored. No remove or rename action for recent projects was visible in the desktop UI.

### 4.4 Conversation sections and history

**User benefit:** Allows users to resume previous work in the same workspace.

**How the user uses it:** Users select a section from the Sections list or start a new section. Terminal users can resume with a section identifier.

**Expected outcome:** Conversation history, section title, model, and workspace association are restored when available.

**Limits and edge cases:** Sections are stored locally. Missing, unreadable, corrupt, or wrong-workspace section records are skipped. Section IDs must be valid GUIDs.

### 4.5 Chat-based coding assistance

**User benefit:** Turns product requests, bug reports, and technical questions into guided coding work.

**How the user uses it:** Users type a prompt in the desktop input area or terminal and run it against the active workspace.

**Expected outcome:** NanoAgent responds with analysis, code changes, validation summaries, and tool output when applicable.

**Limits and edge cases:** Run is disabled when there is no selected project, the prompt is empty, or another operation is running. Some runs can complete with no visible output.

### 4.6 Terminal workflow

**User benefit:** Supports keyboard-first developers who prefer a terminal UI.

**How the user uses it:** Users launch the terminal command, complete onboarding if needed, enter prompts, use slash commands, and resume sections.

**Expected outcome:** A live terminal experience shows conversation, provider/model status, progress, and exit/resume information.

**Limits and edge cases:** Terminal behavior depends on OS terminal capabilities. Startup can fail if configuration, section, provider, or terminal prerequisites are invalid.

### 4.7 File operations

**User benefit:** Allows the agent to inspect and change code directly in the local workspace.

**How the user uses it:** The agent can search files, read files, write files, delete files, and apply patches when permissions allow.

**Expected outcome:** Users get focused repository changes without manually copying code between tools.

**Limits and edge cases:** Paths resolving outside the workspace are denied. Writes, deletes, and patches generally require approval unless permission rules allow them.

### 4.8 Shell command execution

**User benefit:** Lets NanoAgent build, test, lint, and inspect projects without leaving the workflow.

**How the user uses it:** The agent runs workspace shell commands. Users can allow safe command patterns or deny risky ones.

**Expected outcome:** Build/test loops can be completed in the same session and summarized back to the user.

**Limits and edge cases:** Dangerous commands are denied by built-in rules. Some shell commands require approval. Read-only and workspace-write sandbox behavior is OS-dependent; unsupported platforms may fail closed unless escalated or unrestricted mode is used.

### 4.9 Permission controls and sandboxing

**User benefit:** Keeps users in control of what the agent can read, change, execute, or access.

**How the user uses it:** Users rely on default rules, configure permissions, approve prompts, or use desktop Allow/Deny override controls.

**Expected outcome:** Agent actions are allowed, denied, or routed through approval based on tool type, target, rule order, and sandbox mode.

**Limits and edge cases:** Modes include Allow, Ask, and Deny. Sandbox modes include ReadOnly, WorkspaceWrite, and DangerFullAccess. `.env`-style files are denied by default for reads.

### 4.10 Agent profiles

**User benefit:** Lets users switch behavior based on the task.

**How the user uses it:** Users select build, plan, or review from desktop controls or terminal commands. Subagents can be invoked for focused delegated work.

**Expected outcome:** Build mode supports implementation. Plan and review modes stay read-only and use safe inspection behavior.

**Limits and edge cases:** The desktop profile dropdown appears limited to build, plan, and review. Custom profile visibility in desktop controls needs confirmation.

### 4.11 Subagent delegation

**User benefit:** Improves focus by assigning bounded investigation or implementation tasks to specialized agents.

**How the user uses it:** Users or the primary agent invoke subagents such as general or explore, or workspace-defined custom agents.

**Expected outcome:** Subtasks can be handled with a narrower scope and returned to the main conversation.

**Limits and edge cases:** Plan and review profiles do not delegate to implementation-capable agents by default. Custom agents must be defined locally and correctly formatted.

### 4.12 Workspace custom agents

**User benefit:** Allows teams to create local agent behaviors for repeatable workflows.

**How the user uses it:** Users add markdown-defined agents under the workspace configuration area with optional name, mode, description, edit behavior, shell behavior, and tool settings.

**Expected outcome:** NanoAgent can expose custom primary agents or subagents tailored to a repository.

**Limits and edge cases:** Malformed, inaccessible, or missing custom agent files are ignored. Defaults are conservative: subagent mode, read-only edits, and safe shell inspection.

### 4.13 Workspace skills

**User benefit:** Helps the agent follow repository-specific playbooks without overloading every prompt.

**How the user uses it:** Users define skills in the workspace configuration area. NanoAgent sees skill names/descriptions for routing and loads full instructions only when relevant.

**Expected outcome:** The agent can use a targeted skill for tasks such as framework-specific build/test/review guidance.

**Limits and edge cases:** Descriptions and loaded instructions are length-limited. Duplicate skill names are deduplicated. Missing or unreadable skill files are ignored.

### 4.14 Workspace instructions

**User benefit:** Lets teams persist repo guidance without repeating it in every prompt.

**How the user uses it:** Users add workspace instruction documents in supported root locations.

**Expected outcome:** NanoAgent includes those instructions in the session behavior.

**Limits and edge cases:** Exact precedence and conflict handling across multiple instruction sources needs confirmation.

### 4.15 Lesson memory

**User benefit:** Reduces repeated mistakes in the same workspace.

**How the user uses it:** NanoAgent stores and retrieves local lessons about recurring mistakes, failed commands, fixes, and reusable rules.

**Expected outcome:** Relevant lessons can influence future turns and reduce repeated build/tool failures.

**Limits and edge cases:** Memory is local, capped by settings, redacted by default, and can be disabled. Manual writes require approval by default.

### 4.16 MCP server tools

**User benefit:** Extends NanoAgent with external tool capabilities configured by the user or workspace.

**How the user uses it:** Users configure MCP servers in local profile configuration and inspect loaded servers/tools with the MCP command.

**Expected outcome:** Available MCP tools appear to the agent as additional capabilities.

**Limits and edge cases:** No MCP servers means no MCP tools. Servers can be disabled, unavailable, filtered, or require approval depending on configuration.

### 4.17 Lifecycle hooks

**User benefit:** Allows local automation around agent and tool events.

**How the user uses it:** Users configure commands to run before/after tasks, tool calls, file operations, shell commands, web requests, memory actions, and delegation events.

**Expected outcome:** Teams can enforce checks, run custom scripts, or collect local process signals.

**Limits and edge cases:** Before hooks block by default on failure; after hooks continue by default unless configured otherwise. Hooks time out and can be filtered by event, tool, path, or shell command pattern.

### 4.18 Undo and redo for file edits

**User benefit:** Provides a safety net for agent-made file changes.

**How the user uses it:** Users click Undo/Redo in desktop controls or use terminal commands.

**Expected outcome:** Recent tracked file edit transactions can be rolled back or reapplied.

**Limits and edge cases:** Only tracked edit transactions are covered. Exact retention depth and coverage across all edit tools needs confirmation.

### 4.19 Secret redaction and audit controls

**User benefit:** Reduces the risk of exposing credentials in logs, memory, and displayed tool output.

**How the user uses it:** NanoAgent redacts common secret patterns and can optionally write local tool audit records.

**Expected outcome:** Sensitive values are less likely to appear in stored product artifacts.

**Limits and edge cases:** Redaction is pattern-based and should not be treated as a complete data-loss-prevention guarantee. Audit logging is disabled by default.

---

## 5. User Journeys

### 5.1 First run setup

1. Launch NanoAgent from desktop or terminal.
2. Choose a provider: OpenAI, Google AI Studio, Anthropic, or OpenAI-compatible provider.
3. Enter the provider API key. For a custom provider, enter a valid base URL.
4. NanoAgent saves local configuration and discovers available models.
5. The product opens a usable session with a selected model.

### 5.2 Open a workspace and run a task in desktop

1. Open the desktop app.
2. Click **Open** and select a local repository folder.
3. Confirm the selected workspace in the top bar and conversation header.
4. Type a prompt such as a bug fix, review request, or investigation question.
5. Click **Run**.
6. Review conversation output, tool output, activity updates, and any approval prompts.
7. Use **Undo** if a tracked edit should be rolled back.

### 5.3 Resume previous work

1. Select a workspace from the Workspaces list.
2. Choose an existing section from the Sections list.
3. NanoAgent loads the section conversation history and active session details.
4. Continue the conversation from the restored context.

### 5.4 Switch model, thinking, or profile

1. Open a workspace session.
2. Select a model, thinking mode, or profile from the Controls panel.
3. Click **Apply** for the relevant control.
4. NanoAgent updates the active session and uses the new choice for subsequent prompts.

### 5.5 Plan before implementation

1. Switch to the plan profile.
2. Ask for an implementation plan or investigation.
3. NanoAgent inspects safely without edits or mutating shell commands.
4. Review the plan, risks, assumptions, and validation path.
5. Switch to build profile when implementation is ready.

### 5.6 Review code without editing

1. Switch to the review profile.
2. Ask NanoAgent to review changes, a branch, or a work area.
3. NanoAgent searches and inspects code without editing.
4. Review findings, risks, missing tests, and unresolved assumptions.

### 5.7 Permission approval and override

1. NanoAgent requests a potentially sensitive action such as editing a file, running a shell command, using a network request, writing memory, or invoking MCP tools.
2. The user approves, denies, or cancels the prompt.
3. For repeated behavior, the user can add session-scoped Allow or Deny overrides from the desktop controls or terminal commands.
4. NanoAgent applies the updated rule stack to future actions in that session.

### 5.8 Use terminal workflow

1. Launch the terminal command.
2. Complete onboarding or load existing provider settings.
3. Enter prompts or slash commands.
4. Use section resume commands when returning to previous work.
5. Exit when finished; NanoAgent prints section resume details when available.

### 5.9 Configure advanced workspace behavior

1. Add workspace instructions, skills, custom agents, MCP servers, memory settings, permission rules, or lifecycle hooks in local configuration.
2. Start or refresh a session.
3. NanoAgent loads the applicable local configuration.
4. Use commands and prompts to inspect active settings and tools.

---

## 6. Screens / Pages / Interfaces

### Desktop application

NanoAgent has a single main desktop window with the following visible areas:

- **Top bar:** Shows the NanoAgent brand, active project name/path, and status such as Ready or Working.
- **Left sidebar - Workspaces:** Lists local recent projects and includes a **+ Open** button for selecting a folder.
- **Left sidebar - Sections:** Lists saved workspace sections, shows last updated timing, model/turn summary information, and includes **+ New** to start a fresh section.
- **Conversation area:** Shows user messages, NanoAgent responses, tool messages, markdown rendering, status notes, and current workspace context.
- **Prompt composer:** A multi-line input area with **Ask NanoAgent...** placeholder and a **Run** button.
- **Selection prompt overlay:** Displays action choices, descriptions, default option label, optional countdown, and Cancel when available.
- **Text prompt overlay:** Collects user input such as API keys or requested text and includes Submit/Cancel controls.
- **Controls panel:** Includes Refresh, Model selector, Thinking selector, Profile selector, Help, Models, Permissions, Rules, Permission Override, Undo, Redo, and workspace details.
- **Activity panel:** Shows operational status, command/activity messages, errors, and progress items.
- **Working status strip:** Appears while NanoAgent is running and displays elapsed time and estimated token count.

### Terminal interface

The terminal interface provides an interactive UI for prompt entry, live status, onboarding prompts, conversation display, progress, and session resume hints.

It supports slash commands for help, configuration, model switching, profile switching, thinking mode, permissions, rules, MCP, undo/redo, and exit.

It also supports multi-line input and section resume options.

### Routes, web pages, and API endpoints

No product web pages, HTTP routes, controllers, public APIs, or hosted admin screens were found in the inspected repository.

**Needs confirmation:** Whether a separate repository or hosted service exists outside this codebase.

---

## 7. Permissions and Roles

### Role model

No user accounts, organization roles, admin roles, billing roles, or protected web routes were found. NanoAgent appears to be a local single-user product.

### Operational access levels

Access is governed by permission modes, sandbox modes, agent profiles, and local configuration rather than user roles.

### Permission modes

- **Allow:** Permits an action.
- **Ask:** Requires user approval before the action proceeds.
- **Deny:** Blocks the action.

### Sandbox modes

- **ReadOnly:** Blocks write-like actions and mutating shell activity.
- **WorkspaceWrite:** Allows workspace-scoped write behavior under configured rules.
- **DangerFullAccess:** Removes sandbox restrictions when configured.

### Built-in profile permissions

- **Build:** Implementation-capable coding profile.
- **Plan:** Read-only planning profile for safe inspection and planning.
- **Review:** Read-only review profile focused on findings and risk.
- **General:** Implementation-capable subagent for bounded delegated work.
- **Explore:** Read-only subagent for focused investigation.

### Built-in safety rules

Reads are generally allowed. Write, delete, patch, edit, agent, MCP, and external-directory actions generally ask. `.env`-style reads are denied. Safe build/test command patterns are allowed. Dangerous shell command patterns are denied.

### Session overrides

Users can add session-scoped allow or deny overrides for a tool/tag and optional target pattern.

### Memory and MCP permissions

Manual memory writes require approval by default, and memory can be disabled. MCP tools can be filtered and can use default or per-tool approval modes.

---

## 8. Data and Content Model

NanoAgent appears to use local files and local settings rather than a central product database.

### Main product objects

- **User provider configuration:** Stores selected provider and preferred/active model information locally.
- **API key secret:** Stores the selected provider credential locally through the product secret-storage layer.
- **Workspace project:** A local folder opened by the user. Workspaces are remembered in the desktop recent-project list.
- **Section:** A saved conversation/work session tied to a specific workspace. Sections contain an ID, title, workspace path, active model, last updated time, and turns.
- **Conversation turn:** A user prompt and NanoAgent response with associated tool activity and estimated metrics.
- **Model:** A provider-returned model identifier that can be selected as the active model for a session.
- **Agent profile:** A behavior mode that controls agent purpose, edit ability, shell behavior, and available tools.
- **Subagent:** A focused agent profile that can handle bounded delegated work.
- **Permission rule:** A local rule that matches tools/tags and optional target patterns to Allow, Ask, or Deny behavior.
- **Workspace instruction:** Persistent guidance loaded from supported workspace instruction documents.
- **Skill:** A locally defined task playbook with name, description, and body instructions loaded on demand.
- **MCP server:** A configured external tool server with transport settings, environment settings, tool filters, and approval settings.
- **Lesson memory entry:** A reusable local lesson about a trigger, problem, fix/lesson, tags, tool/command context, and fixed status.
- **Lifecycle hook:** A local automation rule that runs a command around selected agent/tool events.
- **Audit record:** Optional local record of completed tool calls when audit logging is enabled.

### Database model

No central database, schema, migrations, or hosted storage layer were found. Product state appears to be local-file based.

**Needs confirmation:** Whether cloud sync or hosted storage exists outside this repository.

---

## 9. Notifications, Emails, or Automations

### Emails

No automated email flows were found.

### Push or system notifications

No push, mobile, or system notification flows were found.

### In-product prompts

NanoAgent shows selection, text, secret, confirmation, and permission prompts during onboarding and action approval.

### Lifecycle hooks

Users can configure local commands to run before or after task, tool, file, shell, web, memory, and delegation events. Before hooks block by default on failure. After hooks continue by default unless configured otherwise.

### Automatic lesson memory

NanoAgent can observe failed tools or shell commands and save resolved lessons when later success suggests a reusable fix.

### Tool audit logging

Optional local audit records can be written for completed tool calls when enabled.

### MCP tool discovery

Configured MCP servers can be loaded and exposed as tools, with availability shown through the MCP command.

### Section title generation

The backend starts title generation for a session after user input.

**Needs confirmation:** Exact timing, model usage, and user visibility of section title generation.

---

## 10. Settings and Configuration

### User-facing settings

- Provider selection: OpenAI, Google AI Studio, Anthropic, or OpenAI-compatible provider.
- API key and, when applicable, custom provider base URL.
- Active model selection.
- Active agent profile: build, plan, review, and supported custom profiles/subagents.
- Thinking mode.
- Workspace selection and recent project list.
- Section selection and new section creation.
- Permission override entries for Allow/Deny decisions.
- Undo/redo of tracked edit transactions.
- Workspace skills, custom agents, MCP servers, lifecycle hooks, memory settings, and audit settings through local configuration files.

### Internal or technical configuration

- Logging levels.
- Conversation system prompt, request timeout, maximum history turns, and maximum tool rounds.
- Model-selection cache duration.
- Permission defaults, rule stack, shell allow/deny patterns, and sandbox mode.
- Memory caps, prompt character limits, redaction behavior, and write-approval policy.
- Tool audit caps and redaction behavior.
- Lifecycle hook timeout, output limits, command execution behavior, and event filters.
- MCP transport, startup timeout, tool timeout, environment variables, headers, and tool filters.

### Configuration mismatch needing product decision

The desktop UI currently exposes a simple on/off thinking choice. Some product copy references broader thinking-effort levels such as none, minimal, low, medium, high, or xhigh. This should be aligned across README, terminal help, desktop controls, and user documentation.

---

## 11. Errors, Empty States, and Edge Cases

- **No workspace selected:** Desktop shows “No project open” and “No folder selected.” Run and workspace commands are disabled until a project is selected.
- **Empty prompt:** Run is disabled when the prompt is blank.
- **Operation already running:** Most commands and new-section actions are disabled while NanoAgent is working.
- **Invalid folder path:** Adding a desktop project silently returns when the path is blank or does not exist.
- **Duplicate recent project:** Opening an already listed workspace selects the existing entry instead of adding a duplicate.
- **Missing section directory:** Sections list is empty when no local section storage exists.
- **Unreadable or corrupt section file:** Invalid, inaccessible, or malformed section records are skipped.
- **Wrong workspace section:** Sections whose stored workspace path does not match the active workspace are ignored. Terminal startup can fail when a section workspace mismatch is detected.
- **Invalid section ID:** Desktop backend rejects non-GUID section IDs.
- **Incomplete provider setup:** Startup detects partial provider/API key state and asks whether to reconfigure or cancel.
- **Invalid onboarding input:** Blank API keys, blank custom base URLs, relative URLs, unsupported schemes, query strings, and fragments are rejected.
- **No usable models:** If the configured provider returns no usable models, model discovery fails.
- **No MCP servers/tools:** The MCP command reports no configured servers or available MCP tools.
- **Permission required:** Actions matching Ask require approval before continuing.
- **Permission denied:** Deny rules block actions and return a decision message.
- **Outside workspace path:** File and patch operations resolving outside the workspace are denied.
- **Read-only sandbox:** Write-like actions and non-safe shell commands are blocked in read-only sandbox mode unless valid escalation is requested and approved.
- **Shell escalation without justification:** Escalated shell requests require a justification.
- **Unsupported sandbox runner:** Some shell sandbox modes fail closed on unsupported platforms unless an approved escalation or unrestricted mode is used.
- **Raw errors in UI:** When backend operations throw, the desktop app displays the exception message as a NanoAgent message and activity error.
- **Settings read failure:** Corrupt desktop recent-project settings are ignored and the app starts with an empty project list.

---

## 12. Product Limitations

### Confirmed limitations from current codebase

- No account system, organization management, billing, role-based access control, or hosted admin console was found.
- No web UI, HTTP routes, public product API, database schema, or migrations were found.
- The desktop app appears to be a single main-window experience rather than a multi-page application with dedicated settings screens.
- No built-in email, push notification, or reminder system was found.
- Recent projects can be added and selected, but no visible desktop action was found to remove or rename recent projects.
- Sections can be selected and newly started, but no visible desktop action was found to rename, delete, export, or share sections.
- Desktop profile options appear hard-coded to build, plan, and review, even though the product supports custom profiles and subagents through local configuration.
- The desktop thinking selector shows a simple on/off model, while product copy also references broader thinking-effort levels. This mismatch needs product decision and documentation alignment.
- Provider/model setup requires user-managed API keys; no OAuth, hosted credential broker, or team credential management was found.
- Sandbox behavior depends on operating system support. Windows and unsupported platforms may require approved escalation or unrestricted mode for shell execution under strict sandbox settings.
- Model discovery depends on provider availability and usable model-list responses.
- Local state can be skipped or reset silently when settings, sections, skills, agents, or memory files are unreadable or malformed.
- Audit logs are disabled by default and no visible audit viewer was found.
- Secret redaction is pattern-based and should not be marketed as a complete security guarantee.
- No integrated update checker, release manager, or auto-updater behavior was confirmed from the inspected product code.

### Assumptions or areas needing confirmation

- Whether NanoAgent is intended for solo developers only or for managed team/company deployments.
- Whether future hosted sync, collaboration, policy management, or admin controls are planned.
- Whether desktop should expose full custom profile, custom subagent, skill, MCP, memory, and hook management rather than relying on local configuration files.
- Whether the public CLI command should be positioned as `nanoai`, `nano`, or both.
- Whether all README-described thinking-effort levels remain part of the product roadmap or should be simplified to on/off in public docs.

---

## 13. Suggested Product Improvements

- **Add a first-run setup checklist:** Show provider, API key status, model discovery, workspace selection, permissions, and next recommended action in one guided screen.
- **Create a dedicated Settings screen:** Move provider/model/profile/thinking/permissions/MCP/memory/hook settings into a clear UI instead of relying mostly on commands and files.
- **Improve section management:** Add rename, delete, duplicate, pin, export, and search for sections.
- **Add recent-project management:** Let users remove missing workspaces, rename display labels, and clear history.
- **Clarify thinking modes:** Align README, terminal commands, desktop controls, and product language around either simple on/off thinking or multi-level thinking effort.
- **Expose custom agents and skills in the desktop UI:** List detected workspace agents/skills, show descriptions, and provide validation warnings for malformed local definitions.
- **Add permission preview and diff review:** Before writes or patches, show affected files, target paths, and a human-readable change summary.
- **Improve error messages:** Convert backend exceptions into user-facing recovery guidance, especially for provider, model, section, permission, and sandbox failures.
- **Add an audit viewer:** Provide a local screen for tool audit records, permission decisions, memory writes, and hook failures.
- **Add onboarding templates:** Provide sample workspace instructions, skills, custom agents, MCP configs, and hook recipes.
- **Add privacy guidance:** Make clear what stays local and what is sent to the selected model provider.
- **Add platform support matrix:** Document desktop installers, CLI support, shell sandbox behavior, pseudo-terminal support, and known limitations by OS.
- **Add export/share options:** Allow users to export a section summary, conversation transcript, audit trail, or implementation report.
- **Add tests/status surfaced to users:** When validation commands run, show a structured pass/fail validation panel instead of relying only on conversation text.
- **Add command palette:** A desktop command palette could expose Help, Models, Permissions, Rules, MCP, Undo, Redo, Profile, Thinking, and section commands consistently.
- **Strengthen documentation:** Publish persona-based quickstarts for bug fixing, code review, planning, MCP setup, workspace skills, and safe permission profiles.

---

## 14. Open Questions

- What is the canonical public product name and CLI command naming: NanoAgent, `nanoai`, `nano`, or a combination?
- Is NanoAgent positioned as a solo-developer tool, team tool, open-source developer tool, commercial product, or all of these?
- What privacy promise should be made regarding local code, prompts, tool output, and third-party model providers?
- Which model providers and model families should be recommended for first-time users?
- Should the desktop app support full profile, custom agent, skill, MCP, hook, memory, and audit configuration visually?
- Are workspace sections intended to be user-facing “threads,” “sessions,” “tasks,” or another product concept?
- Should sections be shareable/exportable, or strictly local?
- What is the intended retention policy for conversation history, sections, memory, and audit logs?
- Should read-only review/planning be marketed as separate modes or as permission presets?
- Should the product include team-level policy presets for safe commands, denied commands, MCP tools, and memory writes?
- What is the expected behavior when provider model discovery fails: block startup, fall back to cached models, or prompt for manual model entry?
- Should missing/corrupt local files be silently ignored or surfaced with recovery actions?
- Is there an intended auto-update path for desktop installers?
- Should MCP server setup include a marketplace, template library, or validation wizard?
- What level of auditability is required for users who run NanoAgent in professional or enterprise environments?