"use client";

import { gatewayAudiences } from "@/lib/data";
import Container from "@/components/ui/Container";
import SectionHeader from "@/components/ui/SectionHeader";

export default function GatewayAudienceCards() {
  return (
    <section className="pt-24" id="why">
      <Container>
        <SectionHeader
          eyebrow="Why Teams Buy"
          title="Built to help enterprise teams say yes to AI adoption faster"
          description="Gateway gives each stakeholder a clear reason to move forward without slowing down developers."
        />
        <div className="grid grid-cols-1 md:grid-cols-3 gap-[18px]">
          {gatewayAudiences.map((item) => (
            <article
              key={item.label}
              className="h-full p-[28px_24px] border border-[var(--color-border)] bg-gradient-to-b from-[var(--color-surface)] to-[var(--color-bg-2)] transition-all duration-200 hover:-translate-y-1 hover:border-[var(--color-border-2)]"
            >
              <span className="inline-flex mb-[14px] px-[10px] py-[6px] border border-[rgba(110,231,255,0.22)] bg-[rgba(110,231,255,0.08)] text-[var(--color-acc-1)] text-xs font-bold tracking-[0.08em] uppercase">
                {item.label}
              </span>
              <h3 className="m-0 mb-[10px] text-xl leading-[1.2] tracking-[-0.02em]">
                {item.title}
              </h3>
              <p className="m-0 text-[14.5px] text-[var(--color-text-mut)]">
                {item.description}
              </p>
            </article>
          ))}
        </div>
      </Container>
    </section>
  );
}
