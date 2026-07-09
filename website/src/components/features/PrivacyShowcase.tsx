"use client";

import Container from "@/components/ui/Container";

interface PrivacyPoint {
  icon: React.ReactNode;
  title: string;
  description: string;
}

const lockIcon = (
  <svg viewBox="0 0 24 24" width="32" height="32" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
    <rect x="5" y="11" width="14" height="9" rx="2" />
    <path d="M8.5 11V8.5A3.5 3.5 0 0 1 12 5a3.5 3.5 0 0 1 3.5 3.5V11" />
  </svg>
);

const points: PrivacyPoint[] = [
  {
    icon: (
      <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
        <rect x="5" y="10" width="14" height="10" rx="2" />
        <path d="M8 10V7.5A4 4 0 0 1 12 3.5a4 4 0 0 1 4 4V10" />
      </svg>
    ),
    title: "Total Privacy",
    description: "Operates entirely within your local environment or secure VPC.",
  },
  {
    icon: (
      <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
        <rect x="7" y="7" width="10" height="10" rx="1.5" />
        <path d="M9 1.5v3" />
        <path d="M15 1.5v3" />
        <path d="M9 19.5v3" />
        <path d="M15 19.5v3" />
        <path d="M1.5 9h3" />
        <path d="M1.5 15h3" />
        <path d="M19.5 9h3" />
        <path d="M19.5 15h3" />
      </svg>
    ),
    title: "Zero Latency",
    description: "Local models mean instant responses and no rate limits.",
  },
  {
    icon: (
      <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
        <ellipse cx="12" cy="5.5" rx="6.5" ry="3" />
        <path d="M5.5 5.5v6c0 1.7 2.9 3 6.5 3s6.5-1.3 6.5-3v-6" />
        <path d="M5.5 11.5v6c0 1.7 2.9 3 6.5 3s6.5-1.3 6.5-3v-6" />
      </svg>
    ),
    title: "Local Indexing",
    description: "Vector embeddings are stored locally, avoiding third-party data collection.",
  },
];

export default function PrivacyShowcase() {
  return (
    <section className="pt-24 pb-0" id="start">
      <Container>
        <div className="grid grid-cols-1 lg:grid-cols-[1.2fr_1fr] gap-12 items-center">
          {/* Copy */}
          <div>
            <h2 className="text-[clamp(32px,4vw,48px)] leading-[1.08] tracking-[-0.03em] font-extrabold m-0">
              <span className="block">Your code.</span>
              <span className="block">Your machine.</span>
              <span className="block text-gradient-nano">Zero compromise.</span>
            </h2>
            <p className="mt-4 text-[16.5px] text-[var(--color-text-mut)] max-w-[520px] mb-8">
              In a world of cloud-hosted AI, NanoAgent takes a different path. Your
              proprietary source code never leaves your workstation unless you push it.
            </p>

            <ul className="list-none m-0 p-0 flex flex-col gap-5">
              {points.map((point) => (
                <li key={point.title} className="flex gap-4 items-start">
                  <span
                    className="flex-none w-[44px] h-[44px] grid place-items-center rounded-xl bg-[rgba(124,140,255,0.08)] border border-[var(--color-border)] text-[var(--color-acc-1)]"
                    aria-hidden="true"
                  >
                    {point.icon}
                  </span>
                  <div>
                    <h3 className="m-0 text-[15.5px] font-semibold tracking-[-0.01em]">
                      {point.title}
                    </h3>
                    <p className="m-0 mt-1 text-[14px] text-[var(--color-text-mut)] leading-[1.5]">
                      {point.description}
                    </p>
                  </div>
                </li>
              ))}
            </ul>
          </div>

          {/* Orbit visual */}
          <div className="hidden lg:flex items-center justify-center" aria-hidden="true">
            <div className="relative w-[320px] h-[320px]">
              {/* Outer ring */}
              <div className="absolute inset-0 rounded-full border border-[rgba(124,140,255,0.12)]" />
              {/* Mid ring */}
              <div className="absolute inset-[32px] rounded-full border border-[rgba(124,140,255,0.08)]" />
              {/* Inner ring */}
              <div className="absolute inset-[72px] rounded-full border border-[rgba(124,140,255,0.06)]" />

              {/* Orbiting dots */}
              <span className="absolute top-[-4px] left-1/2 -translate-x-1/2 w-[8px] h-[8px] rounded-full bg-[var(--color-acc-1)] shadow-[0_0_12px_rgba(110,231,255,0.6)] animate-pulse" />
              <span className="absolute right-[-4px] top-1/2 -translate-y-1/2 w-[8px] h-[8px] rounded-full bg-[var(--color-acc-2)] shadow-[0_0_12px_rgba(30,200,235,0.6)] animate-pulse" style={{ animationDelay: "0.3s" }} />
              <span className="absolute bottom-[-4px] left-1/2 -translate-x-1/2 w-[8px] h-[8px] rounded-full bg-[var(--color-acc-1)] shadow-[0_0_12px_rgba(110,231,255,0.6)] animate-pulse" style={{ animationDelay: "0.6s" }} />
              <span className="absolute left-[-4px] top-1/2 -translate-y-1/2 w-[8px] h-[8px] rounded-full bg-[var(--color-acc-2)] shadow-[0_0_12px_rgba(30,200,235,0.6)] animate-pulse" style={{ animationDelay: "0.9s" }} />

              {/* Core lock */}
              <div className="absolute inset-0 flex items-center justify-center">
                <div className="w-[90px] h-[90px] rounded-full bg-gradient-to-b from-[var(--color-surface)] to-[var(--color-bg-2)] border border-[var(--color-border)] flex items-center justify-center shadow-[0_0_40px_rgba(124,140,255,0.15)]">
                  <span className="text-[var(--color-acc-1)]">{lockIcon}</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </Container>
    </section>
  );
}
