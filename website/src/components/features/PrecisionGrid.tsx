"use client";

import Container from "@/components/ui/Container";

interface PrecisionCard {
  icon: React.ReactNode;
  title: string;
  description: string;
}

const cards: PrecisionCard[] = [
  {
    icon: (
      <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
        <circle cx="11" cy="11" r="6.5" />
        <path d="M16 16 21 21" />
      </svg>
    ),
    title: "Repository Understanding",
    description:
      "NanoAgent builds a semantic graph of your entire codebase locally. It knows where things are and how they connect.",
  },
  {
    icon: (
      <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
        <path d="M6 18V8" />
        <path d="M6 18h12" />
        <circle cx="6" cy="6" r="2" />
        <circle cx="18" cy="8" r="2" />
        <circle cx="14" cy="18" r="2" />
        <path d="M7.8 7 16.2 7" />
        <path d="M16.6 9.7 14.9 16.2" />
      </svg>
    ),
    title: "Strategic Planning",
    description:
      "Before writing a single line of code, the agent formulates a step-by-step plan for your approval.",
  },
  {
    icon: (
      <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
        <path d="M4 20h4" />
        <path d="M14.5 5.5 18.5 9.5" />
        <path d="M6.5 17.5 17 7a1.414 1.414 0 0 0-2-2L4.5 15.5 4 20l4.5-.5Z" />
      </svg>
    ),
    title: "Precision Editing",
    description:
      "Applies targeted modifications to files. No messy copy-pasting, just clean, structural diffs.",
  },
  {
    icon: (
      <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
        <path d="M12 21s7-3.5 7-10V5l-7-2-7 2v6c0 6.5 7 10 7 10Z" />
        <path d="m9.4 11.7 1.8 1.8 3.8-4.2" />
      </svg>
    ),
    title: "Automated Validation",
    description:
      "Runs your tests and linters in the background, automatically fixing errors until the build goes green.",
  },
  {
    icon: (
      <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
        <path d="M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6-9.5-6-9.5-6Z" />
        <circle cx="12" cy="12" r="2.5" />
      </svg>
    ),
    title: "Human Review",
    description:
      "You remain in the driver's seat. Review the proposed diffs before they are committed.",
  },
];

export default function PrecisionGrid() {
  return (
    <section className="section container" id="precision">
      <Container>
        <header className="text-center mb-[46px] max-w-[680px] mx-auto">
          <h2 className="text-[clamp(26px,3.4vw,38px)] leading-[1.12] tracking-[-0.02em] font-extrabold mt-0 mx-0 mb-3">
            Precision engineered for complex codebases.
          </h2>
          <p className="text-[16.5px] text-[var(--color-text-mut)] mt-0 mx-auto max-w-[640px]">
            NanoAgent isn&apos;t a chatbot. It&apos;s a structured workflow engine that operates
            autonomously but transparently.
          </p>
        </header>

        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-4">
          {cards.map((card) => (
            <article
              key={card.title}
              className="bg-gradient-to-b from-[var(--color-surface)] to-[var(--color-bg-2)] border border-[var(--color-border)] rounded-2xl p-[26px_22px] transition-all duration-200 hover:-translate-y-1 hover:border-[var(--color-border-2)] hover:shadow-[0_20px_50px_-30px_rgba(124,140,255,0.5)] flex flex-col"
            >
              <div
                className="w-[48px] h-[48px] grid place-items-center rounded-xl bg-[rgba(124,140,255,0.1)] border border-[var(--color-border)] mb-4 text-[var(--color-acc-1)]"
                aria-hidden="true"
              >
                {card.icon}
              </div>
              <h3 className="m-0 mb-2 text-[16px] tracking-[-0.01em] font-semibold">
                {card.title}
              </h3>
              <p className="m-0 text-[14px] text-[var(--color-text-mut)] leading-[1.55]">
                {card.description}
              </p>
            </article>
          ))}
        </div>
      </Container>
    </section>
  );
}
