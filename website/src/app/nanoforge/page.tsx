"use client";

import { useEffect, useRef, useCallback } from "react";
import Container from "@/components/ui/Container";
import Button from "@/components/ui/Button";
import { siteConfig } from "@/lib/data";

/* ─────────────────────────────────────────────
   Data
   ───────────────────────────────────────────── */
const buildLoopSteps = [
  {
    num: "01",
    label: "PROMPT",
    title: "Describe it",
    description:
      "Create a project and a dedicated PostgreSQL database is provisioned, seeded from the full-stack template. Then just ask.",
  },
  {
    num: "02",
    label: "BUILD",
    title: "Agent works",
    description:
      "NanoAgent edits files, runs the dev server, and commits as it goes — all inside a sealed per-project container.",
  },
  {
    num: "03",
    label: "PREVIEW",
    title: "See it live",
    description:
      "A hot-reloaded preview streams to your browser through the proxy, so you watch the app take shape in real time.",
  },
  {
    num: "04",
    label: "SHIP",
    title: "Review & deploy",
    description:
      "Read the diff, merge the branch, and deploy the single full-stack build to Docker or a VPS.",
  },
] as const;

const stackItems = [
  {
    icon: "\uD83D\uDDA5\uFE0F",
    title: "Frontend",
    description:
      "React + Vite + TypeScript. A fast, modern SPA with hot reload — the agent builds components, routes, and state for you.",
  },
  {
    icon: "\uD83D\uDD0C",
    title: "API",
    description:
      "Express + TypeScript. Typed routes and handlers that compile alongside the frontend into one deployable process.",
  },
  {
    icon: "\uD83D\uDC18",
    title: "Database",
    description:
      "Managed PostgreSQL. A dedicated database provisioned per project and wired into the app from day one.",
  },
] as const;

const ownItItems = [
  {
    icon: "\uD83E\uDDE9",
    title: "Real GitHub repo, never a black box",
    description:
      "Every project is a real repo with a clean commit history under your org. Read it, diff it, branch it, and take it with you any time.",
    wide: true,
  },
  {
    icon: "\uD83D\uDE4C",
    title: "You review every diff",
    description:
      "The agent commits as it works and surfaces each change. Nothing merges that you didn't see and approve.",
    wide: false,
  },
  {
    icon: "\uD83D\uDEE0\uFE0F",
    title: "Bring your own model",
    description:
      "Run each build on the model and provider you choose — your keys, no lock-in.",
    wide: false,
  },
  {
    icon: "\uD83E\uDD14",
    title: "Tuned for build work",
    description:
      "Sessions run with thinking and high reasoning effort, so the agent plans before it writes.",
    wide: false,
  },
  {
    icon: "\uD83D\uDCC1",
    title: "Yours to keep",
    description:
      "Export, self-host, or hand off to your team — the project leaves with you.",
    wide: false,
  },
] as const;

const howItBuildsItems = [
  {
    icon: "\uD83E\uDEB5",
    title: "Reviewable by design",
    description:
      "Every change lands as a commit in a git history you can read, diff, and revert.",
    wide: false,
  },
  {
    icon: "\uD83C\uDF3F",
    title: "Branch-per-session",
    description:
      "Each build runs on its own branch, so parallel experiments never collide.",
    wide: false,
  },
  {
    icon: "\uD83D\uDCE1",
    title: "Live status & commits",
    description:
      "Watch sessions move through Starting, Running, Stopped — with the latest commit and any error surfaced.",
    wide: false,
  },
  {
    icon: "\uD83D\uDEE1\uFE0F",
    title: "Isolated & secure",
    description:
      "Each project builds in its own container with secrets sealed in an encrypted store — tenants stay fully separated.",
    wide: false,
  },
  {
    icon: "\u2699\uFE0F",
    title: "Open-source engine",
    description:
      "It\u2019s the same NanoAgent you can run yourself — no proprietary magic, just an agent that shows its work.",
    wide: true,
  },
] as const;

const shipItems = [
  {
    icon: "\uD83D\uDCE6",
    title: "One build, ship anywhere",
    description:
      "Frontend and API compile into a single Express process — drop it on Docker or a VPS and go live.",
  },
  {
    icon: "\uD83D\uDC18",
    title: "Real database, provisioned",
    description:
      "Every project gets a dedicated PostgreSQL database, wired into the app from the first prompt.",
  },
] as const;

const sectionNavItems = [
  { id: "loop", label: "The build loop" },
  { id: "stack", label: "The stack" },
  { id: "own", label: "You own it" },
  { id: "agent", label: "How it builds" },
  { id: "ship", label: "Ship anywhere" },
] as const;

/* ─────────────────────────────────────────────
   Reusable sub-components
   ───────────────────────────────────────────── */

function Eyebrow({ children }: { children: React.ReactNode }) {
  return (
    <span className="inline-block text-[13px] font-bold tracking-[0.08em] uppercase text-[var(--color-acc-1)]">
      {children}
    </span>
  );
}

function SectionHeading({ children }: { children: React.ReactNode }) {
  return (
    <h2 className="text-[clamp(26px,3.4vw,38px)] leading-[1.12] tracking-[-0.02em] font-extrabold mt-3 mb-0">
      {children}
    </h2>
  );
}

function SectionDescription({ children }: { children: React.ReactNode }) {
  return (
    <p className="mt-4 text-[16.5px] text-[var(--color-text-mut)] max-w-[640px]">
      {children}
    </p>
  );
}

/* ─────────────────────────────────────────────
   Section Nav (sticky anchor navigation)
   ───────────────────────────────────────────── */
function SectionNav() {
  const scrollTo = useCallback((e: React.MouseEvent<HTMLAnchorElement>, id: string) => {
    e.preventDefault();
    const el = document.getElementById(id);
    if (el) {
      el.scrollIntoView({ behavior: "smooth" });
    }
  }, []);

  return (
    <nav
      className="sticky top-[67px] z-40 bg-[rgba(0,0,0,0.82)] backdrop-blur-[14px] border-b border-[var(--color-border)]"
      aria-label="NanoForge sections"
    >
      <div className="w-full max-w-[1160px] mx-auto px-6 flex gap-2 overflow-x-auto py-3 scrollbar-hide">
        {sectionNavItems.map((item) => (
          <a
            key={item.id}
            href={`#${item.id}`}
            onClick={(e) => scrollTo(e, item.id)}
            className="flex-none text-[13px] font-semibold text-[var(--color-text-mut)] px-[14px] py-2 rounded-full border border-[var(--color-border)] bg-[var(--color-surface)] whitespace-nowrap transition-all duration-150 hover:text-[var(--color-text)] hover:border-[var(--color-border-2)]"
          >
            {item.label}
          </a>
        ))}
      </div>
    </nav>
  );
}

/* ─────────────────────────────────────────────
   RevealOnScroll — wraps children with staggered
   intersection-observer reveal animation
   ───────────────────────────────────────────── */
function RevealSection({ children, className = "" }: { children: React.ReactNode; className?: string }) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const el = ref.current;
    if (!el || !("IntersectionObserver" in window)) {
      el?.classList.add("is-in");
      return;
    }
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting) {
          el.classList.add("is-in");
          observer.unobserve(el);
        }
      },
      { threshold: 0.12, rootMargin: "0px 0px -40px 0px" }
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  return (
    <div ref={ref} className={`reveal ${className}`}>
      {children}
    </div>
  );
}

/* ─────────────────────────────────────────────
   Prompt simulation box (hero)
   ───────────────────────────────────────────── */
function PromptBox() {
  return (
    <div
      className="border border-[var(--color-border-2)] bg-gradient-to-b from-[var(--color-surface)] to-[#050505]
                 shadow-[0_20px_60px_-30px_rgba(124,140,255,0.5)] overflow-hidden mt-[26px] mb-2"
      aria-hidden="true"
    >
      {/* Top bar */}
      <div className="flex items-center gap-2 px-[14px] py-[10px] border-b border-[var(--color-border)] bg-[var(--color-surface)] font-mono text-[12px] text-[var(--color-text-dim)]">
        <b className="text-[var(--color-acc-1)] font-bold">Forge</b>
        <span className="text-[var(--color-text-mut)]">/ acme-dashboard</span>
        <span className="text-[10px] opacity-50">&middot;</span>
        <span className="text-[var(--color-acc-1)]">main</span>
      </div>

      {/* Body with typewriter effect */}
      <div className="flex items-center gap-2 px-4 py-4 font-mono text-[14px] leading-relaxed text-[var(--color-text)]">
        <span className="text-[var(--color-acc-1)] font-bold">&gt;</span>
        <span className="animate-typewriter max-w-0 border-r-2 border-[var(--color-acc-1)]"
              style={{ animation: "typewriter 2.5s steps(45) 0.5s forwards" }}>
          Build a team dashboard with auth, projects, and a Postgres-backed activity feed
        </span>
      </div>

      {/* Footer */}
      <div className="flex items-center justify-between px-[14px] py-[10px] border-t border-[var(--color-border)]">
        <span className="font-mono text-[12px] text-[var(--color-text-mut)] border border-[var(--color-border)] px-[10px] py-[4px] rounded-full">
          claude-opus-4-8 &middot; High
        </span>
        <span
          className="w-[34px] h-[34px] flex items-center justify-center rounded-full
                     bg-gradient-to-r from-[var(--color-acc-2)] via-[#7c8cff] to-[var(--color-acc-3)]
                     shadow-[0_6px_18px_-6px_rgba(124,140,255,0.7)]"
        >
          &uarr;
        </span>
      </div>
    </div>
  );
}

/* ─────────────────────────────────────────────
   Visual frame (macOS-style window chrome)
   ───────────────────────────────────────────── */
function VisualFrame({ children }: { children: React.ReactNode }) {
  return (
    <div className="border border-[var(--color-border)] overflow-hidden relative z-10">
      <div className="flex items-center gap-2 px-[14px] py-[10px] bg-[rgba(255,255,255,0.03)] border-b border-[rgba(255,255,255,0.06)]">
        <span className="w-[10px] h-[10px] rounded-full bg-[#ff5f57]" />
        <span className="w-[10px] h-[10px] rounded-full bg-[#febc2e]" />
        <span className="w-[10px] h-[10px] rounded-full bg-[#28c840]" />
        <span className="text-[var(--color-text-dim)] text-xs ml-2 font-mono">
          nanoforge &middot; acme-dashboard
        </span>
      </div>
      {children}
    </div>
  );
}

/* ─────────────────────────────────────────────
   Card hover lift wrapper
   ───────────────────────────────────────────── */
function HoverCard({
  children,
  className = "",
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div
      className={`h-full border border-[var(--color-border)] bg-[rgba(255,255,255,0.02)] p-7
                  transition-all duration-200 hover:-translate-y-1
                  hover:border-[var(--color-border-2)]
                  hover:shadow-[0_20px_50px_-30px_rgba(124,140,255,0.45)] ${className}`}
    >
      {children}
    </div>
  );
}

/* ─────────────────────────────────────────────
   CTA Section
   ───────────────────────────────────────────── */
function CTASection() {
  return (
    <Container>
      <div className="relative overflow-hidden text-center border border-[rgba(255,255,255,0.08)] bg-gradient-to-b from-[rgba(8,10,15,0.98)] to-[rgba(5,7,10,0.98)] px-6 py-[72px] pb-[78px]">
        <div
          className="w-14 h-14 mx-auto mb-6 flex items-center justify-center bg-gradient-to-br from-[var(--color-acc-2)] to-[#7c8cff] shadow-lg shadow-[var(--color-acc-2)]/20"
          aria-hidden="true"
        >
          <img src="/assets/logo.png" alt="" width={22} height={22} />
        </div>

        <h2 className="text-[clamp(22px,3vw,32px)] font-bold tracking-tight mb-4">
          Ship full-stack apps faster with NanoForge.
        </h2>
        <p className="text-[var(--color-text-mut)] max-w-lg mx-auto mb-8 leading-relaxed">
          Scaffold, build, preview, and ship &mdash; backed by real git and a real
          database, with you reviewing every change.
        </p>
        <div className="flex flex-wrap justify-center gap-4">
          <Button href={siteConfig.appUrl} size="lg">
            Start building
          </Button>
          <Button
            href="mailto:abdullah@alfain.tech?subject=NanoForge%20%E2%80%94%20Full-Stack%20App%20Builder%20enquiry"
            variant="ghost"
            size="lg"
          >
            Talk to sales
          </Button>
        </div>

        {/* Ambient glow */}
        <div
          className="absolute -top-20 left-1/2 -translate-x-1/2 w-[600px] h-[300px]
                     bg-gradient-to-br from-[var(--color-acc-2)]/5 via-[#7c8cff]/5
                     to-transparent blur-3xl pointer-events-none"
          aria-hidden="true"
        />
      </div>
    </Container>
  );
}

/* ═══════════════════════════════════════════════
   PAGE
   ═══════════════════════════════════════════════ */
export default function NanoForgePage() {
  return (
    <>
      {/* Section Nav */}
      <SectionNav />

      {/* ════════════════════════════════════════
          HERO
          ════════════════════════════════════════ */}
      <section className="relative overflow-hidden px-6 pt-16 pb-8 md:pt-24 md:pb-16">
        <div className="mx-auto max-w-[1160px] grid gap-12 md:grid-cols-2 md:gap-16 items-center">
          {/* Copy */}
          <RevealSection className="max-w-xl">
            <Eyebrow>NanoForge &middot; Full-Stack App Builder</Eyebrow>
            <h1 className="text-[clamp(32px,5vw,58px)] font-extrabold leading-[1.08] tracking-tight mt-4 mb-5">
              From prompt to
              <br />
              <span className="text-gradient-nano">production-grade app</span>.
            </h1>
            <p className="text-[var(--color-text-mut)] text-[17px] leading-relaxed mb-8">
              Describe what you want and NanoForge&#8217;s agent scaffolds,
              builds, and live-previews a real React&#160;+&#160;Express&#160;+&#160;PostgreSQL
              app inside an isolated container &#8212; backed by its own GitHub repo
              and database, with you reviewing every diff.
            </p>

            <PromptBox />

            <Button href={siteConfig.appUrl} size="lg" className="mt-4">
              Start building
            </Button>
          </RevealSection>

          {/* Screenshot */}
          <RevealSection>
            <div className="relative">
              <VisualFrame>
                <img
                  src="https://getnanoai.com/assets/forge.png"
                  alt="NanoForge building a full-stack app \u2014 the agent edits alongside a live preview"
                  className="w-full block"
                  loading="lazy"
                />
              </VisualFrame>
              <div
                className="absolute -inset-4 bg-gradient-to-br from-[var(--color-acc-2)]/10 via-[#7c8cff]/5 to-transparent blur-2xl pointer-events-none"
                aria-hidden="true"
              />
            </div>
          </RevealSection>
        </div>
      </section>

      {/* ════════════════════════════════════════
          THE BUILD LOOP
          ════════════════════════════════════════ */}
      <section className="px-6 py-20 md:py-28" id="loop">
        <Container>
          <RevealSection className="max-w-2xl mb-14">
            <Eyebrow>The build loop</Eyebrow>
            <SectionHeading>
              From prompt to production, on repeat
            </SectionHeading>
            <SectionDescription>
              Every change runs the same loop &#8212; you prompt, the agent works in
              an isolated container on its own branch, you preview the live
              result, then ship.
            </SectionDescription>
          </RevealSection>

         <div className="grid gap-5 md:grid-cols-4">
           {buildLoopSteps.map((step) => (
              <RevealSection key={step.label} className="h-full">
               <HoverCard className="p-6">
                  <span className="text-[var(--color-acc-1)] text-xs font-semibold tracking-wider">
                    {step.num} &middot; {step.label}
                  </span>
                  <h3 className="text-lg font-bold mt-3 mb-2">
                    {step.title}
                  </h3>
                  <p className="text-[var(--color-text-mut)] text-sm leading-relaxed">
                    {step.description}
                  </p>
                </HoverCard>
              </RevealSection>
            ))}
          </div>
        </Container>
      </section>

      {/* ════════════════════════════════════════
          THE STACK
          ════════════════════════════════════════ */}
      <section
        className="px-6 py-20 md:py-28 border-t border-[var(--color-border)]"
        id="stack"
      >
        <Container>
          <RevealSection className="max-w-2xl mb-14">
            <Eyebrow>The stack</Eyebrow>
            <SectionHeading>
              Production-shaped from the first prompt
            </SectionHeading>
            <SectionDescription>
              Not a throwaway sandbox &#8212; a real, three-layer full-stack app wired
              together and ready to deploy.
            </SectionDescription>
          </RevealSection>

         <div className="grid gap-5 md:grid-cols-3">
           {stackItems.map((item) => (
              <RevealSection key={item.title} className="h-full">
               <HoverCard>
                  <div className="flex items-center gap-3 text-sm font-semibold tracking-wide mb-4">
                    <span className="text-xl" aria-hidden="true">
                      {item.icon}
                    </span>
                    {item.title}
                  </div>
                  <p className="text-[var(--color-text-mut)] text-sm leading-relaxed">
                    <strong className="text-[var(--color-text)] font-semibold">
                      {item.description.split(".")[0]}.
                    </strong>
                    {item.description.slice(item.description.indexOf(".") + 1)}
                  </p>
                </HoverCard>
              </RevealSection>
            ))}
          </div>
        </Container>
      </section>

      {/* ════════════════════════════════════════
          YOU OWN IT (bento grid)
          ════════════════════════════════════════ */}
      <section
        className="px-6 py-20 md:py-28 border-t border-[var(--color-border)]"
        id="own"
      >
        <Container>
          <RevealSection className="max-w-2xl mb-14">
            <Eyebrow>You own it</Eyebrow>
            <SectionHeading>Real code. Your repo. Your model.</SectionHeading>
            <SectionDescription>
              The fastest path from idea to running app &#8212; without giving up the
              code, the control, or the model you trust.
            </SectionDescription>
          </RevealSection>

         <div className="grid gap-5 md:grid-cols-3">
           {ownItItems.map((item) => (
             <RevealSection
               key={item.title}
                className={`${item.wide ? "md:col-span-2" : "md:col-span-1"} h-full`}
             >
                <HoverCard>
                  <div className="text-2xl mb-4" aria-hidden="true">
                    {item.icon}
                  </div>
                  <h3 className="text-lg font-bold mb-2">{item.title}</h3>
                  <p className="text-[var(--color-text-mut)] text-sm leading-relaxed">
                    {item.description}
                  </p>
                </HoverCard>
              </RevealSection>
            ))}
          </div>
        </Container>
      </section>

      {/* ════════════════════════════════════════
          HOW IT BUILDS
          ════════════════════════════════════════ */}
      <section
        className="px-6 py-20 md:py-28 border-t border-[var(--color-border)]"
        id="agent"
      >
        <Container>
          <RevealSection className="max-w-2xl mb-14">
            <Eyebrow>Powered by NanoAgent</Eyebrow>
            <SectionHeading>How the agent builds</SectionHeading>
            <SectionDescription>
              The same open-source agent developers run on the desktop &#8212; now
              running server-side, in the open, to build your app.
            </SectionDescription>
          </RevealSection>

          <div className="grid gap-5 md:grid-cols-3">
           {howItBuildsItems.map((item) => (
            <RevealSection
              key={item.title}
               className={`${item.wide ? "md:col-span-2" : "md:col-span-1"} h-full`}
             >
                <HoverCard>
                  <div className="text-2xl mb-4" aria-hidden="true">
                    {item.icon}
                  </div>
                  <h3 className="text-lg font-bold mb-2">{item.title}</h3>
                  <p className="text-[var(--color-text-mut)] text-sm leading-relaxed">
                    {item.description}
                  </p>
                </HoverCard>
              </RevealSection>
            ))}
          </div>
        </Container>
      </section>

      {/* ════════════════════════════════════════
          SHIP ANYWHERE
          ════════════════════════════════════════ */}
      <section
        className="px-6 py-20 md:py-28 border-t border-[var(--color-border)]"
        id="ship"
      >
        <Container>
          <RevealSection className="max-w-2xl mb-14">
            <Eyebrow>Ship anywhere</Eyebrow>
            <SectionHeading>
              A product you can deploy today
            </SectionHeading>
            <SectionDescription>
              Not a demo you throw away &#8212; a real app you can put in front of
              your users.
            </SectionDescription>
          </RevealSection>

         <div className="grid gap-5 md:grid-cols-2">
           {shipItems.map((item) => (
              <RevealSection key={item.title} className="h-full">
               <HoverCard>
                  <div className="text-2xl mb-4" aria-hidden="true">
                    {item.icon}
                  </div>
                  <h3 className="text-lg font-bold mb-2">{item.title}</h3>
                  <p className="text-[var(--color-text-mut)] text-sm leading-relaxed">
                    {item.description}
                  </p>
                </HoverCard>
              </RevealSection>
            ))}
          </div>
        </Container>
      </section>

      {/* ════════════════════════════════════════
          CTA
          ════════════════════════════════════════ */}
      <section className="px-6 pb-24 md:pb-32 pt-20 md:pt-12">
        <CTASection />
      </section>
    </>
  );
}
