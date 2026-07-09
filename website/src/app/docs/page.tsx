"use client";

import Container from "@/components/ui/Container";
import CTA from "@/components/features/CTA";
import DocsSidebar from "@/components/features/DocsSidebar";

export default function DocsPage() {
  return (
    <>
      {/* Hero */}
      <section className="text-center max-w-[760px] mx-auto px-6 pt-14 pb-2">
        <span className="inline-block text-[13px] font-bold tracking-[0.08em] uppercase text-[var(--color-acc-1)]">
          Documentation
        </span>
        <h1 className="text-[clamp(30px,4.6vw,50px)] leading-[1.05] tracking-[-0.025em] font-extrabold m-0 mt-3">
          The NanoAgent <span className="text-[var(--color-acc-1)]">handbook</span>
        </h1>
        <p className="mt-[18px] mx-auto max-w-[640px] text-[17px] text-[var(--color-text-mut)]">
          NanoAgent is an AI coding agent that works directly inside a repository while respecting local permissions,
          approval prompts, and workspace policy. It runs as a desktop app, the{" "}
          <code className="code-inline">nanoai</code> terminal command, a VS Code extension, a Visual Studio
          extension, and an ACP-compatible editor server. This is the full handbook for installation, daily use,
          safety controls, integration, automation, and advanced customization.
        </p>
      </section>

      <Container>
        <div className="grid grid-cols-[248px_minmax(0,1fr)] max-md:grid-cols-1 gap-14 items-start pt-12">
          <DocsSidebar />

          <article className="min-w-0 max-w-[840px]">

            {/* Install */}
            <DocSection id="install" heading="Install">
              <h3>Desktop app</h3>
              <p>Download the latest release for your platform:</p>
              <DocTable
                headers={["Platform", "Download"]}
                rows={[
                  ["Windows x64", <a key="w" href="https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-win-x64-setup.exe" target="_blank" rel="noopener noreferrer" className="text-[var(--color-acc-1)] hover:underline">Installer</a>],
                  ["Linux x64", <a key="lx" href="https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-linux-x64.zip" target="_blank" rel="noopener noreferrer" className="text-[var(--color-acc-1)] hover:underline">Zip</a>],
                  ["Linux arm64", <a key="la" href="https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-linux-arm64.zip" target="_blank" rel="noopener noreferrer" className="text-[var(--color-acc-1)] hover:underline">Zip</a>],
                  ["macOS x64", <a key="mx" href="https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-osx-x64.zip" target="_blank" rel="noopener noreferrer" className="text-[var(--color-acc-1)] hover:underline">Zip</a>],
                  ["macOS arm64", <a key="ma" href="https://github.com/rizwan3d/NanoAgent/releases/latest/download/NanoAgent.Desktop-osx-arm64.zip" target="_blank" rel="noopener noreferrer" className="text-[var(--color-acc-1)] hover:underline">Zip</a>],
                ]}
              />
              <p>Release downloads are published at:</p>
              <pre className="relative m-4 p-[30px_18px_18px] overflow-x-auto bg-[#050505] border border-[var(--color-border)] rounded-xl font-[var(--font-mono)] text-[13px] leading-[1.7] text-[var(--color-text)]">
                <code>https://github.com/rizwan3d/NanoAgent/releases/latest</code>
              </pre>

              <h3>CLI</h3>
              <h4>Curl</h4>
              <pre className="relative m-4 p-[30px_18px_18px] overflow-x-auto bg-[#050505] border border-[var(--color-border)] rounded-xl font-[var(--font-mono)] text-[13px] leading-[1.7] text-[var(--color-text)]">
                <code>curl -fsSL https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.sh | bash</code>
              </pre>
              <h4>Windows PowerShell</h4>
              <pre className="relative m-4 p-[30px_18px_18px] overflow-x-auto bg-[#050505] border border-[var(--color-border)] rounded-xl font-[var(--font-mono)] text-[13px] leading-[1.7] text-[var(--color-text)]">
                <code>irm https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.ps1 | iex</code>
              </pre>
              <h4>NPM</h4>
              <pre className="relative m-4 p-[30px_18px_18px] overflow-x-auto bg-[#050505] border border-[var(--color-border)] rounded-xl font-[var(--font-mono)] text-[13px] leading-[1.7] text-[var(--color-text)]">
                <code>npm install -g nanoai-cli</code>
              </pre>
              <h4>pnpm</h4>
              <pre className="relative m-4 p-[30px_18px_18px] overflow-x-auto bg-[#050505] border border-[var(--color-border)] rounded-xl font-[var(--font-mono)] text-[13px] leading-[1.7] text-[var(--color-text)]">
                <code>pnpm add -g nanoai-cli</code>
              </pre>
              <h4>bun</h4>
              <pre className="relative m-4 p-[30px_18px_18px] overflow-x-auto bg-[#050505] border border-[var(--color-border)] rounded-xl font-[var(--font-mono)] text-[13px] leading-[1.7] text-[var(--color-text)]">
                <code>bun add -g nanoai-cli</code>
              </pre>
              <DocNote>The CLI install scripts verify the archive checksum against SHA256SUMS, or the SHA256 digest from GitHub release metadata, before extraction. <strong>Checksum verification is mandatory</strong> — installation fails if the checksum cannot be validated.</DocNote>
            </DocSection>

            {/* First Run */}
            <DocSection id="first-run" heading="First run &amp; provider setup">
              <p>Start NanoAgent:</p>
              <pre className="relative m-4 p-[30px_18px_18px] overflow-x-auto bg-[#050505] border border-[var(--color-border)] rounded-xl font-[var(--font-mono)] text-[13px] leading-[1.7] text-[var(--color-text)]">
                <code>nanoai</code>
              </pre>
              <p>NanoAgent will guide you through provider setup:</p>
              <ol className="text-[var(--color-text-mut)] text-[15px] pl-[22px] m-3">
                <li className="m-[6px_0]">Choose a setup type: subscription account, API key provider, OpenAI-compatible provider, or local provider.</li>
                <li className="m-[6px_0]">Choose a provider from the matching submenu when needed.</li>
                <li className="m-[6px_0]">Enter an API key, sign in with ChatGPT Plus/Pro, Claude Pro/Max, or GitHub Copilot, enter a custom compatible base URL, or use a local provider default.</li>
                <li className="m-[6px_0]">Let NanoAgent discover available models.</li>
                <li className="m-[6px_0]">Open a desktop workspace or use the current terminal directory.</li>
                <li className="m-[6px_0]">Start a new section or resume an existing one.</li>
              </ol>
            </DocSection>

            {/* Desktop */}
            <DocSection id="desktop" heading="Desktop workflow">
              <p>The desktop app is built around workspaces, sections, chat, and controls.</p>
              <h3>Workspaces</h3>
              <p>Open a local folder to make it the active workspace. NanoAgent remembers recent workspaces so you can return later.</p>
              <h3>Sections</h3>
              <p>A section is a saved local conversation thread tied to a workspace.</p>
              <h3>Conversation</h3>
              <p>Type a prompt and let NanoAgent inspect, plan, edit, run commands, or ask for approval depending on the active profile and permissions.</p>
              <h3>Budget controls</h3>
              <p>Budget controls are disabled by default. They become active only after you enable them with <code className="code-inline">/budget local</code> or <code className="code-inline">/budget cloud</code>.</p>
            </DocSection>

            {/* Terminal */}
            <DocSection id="terminal" heading="Terminal workflow">
              <h3>Interactive mode</h3>
              <pre className="relative m-4 p-[30px_18px_18px] overflow-x-auto bg-[#050505] border border-[var(--color-border)] rounded-xl font-[var(--font-mono)] text-[13px] leading-[1.7] text-[var(--color-text)]">
                <code>nanoai</code>
              </pre>
              <h3>One-shot prompt</h3>
              <pre className="relative m-4 p-[30px_18px_18px] overflow-x-auto bg-[#050505] border border-[var(--color-border)] rounded-xl font-[var(--font-mono)] text-[13px] leading-[1.7] text-[var(--color-text)]">
                <code>nanoai "Find risky changes in this branch"</code>
              </pre>
              <h3>Prompt from standard input</h3>
              <pre className="relative m-4 p-[30px_18px_18px] overflow-x-auto bg-[#050505] border border-[var(--color-border)] rounded-xl font-[var(--font-mono)] text-[13px] leading-[1.7] text-[var(--color-text)]">
                <code>git diff --stat | nanoai --stdin --profile review</code>
              </pre>
            </DocSection>

            {/* Terminal Input & Keys */}
            <DocSection id="terminal-keys" heading="Terminal input &amp; keys">
              <p>The interactive terminal UI adds keyboard controls for editing, navigating history, queueing work, and interrupting a running turn.</p>
              <h3>Queue prompts and commands</h3>
              <p>You can queue prompts while NanoAgent is busy. Queued items run automatically, in order, as soon as the active turn completes.</p>
              <h3>Interrupt a running or stuck turn</h3>
              <p>Press <strong>Esc</strong> once to request a graceful interrupt. Press <strong>Esc</strong> again to abandon it locally.</p>
              <h3>Tab-complete file and directory paths</h3>
              <p>After a <code className="code-inline">!</code> or <code className="code-inline">!!</code> shell command and a space, <strong>Tab</strong> completes file and directory paths.</p>
            </DocSection>

            {/* Terminal Commands */}
            <DocSection id="commands" heading="Terminal commands">
              <DocTable
                headers={["Command", "Description"]}
                rows={([
                  ["/help", "List commands and usage."],
                  ["/budget [status|local|cloud]", "Show or configure budget controls."],
                  ["/config", "Show provider, model, profile, thinking mode, reasoning effort."],
                  ["/models", "Choose the active model with the arrow-key picker."],
                  ["/use <model>", "Switch directly to a model id."],
                  ["/onboard", "Re-run provider onboarding."],
                  ["/profile <name>", "Switch the active profile."],
                  ["/thinking [on|off]", "Show or set simple thinking mode."],
                  ["/reasoning [show|<level>]", "Show or set provider reasoning effort."],
                  ["/permissions", "Show permission summary."],
                  ["/allow <tool> [pattern]", "Add a session allow override."],
                  ["/deny <tool> [pattern]", "Add a session deny override."],
                  ["/mcp", "Show MCP servers, custom tools, and dynamic tools."],
                  ["/terminals", "List or stop background terminals."],
                  ["/init [recommended|minimal|custom]", "Initialize workspace-local NanoAgent files."],
                  ["/update [now]", "Check for updates."],
                  ["/undo", "Roll back the most recent tracked edit."],
                  ["/redo", "Re-apply the most recently undone edit."],
                  ["/exit", "Exit the interactive shell."],
                ] as const).map(([cmd, desc]) => [<code key={cmd} className="code-inline">{cmd}</code>, desc] as React.ReactNode[])}
              />
            </DocSection>

            {/* VS Code */}
            <DocSection id="vscode" heading="VS Code extension">
              <p>NanoAgent includes a VS Code extension. Install from the Visual Studio Marketplace:</p>
              <pre className="relative m-4 p-[30px_18px_18px] overflow-x-auto bg-[#050505] border border-[var(--color-border)] rounded-xl font-[var(--font-mono)] text-[13px] leading-[1.7] text-[var(--color-text)]">
                <code>ext install rizwan3d.nanoagent</code>
              </pre>
              <DocTable
                headers={["Setting", "Default", "Purpose"]}
                rows={([
                  ["nanoagent.command", <code key="c1" className="code-inline">nanoai</code>, "Command used to start NanoAgent."],
                  ["nanoagent.args", <code key="c2" className="code-inline">["--acp"]</code>, "Arguments passed to the NanoAgent CLI."],
                  ["nanoagent.autoStart", <code key="c3" className="code-inline">false</code>, "Start NanoAgent automatically when VS Code starts."],
                ] as const).map(([s, d, p]) => [<code key={s} className="code-inline">{s}</code>, d, p] as React.ReactNode[])}
              />
            </DocSection>

            {/* Visual Studio */}
            <DocSection id="visual-studio" heading="Visual Studio extension">
              <p>NanoAgent includes a Visual Studio extension. It opens a tool window inside Visual Studio and starts the local NanoAgent CLI over ACP.</p>
            </DocSection>

            {/* ACP */}
            <DocSection id="acp" heading="ACP editor integration">
              <p>NanoAgent can run as an Agent Client Protocol server:</p>
              <pre className="relative m-4 p-[30px_18px_18px] overflow-x-auto bg-[#050505] border border-[var(--color-border)] rounded-xl font-[var(--font-mono)] text-[13px] leading-[1.7] text-[var(--color-text)]">
                <code>nanoai --acp</code>
              </pre>
              <p>ACP mode speaks line-delimited JSON-RPC on stdin/stdout.</p>
            </DocSection>

            {/* CI Reviews */}
            <DocSection id="review" heading="Review automation (CI)">
              <p>NanoAgent includes copy-paste CI examples for GitHub, GitLab, and Bitbucket. Each example installs NanoAI, computes the diff, runs the workspace pr-reviewer profile in read-only mode, and posts a review comment.</p>
            </DocSection>

            {/* Indexing */}
            <DocSection id="indexing" heading="Codebase indexing">
              <p>NanoAgent includes a local codebase index for repository-wide discovery. The <code className="code-inline">codebase_index</code> tool can search, build, list, and show status.</p>
            </DocSection>

            {/* Providers */}
            <DocSection id="providers" heading="Providers &amp; models">
              <p>NanoAgent stores a provider profile locally and discovers models from that provider. Use F2 or <code className="code-inline">/models</code> to switch.</p>
              <h3>Thinking mode</h3>
              <pre className="relative m-4 p-[30px_18px_18px] overflow-x-auto bg-[#050505] border border-[var(--color-border)] rounded-xl font-[var(--font-mono)] text-[13px] leading-[1.7] text-[var(--color-text)]">
                <code>/thinking on\n/thinking off</code>
              </pre>
            </DocSection>

            {/* Profiles */}
            <DocSection id="profiles" heading="Profiles &amp; subagents">
              <DocTable
                headers={["Profile", "Mode", "Edit behavior", "Best for"]}
                rows={[
                  [<code key="b" className="code-inline">build</code>, "Primary", "Allows edits under permissions", "Implementation, fixes, tests, build loops."],
                  [<code key="p" className="code-inline">plan</code>, "Primary", "Read-only", "Investigation and implementation plans."],
                  [<code key="r" className="code-inline">review</code>, "Primary", "Read-only", "Findings-first code review."],
                  [<code key="g" className="code-inline">general</code>, "Subagent", "Allows edits under permissions", "Bounded delegated implementation work."],
                  [<code key="e" className="code-inline">explore</code>, "Subagent", "Read-only", "Fast codebase discovery."],
                ]}
              />
            </DocSection>

            {/* LSP */}
            <DocSection id="lsp" heading="Code intelligence (LSP)">
              <p><code className="code-inline">code_intelligence</code> discovers language servers from built-in definitions, the current workspace, and optional profile overrides.</p>
            </DocSection>

            {/* Permissions */}
            <DocSection id="permissions" heading="Permissions &amp; sandboxing">
              <p>NanoAgent evaluates every sensitive action through permission policy.</p>
              <DocTable
                headers={["Mode", "Meaning"]}
                rows={[
                  ["Allow", "The action can proceed."],
                  ["Ask", "NanoAgent prompts for approval."],
                  ["Deny", "The action is blocked."],
                ]}
              />
              <h3>Sandbox modes</h3>
              <DocTable
                headers={["Mode", "Meaning"]}
                rows={[
                  ["ReadOnly", "No file writes or unsafe shell mutation."],
                  ["WorkspaceWrite", "Workspace-scoped writes are allowed under policy."],
                  ["DangerFullAccess", "Unrestricted execution when explicitly configured or approved."],
                ]}
              />
            </DocSection>

            {/* Workspace Files */}
            <DocSection id="workspace" heading="Workspace files">
              <p>Run <code className="code-inline">/init</code>. NanoAgent asks which starter files to add.</p>
              <p>Place <code className="code-inline">AGENTS.md</code> in the workspace for persistent project instructions.</p>
            </DocSection>

            {/* Memory */}
            <DocSection id="memory" heading="Team memory">
              <p>NanoAgent stores structured team memory as ordinary markdown files in <code className="code-inline">.nanoagent/memory/</code>.</p>
            </DocSection>

            {/* Skills */}
            <DocSection id="skills" heading="Skills &amp; custom agents">
              <p>Skills are task-specific playbooks loaded only when relevant.</p>
            </DocSection>

            {/* MCP */}
            <DocSection id="mcp" heading="MCP servers">
              <p>NanoAgent can load MCP servers from user-level and workspace-level <code className="code-inline">agent-profile.json</code> files.</p>
            </DocSection>

            {/* Custom tools */}
            <DocSection id="custom-tools" heading="Custom tools">
              <p>NanoAgent can expose user-defined process tools from <code className="code-inline">agent-profile.json</code>.</p>
            </DocSection>

            {/* Hooks */}
            <DocSection id="hooks" heading="Memory, audit &amp; hooks">
              <p>Configure memory, audit, and lifecycle hook behavior in workspace profile.</p>
            </DocSection>

            {/* Privacy */}
            <DocSection id="privacy" heading="Privacy &amp; local data">
              <p>Workspace files, configuration, sections, codebase index cache, team memory, and audit logs stay on your machine.</p>
            </DocSection>

            {/* Troubleshooting */}
            <DocSection id="troubleshooting" heading="Troubleshooting">
              <h3><code className="code-inline">nanoai</code> is not found</h3>
              <p>Restart the terminal after installation.</p>
              <h3>Provider setup is incomplete</h3>
              <p>Run <code className="code-inline">nanoai</code> and choose to reconfigure.</p>
              <h3>A command is denied</h3>
              <p>Run <code className="code-inline">/permissions</code> and <code className="code-inline">/rules</code> to see active policy.</p>
            </DocSection>

            {/* SDK */}
            <DocSection id="sdk" heading="NuGet SDK (.NET)">
              <p>The <code className="code-inline">NanoAgent</code> package on NuGet.org provides the core libraries. Add it to your project:</p>
              <pre className="relative m-4 p-[30px_18px_18px] overflow-x-auto bg-[#050505] border border-[var(--color-border)] rounded-xl font-[var(--font-mono)] text-[13px] leading-[1.7] text-[var(--color-text)]">
                <code>dotnet add package NanoAgent</code>
              </pre>
            </DocSection>

            {/* Build from source */}
            <DocSection id="build" heading="Build from source">
              <h3>Requirements</h3>
              <ul className="text-[var(--color-text-mut)] text-[15px] pl-[22px] m-3">
                <li className="m-[6px_0]">.NET SDK compatible with <code className="code-inline">net10.0</code>.</li>
                <li className="m-[6px_0]">Node.js 20 or newer for the VS Code extension.</li>
              </ul>
              <pre className="relative m-4 p-[30px_18px_18px] overflow-x-auto bg-[#050505] border border-[var(--color-border)] rounded-xl font-[var(--font-mono)] text-[13px] leading-[1.7] text-[var(--color-text)]">
                <code>{`dotnet restore NanoAgent.CrossPlatform.slnx
dotnet build NanoAgent.CrossPlatform.slnx
dotnet test NanoAgent.Tests/NanoAgent.Tests.csproj
dotnet pack NanoAgent/NanoAgent.csproj -c Release`}</code>
              </pre>
            </DocSection>

            {/* License */}
            <DocSection id="license" heading="License">
              <p>NanoAgent is licensed under the Apache License 2.0.</p>
              <DocNote>
                Looking for the source, releases, or to file an issue? Visit the{" "}
                <a href="https://github.com/rizwan3d/NanoAgent" target="_blank" rel="noopener noreferrer" className="text-[var(--color-acc-1)] hover:underline">
                  NanoAgent GitHub repository
                </a>.
              </DocNote>
            </DocSection>

          </article>
        </div>

        {/* CTA */}
        <section className="pt-24 pb-0">
          <CTA
            primaryLabel="Get NanoAgent"
            primaryHref="/#get"
            secondaryLabel="See all features"
            secondaryHref="/features"
          />
        </section>

        <div className="pb-16" />
      </Container>
    </>
  );
}

// ── Helper components ──

function DocSection({ id, heading, children }: { id: string; heading: string; children: React.ReactNode }) {
  return (
    <section id={id} className="doc-section scroll-mt-24 [&+&]:border-t [&+&]:border-[var(--color-border)] [&+&]:mt-12 [&+&]:pt-10">
      <h2 className="text-[clamp(24px,3vw,32px)] tracking-[-0.02em] font-extrabold m-0 mb-2">{heading}</h2>
      {children}
    </section>
  );
}

function DocTable({ headers, rows }: { headers: string[]; rows: React.ReactNode[][] }) {
  return (
    <div className="overflow-x-auto border border-[var(--color-border)] rounded-xl m-4 bg-[var(--color-surface)]">
      <table className="w-full border-collapse text-[14px] min-w-[520px]">
        <thead>
          <tr>
            {headers.map((h) => (
              <th key={h} className="bg-[var(--color-bg-2)] text-[var(--color-text)] font-bold text-[13px] px-[14px] py-[10px] text-left border-b border-[var(--color-border-2)]">
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i} className="hover:bg-[rgba(255,255,255,0.025)]">
              {row.map((cell, j) => (
                <td key={j} className="px-[14px] py-[10px] text-left border-b border-[var(--color-border)] text-[var(--color-text-mut)]">
                  {cell}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function DocNote({ children }: { children: React.ReactNode }) {
  return (
    <div className="m-[18px_0] px-[18px] py-[14px] rounded-xl bg-[rgba(124,140,255,0.07)] border border-[var(--color-border-2)] text-[var(--color-text-mut)] text-[14px]">
      {children}
    </div>
  );
}
