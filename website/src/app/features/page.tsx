"use client";

import { useEffect } from "react";
import Container from "@/components/ui/Container";
import CTA from "@/components/features/CTA";
import SectionNav from "@/components/features/SectionNav";
import Card from "@/components/ui/Card";
import { featureCategories } from "@/lib/data";

const navItems = featureCategories.map((cat) => ({
  id: cat.id,
  label: cat.title.split(" ")[0] === cat.title.split(" ")[0]
    ? cat.title
    : cat.title.split(" &")[0],
}));

const categoryNavItems = [
  { id: "cli", label: "CLI Experience" },
  { id: "headless", label: "Oneshot & Headless" },
  { id: "shell", label: "Shell & Terminals" },
  { id: "tools", label: "File & Web Tools" },
  { id: "acp", label: "ACP" },
  { id: "mcp", label: "MCP & Skills" },
  { id: "memory", label: "Repo Memory" },
  { id: "providers", label: "Model Providers" },
  { id: "reasoning", label: "Model & Reasoning" },
  { id: "agents", label: "Profiles & Subagents" },
  { id: "lsp", label: "LSP" },
  { id: "index", label: "Codebase Index" },
  { id: "sandbox", label: "Permissions & Sandbox" },
  { id: "editors", label: "Editors & Desktop" },
  { id: "ci", label: "CI Reviews" },
  { id: "dx", label: "Developer Experience" },
];

export default function FeaturesPage() {
  useEffect(() => {
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    if (!("IntersectionObserver" in window)) return;

    // Scrollspy for category nav
    const links = document.querySelectorAll(".featnav a");
    const map: Record<string, Element> = {};
    links.forEach((a) => {
      const href = a.getAttribute("href");
      if (href) map[href.slice(1)] = a;
    });

    const spy = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            links.forEach((l) => l.classList.remove("is-active"));
            const link = map[entry.target.id];
            if (link) {
              link.classList.add("is-active");
              const bar = document.querySelector(".featnav__inner");
              if (bar) {
                const barRect = bar.getBoundingClientRect();
                const linkRect = link.getBoundingClientRect();
                const delta =
                  linkRect.left + linkRect.width / 2 - (barRect.left + barRect.width / 2);
                bar.scrollLeft += delta;
              }
            }
          }
        });
      },
      { rootMargin: "-140px 0px -68% 0px", threshold: 0 }
    );

    document.querySelectorAll(".featcat").forEach((s) => spy.observe(s));

    return () => spy.disconnect();
  }, []);

  return (
    <>
      {/* Hero */}
      <section className="text-center max-w-[860px] mx-auto px-6 pt-14 pb-2">
        <span className="inline-block text-[13px] font-bold tracking-[0.08em] uppercase text-[var(--color-acc-1)]">
          Features
        </span>
        <h1 className="text-[clamp(30px,4.6vw,52px)] leading-[1.05] tracking-[-0.025em] font-extrabold m-0 mt-3">
          Everything NanoAgent <span className="text-[var(--color-acc-1)]">can do</span>
        </h1>
        <p className="mt-[18px] mx-auto max-w-[640px] text-[17px] text-[var(--color-text-mut)]">
          A local-first AI coding agent built for real engineering work — from the terminal to the editor
          to your CI pipeline. Every capability below is sourced straight from the README and documentation,
          grouped so you can jump to what matters.
        </p>
      </section>

      {/* Category Nav */}
      <SectionNav items={categoryNavItems} />

      {/* Feature Sections */}
      <Container>
        {featureCategories.map((cat) => (
          <section
            key={cat.id}
            id={cat.id}
            className="featcat pt-[78px] first:pt-[60px] scroll-mt-[132px]"
          >
            <div className="flex items-center gap-[14px] mb-[26px]">
              <div className="w-[46px] h-[46px] flex-none grid place-items-center text-[22px] rounded-xl bg-[rgba(124,140,255,0.1)] border border-[var(--color-border)]">
                {cat.icon}
              </div>
              <div>
                <h2 className="m-0 text-[clamp(22px,3vw,30px)] tracking-[-0.02em]">{cat.title}</h2>
                <p className="m-0 mt-[5px] text-[14.5px] text-[var(--color-text-mut)]">{cat.description}</p>
              </div>
            </div>

            <div className="grid grid-cols-3 max-md:grid-cols-2 max-sm:grid-cols-1 gap-[18px]">
              {cat.cards.map((card) => (
                <Card key={card.title} icon={card.icon} title={card.title} description={card.description} />
              ))}
            </div>
          </section>
        ))}

        {/* CTA */}
        <section className="pt-24 pb-0">
          <CTA
            primaryLabel="Get NanoAgent"
            primaryHref="/#get"
            secondaryLabel="Read the docs"
            secondaryHref="/docs"
          />
        </section>

        <div className="pb-16" />
      </Container>
    </>
  );
}
