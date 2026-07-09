"use client";

import { gatewayStats } from "@/lib/data";
import Container from "@/components/ui/Container";

export default function GatewayStats() {
  return (
    <Container>
      <div
        className="grid grid-cols-1 lg:grid-cols-3 m-0 border border-[var(--color-border)] border-t-0 bg-[var(--color-surface)]"
        aria-label="Gateway highlights"
      >
        {gatewayStats.map((stat, idx) => (
          <div
            key={stat.eyebrow}
            className={`relative min-h-[194px] p-[28px_26px_24px] ${
              idx < gatewayStats.length - 1
                ? "border-r border-r-[rgba(255,255,255,0.09)]"
                : ""
            } bg-transparent`}
          >
            {idx < gatewayStats.length - 1 && (
              <span className="absolute top-6 right-[-13px] z-10 w-[26px] h-[22px] grid place-items-center text-[#041017] text-[13px] font-extrabold bg-[var(--color-acc-1)] max-lg:hidden">
                →
              </span>
            )}
            <span className="inline-block mb-4 text-[var(--color-acc-1)] font-mono text-[13px] font-extrabold tracking-[0.14em] uppercase">
              {stat.eyebrow}
            </span>
            <strong className="block mb-[10px] text-[31px] leading-[1.05] tracking-[-0.03em] font-extrabold">
              {stat.value}
            </strong>
            <p className="m-0 max-w-[25ch] text-[var(--color-text-mut)] text-[15px] leading-normal">
              {stat.description}
            </p>
          </div>
        ))}
      </div>
    </Container>
  );
}
