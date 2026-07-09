"use client";

import Container from "@/components/ui/Container";

interface ForgeCard {
  icon: React.ReactNode;
  title: string;
  description: string;
}

const cards: ForgeCard[] = [
  {
    icon: (
      <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
        <path d="M8 8 4 12l4 4" />
        <path d="m16 8 4 4-4 4" />
        <path d="M14 5 10 19" />
      </svg>
    ),
    title: "Full-stack generation",
    description:
      "React frontend, Express API, and PostgreSQL schema generated together from one description.",
  },
  {
    icon: (
      <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
        <path d="M12 3.5 19 7.5v9L12 20.5 5 16.5v-9L12 3.5Z" />
        <path d="M5 7.5 12 11l7-3.5" />
        <path d="M12 11v9.5" />
      </svg>
    ),
    title: "Isolated preview environment",
    description:
      "Each app runs in its own container with a live URL and hot reload before it touches your machine.",
  },
  {
    icon: (
      <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
        <path d="M15 7a3 3 0 1 1 2.12 5.12L13 16.24V20" />
        <path d="M9 17a3 3 0 1 1-2.12-5.12L11 7.76V4" />
      </svg>
    ),
    title: "Review every diff",
    description:
      "Changes are committed to a dedicated GitHub repo so you can inspect, approve, or edit before merging.",
  },
];

export default function NanoForgeHighlight() {
  return (
    <section className="pt-6 mt-20">
      <Container>
        <div className="gateway-highlight forge-highlight">
          {/* Eyebrow */}
          <span
            className="inline-flex items-center px-3 py-[7px] mb-[18px] text-[12px] font-bold tracking-[0.02em]"
            style={{
              border: "1px solid rgba(30,200,235,.16)",
              color: "#67dcf4",
              background: "rgba(30,200,235,.08)",
            }}
          >
            NanoForge · Full-stack app builder
          </span>

          {/* Intro */}
          <div className="mb-[34px]">
            <h2 className="m-0 text-[clamp(34px,5vw,54px)] leading-[1.02] font-bold tracking-[-0.05em] text-[#f4f7fb]">
              Describe what you want. NanoForge builds it.
            </h2>
            <p className="mt-[18px] mx-0 mb-0 text-[16px] leading-[1.75] text-[var(--color-text-mut)] max-w-[640px]">
              NanoForge&apos;s agent scaffolds, builds, and live-previews a real React + Express +
              PostgreSQL app in an isolated container, backed by its own GitHub repo and database.
            </p>
          </div>

         {/* Grid: cards + screenshot */}
         <div
           className="grid grid-cols-1 lg:grid-cols-[minmax(0,0.95fr)_minmax(0,1fr)] gap-7 items-stretch"
         >
            {/* Card stack */}
            <div className="grid gap-4" aria-label="NanoForge capabilities">
              {cards.map((card) => (
                <article
                  key={card.title}
                  className="grid grid-cols-[auto_minmax(0,1fr)] gap-4 items-start p-[18px_18px_17px]"
                >
                  <span
                    className="w-10 h-10 grid place-items-center"
                    style={{
                      color: "#7b6cff",
                      background: "rgba(123,108,255,.12)",
                    }}
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

            {/* Screenshot panel */}
            <div className="min-w-0 flex items-stretch">
              <div className="w-full h-full border border-[rgba(255,255,255,.08)] overflow-hidden flex items-center justify-center bg-[#0a0c12] min-h-[320px]">
                <div className="w-full p-8 text-center">
                  <div
                    className="w-[64px] h-[64px] mx-auto mb-5 rounded-xl flex items-center justify-center"
                    style={{
                      color: "#7b6cff",
                      background: "rgba(123,108,255,.12)",
                    }}
                  >
                    <svg viewBox="0 0 24 24" width="30" height="30" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                      <polygon points="5 3 19 12 5 21 5 3" />
                    </svg>
                  </div>
                  <p className="font-semibold text-[var(--color-text)] mb-1 text-[15px]">
                    NanoForge Preview
                  </p>
                  <p className="text-[13px] text-[var(--color-text-dim)] leading-[1.6] max-w-[260px] mx-auto">
                    Scaffolds, builds, and live-previews your full-stack app in an isolated container.
                  </p>
                </div>
              </div>
            </div>
          </div>

          {/* Actions / CTA */}
          <div className="flex justify-center mt-[34px]">
            <a
              href="/nanoforge"
              className="inline-flex items-center justify-center gap-2 font-semibold leading-none rounded-full border cursor-pointer transition-all duration-200 whitespace-nowrap text-[#06121a] bg-[var(--color-acc-2)] shadow-[0_8px_30px_-10px_rgba(124,140,255,0.6)] hover:bg-[#93a0ff] hover:-translate-y-0.5 hover:shadow-[0_14px_40px_-10px_rgba(124,140,255,0.7)] text-[15px] px-6 py-[14px]"
            >
              Explore NanoForge
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
