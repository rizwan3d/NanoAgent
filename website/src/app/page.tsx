"use client";

import { useEffect, useRef, useState } from "react";
import Container from "@/components/ui/Container";
import SectionHeader from "@/components/ui/SectionHeader";
import Card from "@/components/ui/Card";
import CTA from "@/components/features/CTA";
import Installer from "@/components/features/Installer";
import FeatureList from "@/components/features/FeatureList";
import ComparisonTable from "@/components/features/ComparisonTable";
import ProvidersList from "@/components/features/ProvidersList";
import Quickstart from "@/components/features/Quickstart";
import CodeBlock from "@/components/ui/CodeBlock";
import PrecisionGrid from "@/components/features/PrecisionGrid";
import PrivacyShowcase from "@/components/features/PrivacyShowcase";
import GatewayHighlight from "@/components/features/GatewayHighlight";
import NanoForgeHighlight from "@/components/features/NanoForgeHighlight";
import NuGetCard from "@/components/features/NuGetCard";
import { siteConfig, whyFeatures } from "@/lib/data";

type Platform = "vscode" | "vs" | "terminal" | "desktop" | "cicd";

const platformImages: Record<Platform, string> = {
  vscode: "/assets/vscode.png",
  vs: "/assets/vs.png",
  terminal: "/assets/cli.png",
  desktop: "/assets/desktop.png",
  cicd: "/assets/nano.gif",
};

const platformBadge: Record<Platform, string> = {
  vscode: "NanoAgent — VS Code",
  vs: "NanoAgent — Visual Studio",
  terminal: "nanoai",
  desktop: "NanoAgent — Desktop",
  cicd: "NanoAgent — CI/CD",
};

export default function HomePage() {
  const [activePlatform, setActivePlatform] = useState<Platform>("terminal");
  const visualRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    // Scroll reveal
    if (typeof window === "undefined" || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    if (!("IntersectionObserver" in window)) return;

    const targets = document.querySelectorAll(".reveal-target");
    const io = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add("is-in");
            io.unobserve(entry.target);
          }
        });
      },
      { rootMargin: "0px 0px -10% 0px", threshold: 0.08 }
    );
    targets.forEach((el) => io.observe(el));
    return () => io.disconnect();
  }, []);

  return (
    <>
      {/* ---- HERO ---- */}
      <section className="text-center max-w-[1000px] mx-auto px-6 pt-16 pb-6 reveal-target">
        <h1 className="text-[clamp(44px,7vw,72px)] leading-[0.98] tracking-[-0.05em] font-extrabold m-0 text-[#f3f6fb] [text-wrap:balance]">
          Code faster without
          <span className="grad">giving up control.</span>
        </h1>
        <p className="mt-[22px] mx-auto max-w-[640px] text-[17px] text-[var(--color-text-mut)]">
          NanoAgent runs on your machine. It understands your repository, plans changes, edits files, runs validation, and reviews diffs. Quietly capable, incredibly precise.
        </p>

        <Installer />

        {/* Visual */}
        <div className="relative max-w-[920px] mx-auto mt-14" ref={visualRef}>
          <div className="relative z-[1] border border-[var(--color-border-2)] rounded-[var(--radius)] bg-[#050505] overflow-hidden shadow-[0_30px_80px_-30px_rgba(0,0,0,0.8)]">
            <div className="flex items-center gap-[7px] px-[14px] py-[11px] bg-[var(--color-surface)] border-b border-[var(--color-border)]">
              <span className="w-[11px] h-[11px] rounded-full bg-[#ff5f57]" />
              <span className="w-[11px] h-[11px] rounded-full bg-[#febc2e]" />
              <span className="w-[11px] h-[11px] rounded-full bg-[#28c840]" />
              <span className="ml-2 font-[var(--font-mono)] text-xs text-[var(--color-text-dim)]">
                {platformBadge[activePlatform]}
              </span>
            </div>
            <img
              src={platformImages[activePlatform]}
              alt={`NanoAgent — ${activePlatform}`}
              className="w-full"
              loading="lazy"
            />
          </div>
          <div
            className="absolute inset-[-30px_-20px] z-0 bg-[radial-gradient(closest-side,rgba(124,140,255,0.25),transparent_70%)] blur-[40px] pointer-events-none"
            aria-hidden="true"
          />
        </div>
      </section>

      {/* ---- WORKS WHERE YOU WORK ---- */}
      <section className="workstrip" aria-labelledby="workstrip-title">
        <div className="workstrip__inner w-full max-w-[1160px] mx-auto px-6">
          <p className="workstrip__eyebrow" id="workstrip-title">Works Where You Work</p>
          <ul className="workstrip__list" aria-label="NanoAgent surfaces">
            <li className="workstrip__item" aria-current={activePlatform === "vscode" ? "true" : undefined}>
              <button
                className="workstrip__btn"
                onClick={() => setActivePlatform("vscode")}
                aria-pressed={activePlatform === "vscode"}
              >
                <span className="workstrip__icon" aria-hidden="true">
                  <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M9 18 3 12l6-6" /><path d="m15 6 6 6-6 6" /><path d="m14 4-4 16" />
                  </svg>
                </span>
                <span>VS Code</span>
              </button>
            </li>
            <li className="workstrip__item" aria-current={activePlatform === "vs" ? "true" : undefined}>
              <button
                className="workstrip__btn"
                onClick={() => setActivePlatform("vs")}
                aria-pressed={activePlatform === "vs"}
              >
                <span className="workstrip__icon" aria-hidden="true">
                  <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                    <rect x="3" y="4" width="18" height="12" rx="1.8" /><path d="M8 20h8" /><path d="M12 16v4" />
                  </svg>
                </span>
                <span>Visual Studio</span>
              </button>
            </li>
            <li className="workstrip__item" aria-current={activePlatform === "terminal" ? "true" : undefined}>
              <button
                className="workstrip__btn"
                onClick={() => setActivePlatform("terminal")}
                aria-pressed={activePlatform === "terminal"}
              >
                <span className="workstrip__icon" aria-hidden="true">
                  <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M4 12h8" /><path d="m9 7 5 5-5 5" /><path d="M16 19h4" />
                  </svg>
                </span>
                <span>Terminal</span>
              </button>
            </li>
            <li className="workstrip__item" aria-current={activePlatform === "desktop" ? "true" : undefined}>
              <button
                className="workstrip__btn"
                onClick={() => setActivePlatform("desktop")}
                aria-pressed={activePlatform === "desktop"}
              >
                <span className="workstrip__icon" aria-hidden="true">
                  <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                    <rect x="4" y="4" width="16" height="11" rx="1.8" /><path d="M2 20h20" /><path d="M9 15h6" />
                  </svg>
                </span>
                <span>Desktop</span>
              </button>
            </li>
            <li className="workstrip__item" aria-current={activePlatform === "cicd" ? "true" : undefined}>
              <button
                className="workstrip__btn"
                onClick={() => setActivePlatform("cicd")}
                aria-pressed={activePlatform === "cicd"}
              >
                <span className="workstrip__icon" aria-hidden="true">
                  <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                    <rect x="4" y="4" width="6" height="6" rx="1.5" /><rect x="14" y="14" width="6" height="6" rx="1.5" /><path d="M10 7h4v10" />
                  </svg>
                </span>
                <span>CI/CD Workflows</span>
              </button>
            </li>
          </ul>
        </div>
      </section>

      {/* ---- WHY NANOAGENT ---- */}
      <section className="pt-24 pb-0 reveal-target" id="why">
        <Container>
          <SectionHeader
            eyebrow="Why NanoAgent"
            title="Power-user features, nothing hidden"
            description="Built for practical engineering work: real symbols, version-controlled memory, scriptable runs, and a sandbox for anything sensitive."
          />
          <FeatureList items={whyFeatures} />
          <div className="flex justify-center mt-7">
            <a
              href="/features"
              className="inline-flex items-center justify-center gap-2 font-semibold leading-none rounded-full border cursor-pointer transition-all duration-200 whitespace-nowrap text-[#06121a] bg-[var(--color-acc-2)] shadow-[0_8px_30px_-10px_rgba(124,140,255,0.6)] hover:bg-[#93a0ff] hover:-translate-y-0.5 hover:shadow-[0_14px_40px_-10px_rgba(124,140,255,0.7)] text-[15px] px-6 py-[14px]"
            >
              See all features
            </a>
          </div>
        </Container>
      </section>

      {/* ---- PRECISION GRID ---- */}
      <PrecisionGrid />

      {/* ---- COMPARISON ---- */}
      <section className="pt-24 pb-0 reveal-target" id="compare">
        <Container>
          <SectionHeader
            eyebrow="Comparison"
            title="How NanoAgent compares"
            description="See what makes NanoAgent different from other AI coding tools."
          />
          <ComparisonTable />
        </Container>
      </section>

      {/* ---- PROVIDERS ---- */}
      <section className="pt-24 pb-0 reveal-target" id="providers">
        <Container>
          <SectionHeader
            eyebrow="Provider choice"
            title="Use the model that fits your budget &amp; policy"
            description="From subscription sign-in to API-key providers to fully local models — NanoAgent adapts to what you already pay for."
          />
          <ProvidersList />
        </Container>
      </section>

      {/* ---- PRIVACY SHOWCASE ---- */}
      <PrivacyShowcase />


      {/* ---- GATEWAY HIGHLIGHT ---- */}
      <GatewayHighlight />

      {/* ---- NANOFORGE HIGHLIGHT ---- */}
      <NanoForgeHighlight />

      {/* ---- NUGET CARD ---- */}
      <NuGetCard />

    {/* ---- CTA ---- */}
      <section className="pt-24 pb-0 reveal-target">
        <Container>
          <CTA
            badge={
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 640" width="24" height="24" fill="white" aria-hidden="true">
                <path d="M280.5 426.5C214.5 418.5 168 371 168 309.5C168 284.5 177 257.5 192 239.5C185.5 223 186.5 188 194 173.5C214 171 241 181.5 257 196C276 190 296 187 320.5 187C345 187 365 190 383 195.5C398.5 181.5 426 171 446 173.5C453 187 454 222 447.5 239C463.5 258 472 283.5 472 309.5C472 371 425.5 417.5 358.5 426C375.5 437 387 461 387 488.5L387 540.5C387 555.5 399.5 564 414.5 558C505 523.5 576 433 576 321C576 179.5 461 64 319.5 64C178 64 64 179.5 64 321C64 432 134.5 524 229.5 558.5C243 563.5 256 554.5 256 541L256 501C249 504 240 506 232 506C199 506 179.5 488 165.5 454.5C160 441 154 433 142.5 431.5C136.5 431 134.5 428.5 134.5 425.5C134.5 419.5 144.5 415 154.5 415C169 415 181.5 424 194.5 442.5C204.5 457 215 463.5 227.5 463.5C240 463.5 248 459 259.5 447.5C268 439 274.5 431.5 280.5 426.5z" />
              </svg>
            }
            title="Proudly Open Source."
            description="Released under the permissive Apache-2.0 License. Contribute, fork, and build upon NanoAgent without restrictions."
            primaryLabel="View on GitHub"
            primaryHref={siteConfig.github}
            secondaryLabel="Apache-2.0 License"
            secondaryHref={`${siteConfig.github}/blob/master/LICENSE.txt`}
            whitePrimary
          />
        </Container>
      </section>

      <div className="pb-16" />
    </>
  );
}
