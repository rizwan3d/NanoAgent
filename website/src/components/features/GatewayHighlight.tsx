"use client";

import { useState } from "react";
import Container from "@/components/ui/Container";
import CodeBlock from "@/components/ui/CodeBlock";

interface GatewayCard {
  icon: React.ReactNode;
  title: string;
  description: string;
}

const cards: GatewayCard[] = [
  {
    icon: (
      <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
        <path d="M12 3.5 18 6v5.5c0 3.8-2.3 7.2-6 8.9-3.7-1.7-6-5.1-6-8.9V6l6-2.5Z" />
        <path d="M9.5 11.5 11 13l3.5-3.5" />
      </svg>
    ),
    title: "Security",
    description: "Track how teams use models and watch for anomalies as adoption grows.",
  },
  {
    icon: (
      <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
        <path d="M12 4v16" />
        <path d="M17 7.5c0-1.9-2.2-3.5-5-3.5S7 5.6 7 7.5 9.2 11 12 11s5 1.6 5 3.5S14.8 18 12 18s-5-1.6-5-3.5" />
      </svg>
    ),
    title: "Finance",
    description: "Attribute usage by team, app, project, and user so budgets stop being guesswork.",
  },
  {
    icon: (
      <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
        <path d="M12 4 5 7.5 12 11l7-3.5L12 4Z" />
        <path d="M5 12.5 12 16l7-3.5" />
        <path d="M5 17.5 12 21l7-3.5" />
      </svg>
    ),
    title: "Platform",
    description: "Keep clients pointed at one endpoint while your team manages changes behind the scenes.",
  },
];

const aisdkCode = `import { streamText } from 'ai'
import { createOpenAI } from '@ai-sdk/openai'

const nano = createOpenAI({
  baseURL: 'https://app.getnanoai.com/v1',
  apiKey: process.env.NANOAGENT_API_KEY,
})

const result = streamText({
  model: nano.chat('anthropic/claude-opus-4-8'),
  prompt: 'Why is the sky blue?',
})`;

const pythonCode = `import os
from openai import OpenAI

client = OpenAI(
    base_url="https://app.getnanoai.com/v1",
    api_key=os.environ["NANOAGENT_API_KEY"],
)

stream = client.chat.completions.create(
    model="anthropic/claude-opus-4-8",
    messages=[{"role": "user", "content": "Why is the sky blue?"}],
    stream=True,
)`;

const curlCode = `# point any OpenAI-compatible client at the Gateway
curl https://app.getnanoai.com/v1/chat/completions \\
  -H "Authorization: Bearer $NANOAGENT_API_KEY" \\
  -H "Content-Type: application/json" \\
  -d '{
    "model": "anthropic/claude-opus-4-8",
    "messages": [{"role": "user", "content": "Why is the sky blue?"}],
    "stream": true
  }'`;

const codeSnippets = [
  { id: "aisdk", label: "AI SDK", code: aisdkCode, language: "typescript" as const },
  { id: "python", label: "Python", code: pythonCode, language: "python" as const },
  { id: "curl", label: "curl", code: curlCode, language: "bash" as const },
];

export default function GatewayHighlight() {
  const [activeTab, setActiveTab] = useState("aisdk");

  return (
    <section className="pt-6 mt-24">
      <Container>
        <div className="gateway-highlight">
          {/* Intro */}
          <div className="mb-[34px]">
            <h2 className="m-0 text-[clamp(34px,5vw,54px)] leading-[1.02] font-bold tracking-[-0.05em] text-[#f4f7fb]">
              One gateway, every model.
            </h2>
            <p className="mt-[18px] mx-0 mb-0 text-[16px] leading-[1.75] text-[var(--color-text-mut)] max-w-[640px]">
              NanoAgent Gateway is the control plane for enterprise AI. Put a single
              OpenAI-compatible endpoint in front of your traffic and give security, finance,
              and platform teams the visibility and control they need.
            </p>
          </div>

          {/* Grid: cards + code window */}
          <div className="grid grid-cols-1 lg:grid-cols-[minmax(0,0.98fr)_minmax(0,0.92fr)] gap-7 items-stretch">
            {/* Card stack */}
            <div className="grid gap-4" aria-label="Gateway capabilities">
              {cards.map((card) => (
                <article
                  key={card.title}
                  className="grid grid-cols-[auto_minmax(0,1fr)] gap-4 items-start p-[18px_18px_17px]"
                >
                  <span
                    className="w-10 h-10 grid place-items-center text-[#19d3ff] bg-[rgba(21,194,240,0.12)] rounded-xl"
                    aria-hidden="true"
                  >
                    {card.icon}
                  </span>
                  <div>
                    <h3 className="m-[1px_0_6px] text-[24px] leading-[1.05] tracking-[-0.03em]">
                      {card.title}
                    </h3>
                    <p className="m-0 text-[15px] leading-[1.6] text-[var(--color-text-mut)]">
                      {card.description}
                    </p>
                  </div>
                </article>
              ))}
            </div>

            {/* Code window */}
            <div className="min-w-0">
              <div className="h-full border border-[rgba(255,255,255,0.07)] bg-gradient-to-b from-[rgba(29,32,42,0.98)] to-[rgba(24,27,36,0.98)] shadow-[inset_0_1px_0_rgba(255,255,255,0.03)] overflow-hidden rounded-xl">

                {/* Tabs */}
                <div className="flex border-b border-[rgba(255,255,255,0.06)]" role="tablist">
                  {codeSnippets.map((tab) => (
                    <button
                      key={tab.id}
                      onClick={() => setActiveTab(tab.id)}
                      className="code-tab"
                      role="tab"
                      aria-selected={tab.id === activeTab}
                    >
                      {tab.label}
                    </button>
                  ))}
                </div>

                {/* Code body */}
                <pre
                  className="m-0 p-[28px_24px_30px] border-0 bg-transparent text-[14px] leading-[1.75] font-[var(--font-mono)] overflow-x-auto"
                  role="tabpanel"
                >
                  <CodeBlock
                    code={codeSnippets.find(t => t.id === activeTab)?.code ?? ""}
                    language={codeSnippets.find(t => t.id === activeTab)?.language ?? "typescript"}
                  />
                </pre>
              </div>
            </div>
          </div>

          {/* Actions / CTA */}
          <div className="flex justify-center mt-[34px]">
            <a
              href="/gateway"
              className="inline-flex items-center justify-center gap-2 font-semibold leading-none rounded-full border cursor-pointer transition-all duration-200 whitespace-nowrap text-[#06121a] bg-[var(--color-acc-2)] shadow-[0_8px_30px_-10px_rgba(124,140,255,0.6)] hover:bg-[#93a0ff] hover:-translate-y-0.5 hover:shadow-[0_14px_40px_-10px_rgba(124,140,255,0.7)] text-[15px] px-6 py-[14px]"
            >
              Explore the Gateway
              <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                <path d="M5 12h14" />
                <path d="m13 6 6 6-6 6" />
              </svg>
            </a>
          </div>
        </div>
      </Container>
    </section>
  );
}
