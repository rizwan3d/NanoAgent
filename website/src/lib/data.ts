// ── Site content data ──

export const siteConfig = {
  name: "NanoAgent",
  tagline: "Your AI coding agent that doesn't hide what it's doing",
  description:
    "NanoAgent is a local-first AI coding agent for desktop, terminal, and editor workflows. Understand code, plan changes, edit files, run validation, and review diffs — with you in control.",
  ogImage: "/assets/logo.png",
  github: "https://github.com/rizwan3d/NanoAgent",
  nuget: "https://www.nuget.org/packages/NanoAgent/",
  creator: "https://github.com/rizwan3d",
  sponsor: "https://alfain.co/",
  sponsorName: "ALFAIN Technologies",
  blog: "https://hackernoon.com/building-an-ai-coding-agent-that-doesnt-hide-what-its-doing",
  security: "https://github.com/rizwan3d/NanoAgent/blob/master/SECURITY.md",
  latestRelease: "https://github.com/rizwan3d/NanoAgent/releases/latest",
  changelog: "https://github.com/rizwan3d/NanoAgent/releases",
  issues: "https://github.com/rizwan3d/NanoAgent/issues",
  discussions: "https://github.com/rizwan3d/NanoAgent/discussions",
  readme: "https://github.com/rizwan3d/NanoAgent#readme",
  commits: "https://github.com/rizwan3d/NanoAgent/commits/master",
  appUrl: "https://app.getnanoai.com",
};

export interface NavLink {
  label: string;
  href: string;
}

export interface FeatureItem {
  icon: string;
  title: string;
  description: string;
}

export const whyFeatures: FeatureItem[] = [
  {
    icon: "🧩",
    title: "LSP-enabled",
    description:
      "Language Server Protocol integration gives the agent real symbols, types, and diagnostics — not guesses about your code.",
  },
  {
    icon: "🧠",
    title: "Repo memory",
    description:
      "Team knowledge lives in version-controlled .nanoagent/memory files you can read, review, and edit — not hidden agent notes.",
  },
  {
    icon: "⚙️",
    title: "Headless mode",
    description:
      "Non-interactive single-query mode for CI/CD pipelines, scripts, and automation. Supports piped stdin.",
  },
  {
    icon: "⌨️",
    title: "! and !! shell",
    description:
      "Run shell commands inline with ! and rerun the last one with !! — no need to switch terminals.",
  },
  {
    icon: "🛡️",
    title: "Sandboxed execution",
    description:
      "Tools and commands run inside a sandbox with permission gates for file edits, network access, and elevated operations.",
  },
  {
    icon: "🗺️",
    title: "Planning mode",
    description:
      "A read-only planning profile drafts a reviewable plan before a single file is touched.",
  },
  {
    icon: "📦",
    title: "SDK to embed in your app",
    description:
      "Drop NanoAgent into your own .NET application with the SDK on NuGet and drive it programmatically.",
  },
];

export const comparisonHeaders = [
  "Feature",
  "NanoAgent",
  "Codex",
  "Claude Code",
  "OpenCode",
  "Nanoai Code",
  "GitHub Copilot",
  "Aider",
] as const;

export const comparisonRows: { feature: string; values: string[] }[] = [
  { feature: "Local-first", values: ["✓", "◐", "◐", "✓", "◐", "–", "✓"] },
  { feature: "Repo memory", values: ["✓", "◐", "✓", "◐", "◐", "◐", "–"] },
  { feature: "VS Code extension", values: ["✓", "✓", "✓", "✓", "✓", "✓", "–"] },
  { feature: "Visual Studio", values: ["✓", "–", "–", "–", "–", "✓", "–"] },
  { feature: "JetBrains plugin", values: ["✓ (ACP)", "–", "✓", "✓", "✓", "✓", "–"] },
  { feature: "CI code reviews", values: ["✓", "✓", "✓", "–", "✓", "✓", "–"] },
  { feature: "15+ AI providers", values: ["✓", "–", "–", "✓", "✓", "–", "✓"] },
  { feature: "ACP protocol", values: ["✓", "–", "–", "✓", "✓", "–", "–"] },
  { feature: "Sandboxed execution", values: ["✓", "✓", "✓", "–", "–", "–", "–"] },
  { feature: "Open source", values: ["✓", "✓", "–", "✓", "✓", "–", "✓"] },
  { feature: "LSP", values: ["✓", "–", "–", "✓", "–", "✓", "–"] },
  { feature: "Direct shell & bg terminals", values: ["✓", "◐", "✓", "◐", "✓", "◐", "◐"] },
  { feature: "Subagents & orchestration", values: ["✓", "✓", "✓", "✓", "✓", "◐", "–"] },
  { feature: "Local codebase indexing", values: ["✓", "–", "–", "–", "✓", "◐", "◐"] },
  { feature: "Memory, audit & hooks", values: ["✓", "–", "✓", "–", "–", "–", "–"] },
  { feature: "SDK to embed in your app", values: ["✓", "–", "✓", "–", "–", "–", "–"] },
];

export const providers = [
  "OpenAI",
  "ChatGPT Plus / Pro",
  "Anthropic Claude Pro / Max",
  "GitHub Copilot",
  "OpenRouter",
  "Nano Code",
  "Cerebras",
  "Groq",
  "DeepSeek",
  "Anthropic",
  "Google AI Studio",
  "Ollama",
  "LM Studio",
  "Ollama Cloud",
  "OpenAI-compatible",
];

export const quickstartSteps = [
  {
    num: 1,
    title: "Install the CLI",
    tabs: [
      { id: "sh", label: "macOS / Linux", code: "# install\ncurl -fsSL https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.sh | bash\n\n# start\nnanoai" },
      { id: "ps", label: "Windows", code: "# install\nirm https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.ps1 | iex\n\n# start\nnanoai" },
    ],
  },
  {
    num: 2,
    title: "Pick a provider & go",
    note: "Launch and let onboarding walk you through setup — or preseed it and skip straight to a run:",
    tabs: [
      {
        id: "normal",
        label: "Normal run",
        code: "# launch — onboarding walks you through provider setup\nnanoai",
      },
      {
        id: "headless",
        label: "Headless",
        code: `# headless setup via environment variables
export NANOAGENT_PROVIDER="openai"
export NANOAGENT_MODEL="gpt-5-5"
export NANOAGENT_THINKING="on"
export NANOAGENT_API_KEY="PASTE_NEW_ROTATED_KEY_HERE"

nanoai -p "Say hello in one short line" --yes`,
      },
    ],
  },
];

export interface FooterColumn {
  title: string;
  links: { label: string; href: string }[];
}

export const footerColumns: FooterColumn[] = [
  {
    title: "Product",
    links: [
      { label: "Why NanoAgent", href: "/#why" },
      { label: "Features", href: "/features" },
      { label: "Compare", href: "/#compare" },
      { label: "Providers", href: "/#providers" },
      { label: "Quickstart", href: "/#start" },
    ],
  },
  {
    title: "Resources",
    links: [
      { label: "Documentation", href: "/docs" },
      { label: "Releases", href: siteConfig.latestRelease },
      { label: "Changelog", href: siteConfig.changelog },
      { label: "NuGet SDK", href: siteConfig.nuget },
      { label: "Security", href: siteConfig.security },
    ],
  },
  {
    title: "Community",
    links: [
      { label: "GitHub", href: siteConfig.github },
      { label: "Issues", href: siteConfig.issues },
      { label: "Discussions", href: siteConfig.discussions },
      { label: "Blog", href: siteConfig.blog },
    ],
  },
  {
    title: "More",
    links: [
      { label: "About", href: siteConfig.readme },
      { label: "Creator", href: siteConfig.creator },
      { label: "@rizwan3d", href: siteConfig.creator },
    ],
  },
];

export const installTabs = [
  { id: "cli", label: "CLI", icon: "terminal" },
  { id: "vscode", label: "VS Code", icon: "vscode" },
  { id: "vs", label: "Visual Studio", icon: "vs" },
  { id: "desktop", label: "Desktop", icon: "desktop" },
];

export interface InstallPanel {
  id: string;
  subTabs?: { id: string; label: string }[];
  commands?: { id: string; cmd: string }[];
  actions?: { label: string; href: string; variant: "primary" | "ghost" }[];
  foot?: string;
  defaultSubTab?: string;
}

export const installPanels: InstallPanel[] = [
  {
    id: "cli",
    subTabs: [
      { id: "npm", label: "npm" },
      { id: "pnpm", label: "pnpm" },
      { id: "bun", label: "bun" },
      { id: "pw", label: "Powershell" },
      { id: "curl", label: "curl" },
    ],
    defaultSubTab: "npm",
    commands: [
      { id: "npm", cmd: "npm install -g nanoai-cli" },
      { id: "pnpm", cmd: "pnpm add -g nanoai-cli" },
      { id: "bun", cmd: "bun add -g nanoai-cli" },
      { id: "pw", cmd: "irm https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.ps1 | iex" },
      { id: "curl", cmd: "curl -fsSL https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.sh | bash" },
    ],
    actions: [
      { label: "See the quickstart", href: "#start", variant: "primary" },
      { label: "Latest release", href: siteConfig.latestRelease, variant: "ghost" },
    ],
  },
  {
    id: "vscode",
    commands: [
      { id: "vscode", cmd: "ext install rizwan3d.nanoagent" },
    ],
    actions: [
      {
        label: "Install for free",
        href: "https://marketplace.visualstudio.com/items?itemName=rizwan3d.nanoagent",
        variant: "primary",
      },
      { label: "View on GitHub", href: siteConfig.github, variant: "ghost" },
    ],
    foot: "Docked tool window drives the local nanoai CLI over ACP.",
  },
  {
    id: "vs",
    commands: [
      { id: "vs", cmd: "Extensions ▸ Manage Extensions ▸ search \"NanoAgent\"" },
    ],
    actions: [
      {
        label: "Get the VS extension",
        href: "https://marketplace.visualstudio.com/items?itemName=rizwan3d.nanoagent-vs",
        variant: "primary",
      },
      { label: "Read the docs", href: "/docs", variant: "ghost" },
    ],
    foot: "Docked tool window drives the local nanoai CLI over ACP.",
  },
  {
    id: "desktop",
    commands: [
      { id: "desktop", cmd: "Windows · macOS · Linux (x64 / arm64)" },
    ],
    actions: [
      { label: "Download desktop app", href: siteConfig.latestRelease, variant: "primary" },
      { label: "View on GitHub", href: siteConfig.github, variant: "ghost" },
    ],
    foot: "Verify with SHA256SUMS + build attestation.",
  },
];

export interface FeatureCategory {
  id: string;
  icon: string;
  title: string;
  description: string;
  cards: FeatureItem[];
}

export const featureCategories: FeatureCategory[] = [
  {
    id: "cli",
    icon: "🖥️",
    title: "CLI experience",
    description: "A keyboard-first agent that lives in your terminal via the nanoai command.",
    cards: [
      { icon: "⌨️", title: "Interactive sessions", description: "Full terminal UI with persistent conversation history and a live activity stream of every action and reasoning step." },
      { icon: "↩️", title: "Session resume", description: "Pick up where you left off with --session <guid>; NanoAgent prints the resume command on exit." },
      { icon: "🛋️", title: "Rich CLI flags", description: "-p/--prompt, -y/--yes, --stdin, --session, --profile, --thinking, and --acp cover scripted and interactive runs." },
      { icon: "⚡", title: "F2 model switch", description: "Swap the active model mid-session from the terminal without breaking your flow." },
      { icon: "💬", title: "Slash commands", description: "Type / for inline suggestions — /help, /config, /models, /profile, /clear, /exit and many more." },
      { icon: "🧱", title: "One-command install", description: "Bash installer for macOS/Linux and a PowerShell installer for Windows — no build step required." },
    ],
  },
  {
    id: "headless",
    icon: "⚙️",
    title: "Oneshot & headless",
    description: "Non-interactive runs for scripts, pipelines, and automation.",
    cards: [
      { icon: "🎯", title: "One-shot prompts", description: "Run a single query with nanoai -p \"...\" and capture the response — ideal for scripts and cron jobs." },
      { icon: "⏩", title: "Piped stdin", description: "Feed context straight from a pipe with --stdin so the agent reads from upstream commands." },
      { icon: "✅", title: "Auto-approve", description: "-y/--yes skips interactive prompts for fully unattended, fail-closed automation runs." },
      { icon: "🔑", title: "Env-var setup", description: "Preseed everything with NANOAGENT_PROVIDER, NANOAGENT_MODEL, NANOAGENT_THINKING, NANOAGENT_REASONING and NANOAGENT_API_KEY." },
    ],
  },
  {
    id: "shell",
    icon: "⌨️",
    title: "Shell & terminals",
    description: "Run commands without leaving the conversation.",
    cards: [
      { icon: "❗", title: "! direct shell", description: "Prefix a line with ! to run a local shell command directly — no agent round-trip, no approval gate." },
      { icon: "‼️", title: "!! background terminals", description: "Launch a background terminal with !! whose output streams live while the session keeps going." },
      { icon: "🛎️", title: "Terminal management", description: "Track running terminals with /terminals; concurrency and TTL limits are configurable per session." },
      { icon: "🛠️", title: "shell_command tool", description: "The agent executes system commands through a permission-gated tool with sandbox enforcement." },
    ],
  },
  {
    id: "tools",
    icon: "📂",
    title: "File, search & web tools",
    description: "The built-in toolset the agent uses to read your code and reach the web — every call permission-gated.",
    cards: [
      { icon: "📄", title: "File operations", description: "file_read, file_write, and file_delete read and edit workspace files under your permission policy." },
      { icon: "🔎", title: "Navigate & search", description: "directory_list, text_search, and search_files browse structure and find code by content or path." },
      { icon: "🌐", title: "web_search", description: "Network-gated web search lets the agent pull in current information when you allow it." },
      { icon: "🔗", title: "webfetch", description: "Fetch a URL over HTTP with approval — bring docs, issues, or specs straight into the conversation." },
      { icon: "🧭", title: "headless_browser", description: "Chromium-family browser automation for pages that need real rendering — covered by the network permission." },
      { icon: "🚫", title: ".nanoignore aware", description: "File tools respect .nanoignore so sensitive paths stay out of the agent's reach." },
    ],
  },
  {
    id: "acp",
    icon: "🔌",
    title: "ACP — Agent Client Protocol",
    description: "Drive the local CLI from any editor over a clean JSON-RPC channel.",
    cards: [
      { icon: "🔗", title: "nanoai --acp", description: "Starts a stdio-based JSON-RPC server speaking line-delimited messages over stdin/stdout." },
      { icon: "🏠", title: "Purely local", description: "No network transport — editors talk to the agent as a local child process, keeping data on your machine." },
      { icon: "🧵", title: "Session methods", description: "session/new, load, prompt, cancel, and close — one active session per process." },
      { icon: "🔐", title: "Optional token auth", description: "Require a token via NANOAGENT_ACP_AUTH_TOKEN; client mcpServers merge with your config." },
    ],
  },
  {
    id: "mcp",
    icon: "🧰",
    title: "MCP & Skills",
    description: "Extend the agent with external tools and reusable playbooks.",
    cards: [
      { icon: "🧩", title: "MCP servers", description: "Load Model Context Protocol servers over stdio or streamable HTTP, from user- or workspace-level config." },
      { icon: "🎚️", title: "Tool filtering", description: "enabledTools/disabledTools plus per-server startup and tool timeouts keep things tight." },
      { icon: "🔍", title: "/mcp inspection", description: "List active servers, custom providers, and the dynamic tools they expose at a glance." },
      { icon: "🧪", title: "Custom tools", description: "Any language that reads JSON stdin and writes stdout becomes a tool exposed as custom__<name>." },
      { icon: "📓", title: "Skills", description: "Drop task- or tool-specific playbooks in .nanoagent/skills (e.g. a dotnet or code-review SKILL.md)." },
      { icon: "💾", title: "Custom slash commands", description: "Save repeatable prompts in .nanoagent/commands with namespaces, front matter, and $ARGUMENTS." },
    ],
  },
  {
    id: "memory",
    icon: "🧠",
    title: "Repo memory",
    description: "Team knowledge that lives in version control — reviewable, not hidden.",
    cards: [
      { icon: "📚", title: "Reviewable memory files", description: "Knowledge lives in .nanoagent/memory — architecture, conventions, decisions, known-issues, and test-strategy markdown." },
      { icon: "🎓", title: "Lessons", description: "lessons.jsonl can auto-capture failures and fixes, then inject relevant lessons back into future prompts." },
      { icon: "📌", title: "AGENTS.md instructions", description: "Persistent project instructions via AGENTS.md, plus full or appended system-prompt overrides." },
      { icon: "🛡️", title: "Approval & redaction", description: "Memory writes require approval by default, and secret-looking values are redacted before storage." },
    ],
  },
  {
    id: "providers",
    icon: "🌐",
    title: "Model providers",
    description: "Use the model that fits your budget and policy — subscription, API key, or fully local.",
    cards: [
      { icon: "🔑", title: "Subscription sign-in", description: "OAuth into ChatGPT Plus/Pro and Anthropic Claude Pro/Max, plus device-code login for GitHub Copilot." },
      { icon: "🗝️", title: "API-key providers", description: "OpenAI, Anthropic, Google AI Studio, OpenRouter, Kilo Code, Cerebras, Groq, DeepSeek, and Ollama Cloud." },
      { icon: "💻", title: "Local providers", description: "Run on-device with Ollama (localhost:11434) or LM Studio for fully offline workflows." },
      { icon: "🔌", title: "OpenAI-compatible", description: "Point at any custom base URL and API key to plug in third-party or self-hosted endpoints." },
      { icon: "🔎", title: "Model discovery", description: "NanoAgent automatically discovers available models from your selected provider." },
      { icon: "👤", title: "Provider profiles", description: "Save and switch between provider configurations; onboarding fails closed and offers reconfiguration." },
    ],
  },
  {
    id: "reasoning",
    icon: "🎚️",
    title: "Model & reasoning controls",
    description: "Tune how the model thinks and switch it on the fly.",
    cards: [
      { icon: "🔄", title: "Model switching", description: "The /models picker, /use <model>, and F2 let you change models instantly." },
      { icon: "🧠", title: "Thinking mode", description: "/thinking on|off toggles whether the provider's extended reasoning is shown." },
      { icon: "📈", title: "Reasoning effort", description: "/reasoning sets effort from minimal through low/medium/high up to xhigh and max." },
      { icon: "🪙", title: "Token & cost tracking", description: "Cached input tokens are tracked separately from standard input for accurate pricing." },
    ],
  },
  {
    id: "agents",
    icon: "🤝",
    title: "Profiles & subagents",
    description: "Right-sized agents for building, planning, reviewing, and delegating.",
    cards: [
      { icon: "🏗️", title: "Primary profiles", description: "build for edit-enabled implementation, plan for read-only investigation, and review for findings-first code review." },
      { icon: "🔭", title: "Subagents", description: "general handles delegated implementation; explore does fast, read-only codebase discovery." },
      { icon: "📣", title: "@subagent handoff", description: "Mention a subagent directly — e.g. @explore How does auth work? — for a single-turn handoff." },
      { icon: "🧑‍🤝‍🧑", title: "Delegate & orchestrate", description: "agent_delegate hands off one focused task; agent_orchestrate coordinates subtasks." },
      { icon: "❓", title: "ask_question tool", description: "The agent can ask you a question with options, multi-select, or free-form text when it needs a decision." },
      { icon: "✍️", title: "Custom agent prompts", description: "Override any profile's prompt with markdown in .nanoagent/agents/*.md." },
    ],
  },
  {
    id: "lsp",
    icon: "🧩",
    title: "LSP & code intelligence",
    description: "Real symbols, types, and diagnostics from language servers — not guesses.",
    cards: [
      { icon: "🧠", title: "Language Server Protocol", description: "The code_intelligence tool gives the agent definitions, completions, diagnostics, and read-only rename previews." },
      { icon: "🌍", title: "Multi-language", description: "Built-in support for TypeScript/JavaScript, Python, C#, Rust, Go, and C/C++." },
      { icon: "🔧", title: "Server detection & status", description: "Discovers servers from built-ins and workspace overrides, with health and install hints on demand." },
      { icon: "🧪", title: "/lsp commands", description: "/lsp, /lsp refresh, and /lsp file <path> manage code intelligence from the session." },
    ],
  },
  {
    id: "index",
    icon: "🗂️",
    title: "Codebase indexing",
    description: "Local embeddings for fast, relevant repository search.",
    cards: [
      { icon: "🧬", title: "Local embeddings", description: "Lightweight per-file vectors are cached in .nanoagent/cache/codebase-index.json — entirely on-device." },
      { icon: "♻️", title: "Incremental updates", description: "Unchanged files are reused and only changed files re-indexed; optional auto-update runs after each turn." },
      { icon: "🚫", title: "Ignore-aware", description: "Respects .gitignore, .nanoignore, and built-in exclusions." },
      { icon: "🗄️", title: "/index commands", description: "/index, update, status, rebuild, and list control the index." },
    ],
  },
  {
    id: "sandbox",
    icon: "🛡️",
    title: "Permissions & sandboxing",
    description: "You decide what runs automatically, what needs approval, and what's denied.",
    cards: [
      { icon: "🚦", title: "Allow / Ask / Deny", description: "Per-tool permission modes gate file edits, shell commands, network, MCP tools, and memory writes." },
      { icon: "📦", title: "Sandbox modes", description: "ReadOnly, WorkspaceWrite, and DangerFullAccess scope what the agent may touch." },
      { icon: "🧱", title: "OS-native sandboxing", description: "bubblewrap on Linux, sandbox-exec on macOS, and a Windows sandbox runner for foreground commands." },
      { icon: "⚖️", title: "Session overrides", description: "/allow and /deny adjust rules on the fly; explicit deny patterns always win." },
      { icon: "↶", title: "Undo / redo", description: "Tracked file edits can be undone and redone with /undo and /redo." },
      { icon: "🔒", title: "Secret redaction", description: "Pattern-based redaction protects credentials across output, memory, audit, and logs." },
    ],
  },
  {
    id: "editors",
    icon: "🪟",
    title: "Editors & desktop",
    description: "The same agent, wherever you work.",
    cards: [
      { icon: "🖥️", title: "Desktop app", description: "A visual workspace with chat, model controls, budget config, file-edit tracking, and per-workspace sections." },
      { icon: "🆚", title: "VS Code extension", description: "rizwan3d.nanoagent docks a chat view and adds send-selection, review-file, review-diff, plan, and apply-changes commands." },
      { icon: "🟪", title: "Visual Studio extension", description: "NanoAgent.VS keeps a NanoAgent tool window inside Visual Studio 2022+." },
      { icon: "🧠", title: "JetBrains via ACP", description: "Any ACP-capable editor drives the local nanoai CLI over the protocol." },
      { icon: "🔄", title: "Diff review & apply", description: "Review Git diffs and apply suggested changes directly from the editor." },
    ],
  },
  {
    id: "ci",
    icon: "✅",
    title: "CI code reviews",
    description: "Automated, findings-first reviews on every pull and merge request.",
    cards: [
      { icon: "🐙", title: "GitHub Actions", description: "A ready-made workflow runs NanoAgent against PR diffs and posts a review comment." },
      { icon: "🦊", title: "GitLab CI", description: "A pipeline + script reviews merge requests and comments back using your GitLab token." },
      { icon: "🪣", title: "Bitbucket Pipelines", description: "The same review automation wired for Bitbucket with an access-token integration." },
      { icon: "📝", title: "Review profile", description: "A read-only pr-reviewer profile computes the diff, skips drafts, and retains artifacts for 14 days." },
    ],
  },
  {
    id: "dx",
    icon: "🧪",
    title: "Developer experience",
    description: "The tooling, transparency, and trust that make the agent practical day to day.",
    cards: [
      { icon: "💰", title: "Budget controls", description: "Optional local or cloud budgets with monthly USD caps, alert thresholds, and per-1M token pricing." },
      { icon: "🪝", title: "Lifecycle hooks", description: "Run hooks around task, tool, file, shell, web, memory, permission, and delegation events with timeouts." },
      { icon: "📑", title: "Tool audit log", description: "Optionally record every tool call to .nanoagent/logs/tool-audit.jsonl for review." },
      { icon: "🎨", title: "Tool-output modes", description: "full, compact, or preview rendering — set per-profile or with /tooloutput." },
      { icon: "🧰", title: "/init scaffolding", description: "Scaffold workspace-local .nanoagent files with recommended, minimal, or custom presets." },
      { icon: "📦", title: "SDK on NuGet", description: "Embed NanoAgent in your own .NET app via the NanoAgent package on NuGet.org." },
      { icon: "💿", title: "Cross-platform builds", description: "Windows x64 (installer + portable), macOS Apple Silicon & Intel, and Linux x64/arm64." },
      { icon: "🔏", title: "Verified releases", description: "Every release ships SHA256SUMS and GitHub artifact attestations for SLSA build provenance." },
      { icon: "⬆️", title: "In-place updates", description: "/update checks for new versions and /update now installs immediately." },
      { icon: "🏠", title: "Local-first privacy", description: "Workspace files, config, sections, index cache, and credentials stay on your machine — only prompts and selected context go to the provider." },
      { icon: "📊", title: "Anonymous telemetry, opt-out", description: "Aggregate, anonymous usage analytics only — turn it off entirely with /disableanalytics." },
      { icon: "📐", title: "Task-based benchmarks", description: "The benchmarks/ suite tracks bug-fixing, repo understanding, patch quality, security review, and tool safety over time." },
    ],
  },
];

export interface SidebarSection {
  title: string;
  items: { label: string; href: string }[];
}

export const docsSidebar: SidebarSection[] = [
  {
    title: "Getting started",
    items: [
      { label: "Install", href: "#install" },
      { label: "First run & provider setup", href: "#first-run" },
    ],
  },
  {
    title: "Daily use",
    items: [
      { label: "Desktop workflow", href: "#desktop" },
      { label: "Terminal workflow", href: "#terminal" },
      { label: "Terminal input & keys", href: "#terminal-keys" },
      { label: "Terminal commands", href: "#commands" },
    ],
  },
  {
    title: "Editors & CI",
    items: [
      { label: "VS Code extension", href: "#vscode" },
      { label: "Visual Studio extension", href: "#visual-studio" },
      { label: "ACP editor integration", href: "#acp" },
      { label: "Review automation (CI)", href: "#review" },
    ],
  },
  {
    title: "Capabilities",
    items: [
      { label: "Codebase indexing", href: "#indexing" },
      { label: "Providers & models", href: "#providers" },
      { label: "Profiles & subagents", href: "#profiles" },
      { label: "Code intelligence (LSP)", href: "#lsp" },
    ],
  },
  {
    title: "Configuration",
    items: [
      { label: "Permissions & sandboxing", href: "#permissions" },
      { label: "Workspace files", href: "#workspace" },
      { label: "Team memory", href: "#memory" },
      { label: "Skills & custom agents", href: "#skills" },
      { label: "MCP servers", href: "#mcp" },
      { label: "Custom tools", href: "#custom-tools" },
      { label: "Memory, audit & hooks", href: "#hooks" },
    ],
  },
  {
    title: "Reference",
    items: [
      { label: "Privacy & local data", href: "#privacy" },
      { label: "Troubleshooting", href: "#troubleshooting" },
      { label: "NuGet SDK (.NET)", href: "#sdk" },
      { label: "Build from source", href: "#build" },
      { label: "License", href: "#license" },
    ],
  },
];

export const docSections = [
 { id: "install", heading: "Install" },
 { id: "first-run", heading: "First run & provider setup" },
 { id: "desktop", heading: "Desktop workflow" },
 { id: "terminal", heading: "Terminal workflow" },
 { id: "terminal-keys", heading: "Terminal input & keys" },
 { id: "commands", heading: "Terminal commands" },
 { id: "vscode", heading: "VS Code extension" },
 { id: "visual-studio", heading: "Visual Studio extension" },
 { id: "acp", heading: "ACP editor integration" },
 { id: "review", heading: "Review automation (CI)" },
 { id: "indexing", heading: "Codebase indexing" },
 { id: "providers", heading: "Providers & models" },
 { id: "profiles", heading: "Profiles & subagents" },
 { id: "lsp", heading: "Code intelligence (LSP)" },
 { id: "permissions", heading: "Permissions & sandboxing" },
 { id: "workspace", heading: "Workspace files" },
 { id: "memory", heading: "Team memory" },
 { id: "skills", heading: "Skills & custom agents" },
 { id: "mcp", heading: "MCP servers" },
 { id: "custom-tools", heading: "Custom tools" },
 { id: "hooks", heading: "Memory, audit & hooks" },
 { id: "privacy", heading: "Privacy & local data" },
 { id: "troubleshooting", heading: "Troubleshooting" },
 { id: "sdk", heading: "NuGet SDK (.NET)" },
 { id: "build", heading: "Build from source" },
 { id: "license", heading: "License" },
] as const;
 
 // ── Gateway page data ──
 
 export interface GatewayAudience {
   label: string;
   title: string;
   description: string;
 }
 
 export const gatewayAudiences: GatewayAudience[] = [
   {
     label: "Security",
     title: "See AI traffic clearly across your organization",
     description: "Track how teams use models, watch for anomalies, and keep operations visible as adoption grows.",
   },
   {
     label: "Finance",
     title: "Turn AI spend into something you can actually measure",
     description: "Attribute usage by team, app, project, and user so budgets, quotas, and cost reviews stop being guesswork.",
   },
   {
     label: "Platform",
     title: "Give developers one stable interface as your stack evolves",
     description: "Keep clients pointed at one endpoint while your team manages changes behind the scenes.",
   },
 ];
 
 export interface GatewayStat {
   eyebrow: string;
   value: string;
   description: string;
 }
 
 export const gatewayStats: GatewayStat[] = [
   {
     eyebrow: "01 · ACCESS",
     value: "1 endpoint",
     description: "for teams that need one consistent AI entry point",
   },
   {
     eyebrow: "02 · MIGRATE",
     value: "0 client rewrites",
     description: "for tools already built on the OpenAI API shape",
   },
   {
     eyebrow: "03 · OBSERVE",
     value: "Full visibility",
     description: "across spend, access patterns, and usage",
   },
 ];
 
 export interface GatewayFeature {
   icon: string;
   title: string;
   description: string;
 }
 
 export const gatewayFeatures: GatewayFeature[] = [
   {
     icon: "🔌",
     title: "Zero migration",
     description: "Keep your existing OpenAI-compatible SDKs and prompts. Swap one base URL and you're routed through the Gateway.",
   },
   {
     icon: "🛡️",
     title: "Operations built in",
     description: "Visibility features wrap every call without forcing teams to rebuild their tooling.",
   },
   {
     icon: "💰",
     title: "Cost in your control",
     description: "Attribute spend per team and monitor usage trends so AI costs stay understandable as demand grows.",
   },
 ];
 
 export const gatewayCodeTabs = [
   {
     id: "aisdk",
     label: "AI SDK",
     code: `import { streamText } from 'ai'
 import { createOpenAI } from '@ai-sdk/openai'
 
 const nano = createOpenAI({
   baseURL: 'https://app.getnanoai.com/v1',
   apiKey: process.env.NANOAGENT_API_KEY,
 })
 
 const result = streamText({
   model: nano.chat('anthropic/claude-opus-4-8'),
   prompt: 'Why is the sky blue?',
 })`,
   },
   {
     id: "python",
     label: "Python",
     code: `import os
 from openai import OpenAI
 
 client = OpenAI(
     base_url="https://app.getnanoai.com/v1",
     api_key=os.environ["NANOAGENT_API_KEY"],
 )
 
 stream = client.chat.completions.create(
     model="anthropic/claude-opus-4-8",
     messages=[{"role": "user", "content": "Why is the sky blue?"}],
     stream=True,
 )`,
   },
   {
     id: "curl",
     label: "curl",
     code: `# point any OpenAI-compatible client at the Gateway
 curl https://app.getnanoai.com/v1/chat/completions \\
   -H "Authorization: Bearer $NANOAGENT_API_KEY" \\
   -H "Content-Type: application/json" \\
   -d '{
     "model": "anthropic/claude-opus-4-8",
     "messages": [{"role": "user", "content": "Why is the sky blue?"}],
     "stream": true
   }'`,
   },
 ];
 
 export interface GatewayPricingPlan {
   name: string;
   monthlyPrice: string;
   annualPrice: string;
   description: string;
   features: { name: string; included: boolean }[];
   ctaLabel: string;
   ctaHref: string;
   featured?: boolean;
 }
 
 export const gatewayPricingPlans: GatewayPricingPlan[] = [
   {
     name: "Free",
     monthlyPrice: "$0",
     annualPrice: "$0",
     description: "Start evaluating the Gateway with basic visibility.",
     features: [
       { name: "Single workspace", included: true },
       { name: "Up to 10,000 requests/mo", included: true },
       { name: "Basic request logs", included: true },
       { name: "Community support", included: true },
       { name: "Team management", included: false },
       { name: "Usage analytics", included: false },
       { name: "Role-based access", included: false },
       { name: "Priority support", included: false },
     ],
     ctaLabel: "Get Started",
     ctaHref: "https://app.getnanoai.com",
   },
   {
     name: "Pro",
     monthlyPrice: "$49",
     annualPrice: "$39",
     description: "For teams that need operational visibility.",
     featured: true,
     features: [
       { name: "Multiple workspaces", included: true },
       { name: "Up to 100,000 requests/mo", included: true },
       { name: "Detailed request logs", included: true },
       { name: "Usage analytics", included: true },
       { name: "Team management", included: true },
       { name: "Role-based access", included: false },
       { name: "Priority support", included: false },
       { name: "Custom rate limits", included: false },
     ],
     ctaLabel: "Start Free Trial",
     ctaHref: "https://app.getnanoai.com",
   },
   {
     name: "Enterprise",
     monthlyPrice: "Custom",
     annualPrice: "Custom",
     description: "For organizations with compliance and control requirements.",
     features: [
       { name: "Unlimited workspaces", included: true },
       { name: "Custom request limits", included: true },
       { name: "Audit-grade logging", included: true },
       { name: "Advanced usage analytics", included: true },
       { name: "Team management", included: true },
       { name: "Role-based access (RBAC)", included: true },
       { name: "Priority support & SLAs", included: true },
       { name: "Custom rate limits", included: true },
     ],
     ctaLabel: "Talk to Sales",
     ctaHref: "mailto:abdullah@alfain.tech?subject=NanoAgent%20Gateway%20-%20Enterprise%20enquiry",
   },
 ];

export interface DocSectionContent {
  id: string;
  content: string;
}
