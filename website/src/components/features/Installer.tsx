"use client";

import { useState, useCallback } from "react";
import { installTabs, installPanels, InstallPanel } from "@/lib/data";

function TerminalIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" className="lucide lucide-terminal" aria-hidden="true">
      <path d="M12 19h8" /><path d="m4 17 6-6-6-6" />
    </svg>
  );
}

function VSCodeIcon() {
  return (
    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
      <path opacity="0.8" d="M21.5145 3.2381L17.1391 1.13147C16.6327 0.887624 16.0275 0.990482 15.63 1.38794L1.30685 14.4473C0.921589 14.7985 0.922032 15.4051 1.3078 15.7558L2.47776 16.8194C2.79314 17.1061 3.26812 17.1272 3.60768 16.8696L20.8561 3.78462C21.4347 3.34563 22.2659 3.75835 22.2659 4.48468V4.43389C22.2659 3.92404 21.9738 3.45928 21.5145 3.2381Z" fill="currentColor" />
      <path d="M21.5145 19.8875L17.1391 21.9941C16.6327 22.238 16.0275 22.1351 15.63 21.7376L1.30685 8.67829C0.921589 8.32702 0.922032 7.72047 1.3078 7.36978L2.47776 6.30617C2.79314 6.01946 3.26812 5.99835 3.60768 6.25595L20.8561 19.3409C21.4347 19.7799 22.2659 19.3672 22.2659 18.6409V18.6917C22.2659 19.2015 21.9738 19.6663 21.5145 19.8875Z" fill="currentColor" />
      <path opacity="0.6" d="M17.1393 21.9945C16.6327 22.2382 16.0275 22.1351 15.63 21.7376C16.1198 22.2274 16.9572 21.8805 16.9572 21.1879V1.93735C16.9572 1.24473 16.1198 0.897861 15.63 1.38762C16.0275 0.990127 16.6327 0.887124 17.1393 1.13075L21.5139 3.23449C21.9736 3.45555 22.2659 3.92048 22.2659 4.43054V18.6948C22.2659 19.2048 21.9736 19.6698 21.5139 19.8908L17.1393 21.9945Z" fill="currentColor" />
    </svg>
  );
}

function VSIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" x="0px" y="0px" width="24" height="24" viewBox="0 0 30 30" fill="currentColor">
      <path d="M27.324,4.804l-4.75-1.625c-0.315-0.108-0.667-0.051-0.932,0.152l-10.708,8.21L5.517,8.27 c-0.278-0.169-0.62-0.192-0.918-0.061l-2,0.875C2.235,9.243,2,9.603,2,10v10c0,0.397,0.235,0.757,0.599,0.916l2,0.875 c0.297,0.131,0.639,0.107,0.918-0.061l5.416-3.271l10.708,8.21c0.177,0.136,0.392,0.206,0.608,0.206 c0.109,0,0.218-0.018,0.324-0.054l4.75-1.625C27.728,25.058,28,24.678,28,24.25V5.75C28,5.322,27.728,4.942,27.324,4.804z M6,16.766 v-3.532L8.923,15L6,16.766z M22,19.717L15.038,15L22,10.283V19.717z" />
    </svg>
  );
}

function DesktopIcon() {
  return (
    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
      <path d="M22.5 2.25H1.5C1.10218 2.25 0.720644 2.40804 0.43934 2.68934C0.158035 2.97064 0 3.35218 0 3.75L0 18C0 18.3978 0.158035 18.7794 0.43934 19.0607C0.720644 19.342 1.10218 19.5 1.5 19.5H10.5V21H7.5V21.75H16.5V21H13.5V19.5H22.5C22.8978 19.5 23.2794 19.342 23.5607 19.0607C23.842 18.7794 24 18.3978 24 18V3.75C24 3.35218 23.842 2.97064 23.5607 2.68934C23.2794 2.40804 22.8978 2.25 22.5 2.25ZM12.75 21H11.25V19.5H12.75V21ZM23.25 18C23.25 18.1989 23.171 18.3897 23.0303 18.5303C22.8897 18.671 22.6989 18.75 22.5 18.75H1.5C1.30109 18.75 1.11032 18.671 0.96967 18.5303C0.829018 18.3897 0.75 18.1989 0.75 18V3.75C0.75 3.55109 0.829018 3.36032 0.96967 3.21967C1.11032 3.07902 1.30109 3 1.5 3H22.5C22.6989 3 22.8897 3.07902 23.0303 3.21967C23.171 3.36032 23.25 3.55109 23.25 3.75V18Z" fill="currentColor" />
      <path d="M1.5 15.75H22.5V3.75H1.5V15.75ZM2.25 4.5H21.75V15H2.25V4.5Z" fill="currentColor" />
      <path d="M12 18C12.4142 18 12.75 17.6642 12.75 17.25C12.75 16.8358 12.4142 16.5 12 16.5C11.5858 16.5 11.25 16.8358 11.25 17.25C11.25 17.6642 11.5858 18 12 18Z" fill="currentColor" />
    </svg>
  );
}

const tabIcons: Record<string, React.ReactNode> = {
  cli: <TerminalIcon />,
  vscode: <VSCodeIcon />,
  vs: <VSIcon />,
  desktop: <DesktopIcon />,
};

interface CommandScreenProps {
  cmd: string;
  sigil?: string;
}

function CommandScreen({ cmd, sigil = "$" }: CommandScreenProps) {
  const [copied, setCopied] = useState(false);

  const handleCopy = useCallback(async () => {
    try {
      await navigator.clipboard.writeText(cmd);
    } catch {
      const ta = document.createElement("textarea");
      ta.value = cmd;
      ta.style.position = "fixed";
      ta.style.opacity = "0";
      document.body.appendChild(ta);
      ta.select();
      document.execCommand("copy");
      document.body.removeChild(ta);
    }
    setCopied(true);
    setTimeout(() => setCopied(false), 1600);
  }, [cmd]);

  return (
    <div className="flex items-stretch gap-0 bg-[#050505] border border-[var(--color-border)] rounded-xl overflow-hidden">
      <code className="flex-1 min-w-0 font-[var(--font-mono)] text-[13px] text-[var(--color-text)] px-4 py-2 overflow-x-auto whitespace-nowrap">
        <span className="text-[var(--color-acc-1)] mr-2 select-none">{sigil}</span>
        {cmd}
      </code>
      <button
        className="shrink-0 border-0 border-l border-[var(--color-border)] bg-[rgba(255,255,255,0.04)] text-[var(--color-text-mut)] font-semibold text-[13px] px-[18px] cursor-pointer transition-all duration-150 hover:bg-[rgba(255,255,255,0.09)] hover:text-[var(--color-text)]"
        onClick={handleCopy}
        aria-label="Copy command"
      >
        {copied ? "Copied!" : "Copy"}
      </button>
    </div>
  );
}

export default function Installer() {
  const [activeTab, setActiveTab] = useState("cli");
  const [activeSubTab, setActiveSubTab] = useState("npm");
  const [activeCodeTab, setActiveCodeTab] = useState("sh");

  const panel = installPanels.find((p) => p.id === activeTab) as InstallPanel | undefined;

  const sigils: Record<string, string> = {
    curl: "$",
    pw: "PS>",
    npm: "$",
    pnpm: "$",
    bun: "$",
    vscode: "$",
    vs: "›",
    desktop: "↓",
  };

  return (
    <div className="mx-auto mt-[34px] border border-[var(--color-border-2)] rounded-[var(--radius)] bg-gradient-to-b from-[var(--color-surface)] to-[var(--color-bg-2)] shadow-[0_30px_80px_-40px_rgba(0,0,0,0.9)] overflow-hidden text-left">
      {/* Tabs */}
      <div className="flex flex-wrap gap-1 p-[7px] bg-[var(--color-bg-2)] border-b border-[var(--color-border)]" role="tablist" aria-label="Install NanoAgent">
        {installTabs.map((tab) => (
          <button
            key={tab.id}
            className={`inline-flex items-center gap-[7px] border-0 bg-none font-inherit text-[13.5px] font-semibold px-[14px] py-2 rounded-lg cursor-pointer transition-all duration-150 ${
              activeTab === tab.id
                ? "text-[var(--color-text)] bg-[rgba(124,140,255,0.16)]"
                : "text-[var(--color-text-mut)] hover:text-[var(--color-text)] hover:bg-[rgba(255,255,255,0.05)]"
            }`}
            role="tab"
            aria-selected={activeTab === tab.id}
            onClick={() => {
              setActiveTab(tab.id);
              setActiveSubTab(tab.id === "cli" ? "npm" : tab.id);
            }}
          >
            {tabIcons[tab.id]}
            {tab.label}
          </button>
        ))}
      </div>

      {/* Panels */}
      <div className="p-3">
        {installPanels.map((panel) => (
          <div key={panel.id} className={activeTab === panel.id ? "block" : "hidden"}>
            {/* Sub tabs for CLI */}
            {panel.subTabs && (
              <div className="flex flex-wrap gap-1 mb-3 border-b border-[var(--color-border)]" role="tablist" aria-label="Choose install method">
                {panel.subTabs.map((st) => (
                  <button
                    key={st.id}
                    className={`border-0 bg-none font-[var(--font-mono)] text-[12.5px] font-semibold px-3 py-[7px] rounded-t-lg cursor-pointer border-b-2 border-transparent transition-all duration-150 ${
                      activeSubTab === st.id
                        ? "text-[var(--color-acc-1)] border-b-[var(--color-acc-1)]"
                        : "text-[var(--color-text-dim)] hover:text-[var(--color-text)]"
                    }`}
                    role="tab"
                    aria-selected={activeSubTab === st.id}
                    onClick={() => setActiveSubTab(st.id)}
                  >
                    {st.label}
                  </button>
                ))}
              </div>
            )}

            {/* Commands */}
            {panel.commands && (
              <div>
                {panel.commands.map((cmd) => {
                  if (panel.subTabs && cmd.id !== activeSubTab) return null;
                  return (
                    <CommandScreen
                      key={cmd.id}
                      cmd={cmd.cmd}
                      sigil={sigils[cmd.id] || "$"}
                    />
                  );
                })}
              </div>
            )}

            {/* Actions */}
            {panel.actions && (
              <div className="flex flex-wrap gap-[10px] justify-center mt-3">
                {panel.actions.map((action) => (
                  <a
                    key={action.label}
                    href={action.href}
                    className={`inline-flex items-center justify-center gap-2 font-semibold leading-none rounded-full border cursor-pointer transition-all duration-200 whitespace-nowrap text-[14px] px-[18px] py-[9px] ${
                      action.variant === "primary"
                        ? "text-[#06121a] bg-[var(--color-acc-2)] shadow-[0_8px_30px_-10px_rgba(124,140,255,0.6)] hover:bg-[#93a0ff] hover:shadow-[0_14px_40px_-10px_rgba(124,140,255,0.7)]"
                        : "text-[var(--color-text)] bg-[rgba(255,255,255,0.04)] border-[var(--color-border-2)] hover:bg-[rgba(255,255,255,0.08)]"
                    }`}
                  >
                    {action.label}
                  </a>
                ))}
              </div>
            )}

            {panel.foot && (
              <p className="mt-4 mb-[2px] text-center text-[13px] text-[var(--color-text-dim)]">
                {panel.foot}
              </p>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
