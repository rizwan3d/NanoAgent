"use client";

import { ReactNode } from "react";
import Button from "@/components/ui/Button";

interface CTAProps {
  badge?: ReactNode;
  title?: string;
  description?: string;
  primaryLabel?: string;
  primaryHref?: string;
  secondaryLabel?: string;
  secondaryHref?: string;
  /** When true, the primary button uses a light background (white) instead of the accent color */
  whitePrimary?: boolean;
}

export default function CTA({
  badge,
  title = "Ship with an agent that keeps you in control.",
  description = "Local-first. Reviewable. Open source under Apache-2.0.",
  primaryLabel = "Get NanoAgent",
  primaryHref = "/#get",
  secondaryLabel = "Star on GitHub",
  secondaryHref = "https://github.com/rizwan3d/NanoAgent",
  whitePrimary = false,
}: CTAProps) {
  return (
    <div
      className="relative overflow-hidden text-center
                 border border-[rgba(255,255,255,0.08)] rounded-3xl
                 bg-gradient-to-b from-[rgba(8,10,15,0.98)] to-[rgba(5,7,10,0.98)]
                 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]
                 px-8 py-[82px] max-md:px-5 max-md:py-16"
    >
      {/* Background glow */}
      <div
        className="absolute left-1/2 -translate-x-1/2 bottom-[-140px] w-[760px] h-[360px]
                   bg-[radial-gradient(closest-side,rgba(124,140,255,0.18),transparent_72%)]
                   blur-[44px] pointer-events-none"
        aria-hidden="true"
      />

      {/* Badge */}
      {badge && (
        <div className="relative z-[1] w-[60px] h-[60px] mx-auto mb-[34px] grid place-items-center border border-[rgba(255,255,255,0.08)] bg-[rgba(255,255,255,0.02)] shadow-[0_0_0_12px_rgba(255,255,255,0.01)]">
          {badge}
        </div>
      )}

      {/* Title */}
      <h2 className={`relative z-[1] text-[clamp(36px,5vw,58px)] font-bold leading-[1.02] tracking-[-0.05em] m-0 mx-auto mb-4 text-[#f4f7fb] [text-wrap:balance] ${badge ? "" : ""}`}>
        {title}
      </h2>

      {/* Description */}
      <p className="relative z-[1] max-w-[760px] mx-auto mb-[34px] text-[#a0a7b7] text-[17px] leading-[1.6]">
        {description}
      </p>

      {/* Actions */}
      <div className="relative z-[1] flex gap-[14px] justify-center flex-wrap">
        <Button
          variant={whitePrimary ? "primary" : "primary"}
          size="lg"
          href={primaryHref}
          className={whitePrimary ? "!text-[#05070b] !bg-[#f3f5f8] !shadow-none hover:!bg-white hover:!shadow-none !border-transparent" : ""}
        >
          {primaryLabel}
        </Button>
        <Button variant="ghost" size="lg" href={secondaryHref}>
          {secondaryLabel}
        </Button>
      </div>
    </div>
  );
}
