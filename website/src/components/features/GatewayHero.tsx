"use client";

import { useState, useCallback } from "react";
import Button from "@/components/ui/Button";
import { gatewayCodeTabs } from "@/lib/data";

function CodeBlock({ code, isActive }: { code: string; isActive: boolean }) {
  return (
    <pre
     className={`relative m-0 p-5 overflow-x-auto font-mono text-[13px] leading-relaxed text-white bg-[#050505] ${
       isActive ? "block" : "hidden"
     }`}
    >
      <code className="block [counter-reset:ln]">
        {code.split("\n").map((line, i) => {
          const lineContent = line || " ";
          const parts = lineContent.split(/(\bimport\b)/);
          const hasImport = parts.length > 1;
          return (
            <span
              key={i}
              className="block relative pl-[3em] min-h-[1.75em] [counter-increment:ln] before:content-[counter(ln)] before:absolute before:left-0 before:w-[1.8em] before:text-right before:text-[#c4a7ff] before:select-none"
            >
              {hasImport
                ? parts.map((part, j) =>
                    part === "import" ? (
                      <span key={j} className="text-[#9ece6a]">
                        import
                      </span>
                    ) : (
                      part
                    )
                  )
                : lineContent}
            </span>
          );
        })}
      </code>
    </pre>
  );
}

export default function GatewayHero() {
  const [activeTab, setActiveTab] = useState(gatewayCodeTabs[0].id);
  const [copied, setCopied] = useState<string | null>(null);

  const activeCode =
    gatewayCodeTabs.find((t) => t.id === activeTab)?.code ?? "";

  const handleCopy = useCallback(async () => {
    try {
      await navigator.clipboard.writeText(activeCode);
      setCopied(activeTab);
      setTimeout(() => setCopied(null), 1600);
    } catch {
      const ta = document.createElement("textarea");
      ta.value = activeCode;
      ta.style.position = "fixed";
      ta.style.opacity = "0";
      document.body.appendChild(ta);
      ta.select();
      document.execCommand("copy");
      document.body.removeChild(ta);
      setCopied(activeTab);
      setTimeout(() => setCopied(null), 1600);
    }
  }, [activeCode, activeTab]);

  return (
    <section className="px-[158px] max-lg:px-6">
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-12 items-center pt-14 pb-5 max-lg:gap-8 max-lg:pt-8">
        {/* Copy side */}
        <div className="flex flex-col gap-[18px]">
          <span className="inline-block text-[13px] font-bold tracking-[0.08em] uppercase text-[var(--color-acc-1)]">
            Enterprise Gateway
          </span>
          <h1 className="text-[clamp(38px,5vw,58px)] leading-[1.1] tracking-[-0.02em] font-extrabold m-0">
            One AI gateway for security, finance, and platform teams{" "}
            <span className="block mt-[0.08em] bg-gradient-to-r from-[#2fd4ff] via-[#5c9cff] to-[#6c54ff] bg-clip-text text-transparent">
              to control every model call
            </span>
          </h1>
          <p className="m-0 max-w-[520px] text-[16.5px] text-[var(--color-text-mut)]">
            NanoAgent Gateway is the control plane for enterprise AI. Put a
            single OpenAI-compatible endpoint in front of your AI traffic, and
            give security, finance, and platform teams the visibility and
            operational control they need.
          </p>
          <div className="flex gap-[14px] flex-wrap">
            <Button
              variant="primary"
              size="lg"
              href="mailto:abdullah@alfain.tech?subject=NanoAgent%20Gateway%20-%20Enterprise%20enquiry"
            >
              Talk to sales
            </Button>
          </div>
        </div>

        {/* Code panel side */}
        <div className="border border-[var(--color-border)] rounded-none bg-gradient-to-b from-[var(--color-surface)] to-[var(--color-bg-2)] overflow-hidden">
          <div className="p-[22px_24px] mb-0">
            <div>
              <h2 className="text-xl m-0">Drop-in OpenAI-compatible proxy</h2>
              <p className="mt-1 text-[var(--color-text-mut)] text-sm m-0">
                Change one base URL. The Gateway speaks the OpenAI API your
                tools already use.
              </p>
            </div>
          </div>

          <div className="border-t border-[var(--color-border)]">
            <div className="p-[6px_8px] bg-[var(--color-bg-2)] border-b border-[var(--color-border)] flex gap-1">
              {gatewayCodeTabs.map((tab) => (
                <button
                  key={tab.id}
                  className={`border-0 bg-transparent text-[var(--color-text-mut)] font-sans text-[13px] font-semibold p-[6px_14px] rounded-lg cursor-pointer transition-all duration-150 hover:text-[var(--color-text)] hover:bg-white/5 ${
                    activeTab === tab.id
                      ? "!text-[var(--color-text)] !bg-[rgba(124,140,255,0.16)]"
                      : ""
                  }`}
                  onClick={() => setActiveTab(tab.id)}
                >
                  {tab.label}
                </button>
              ))}
            </div>
            <div className="relative">
              {gatewayCodeTabs.map((tab) => (
                <CodeBlock
                  key={tab.id}
                  code={tab.code}
                  isActive={activeTab === tab.id}
                />
              ))}
              <button
                className="absolute top-3 right-3 border border-[var(--color-border)] bg-white/5 text-[var(--color-text-mut)] font-semibold text-xs px-3 py-[6px] rounded-md cursor-pointer transition-all duration-150 z-10 hover:bg-white/10 hover:text-[var(--color-text)]"
                onClick={handleCopy}
              >
                {copied === activeTab ? "Copied!" : "Copy"}
              </button>
            </div>
          </div>

          <p className="mx-4 mb-4 mt-3 text-left text-[13px] text-[var(--color-text-dim)]">
            Standard routes: <code className="font-mono text-[var(--color-text-mut)] text-[12.5px] bg-white/5 px-[6px] py-[1px] rounded">POST /v1/chat/completions</code> and{" "}
            <code className="font-mono text-[var(--color-text-mut)] text-[12.5px] bg-white/5 px-[6px] py-[1px] rounded">GET /v1/models</code>. Streaming responses, headers, and
            finish reasons pass through transparently.
          </p>
        </div>
      </div>
    </section>
  );
}
