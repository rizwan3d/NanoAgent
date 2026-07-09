"use client";

import GatewayHero from "@/components/features/GatewayHero";
import GatewayAudienceCards from "@/components/features/GatewayAudienceCards";
import GatewayStats from "@/components/features/GatewayStats";
import GatewayFeatureCards from "@/components/features/GatewayFeatureCards";
import GatewayPricing from "@/components/features/GatewayPricing";
import Container from "@/components/ui/Container";

export default function GatewayPage() {
  return (
    <>
      <GatewayHero />

      <GatewayAudienceCards />

      <GatewayStats />

      <GatewayFeatureCards />

      <GatewayPricing />

      {/* CTA Section */}
      <section className="pb-0 mt-24">
        <Container>
          <div className="relative overflow-hidden text-center border border-[rgba(255,255,255,0.08)] bg-gradient-to-b from-[rgba(8,10,15,0.98)] to-[rgba(5,7,10,0.98)] px-6 py-[72px] pb-[78px]">
            <div
              className="w-14 h-14 mx-auto mb-6 flex items-center justify-center bg-[rgba(110,231,255,0.1)] rounded-2xl"
              aria-hidden="true"
            >
              <img src="/assets/logo.png" alt="" width="28" height="28" />
            </div>
            <h2 className="text-[clamp(28px,3.6vw,42px)] tracking-[-0.02em] m-0 mb-[10px]">
              Bring your AI traffic under enterprise control.
            </h2>
            <p className="text-[var(--color-text-mut)] text-[16.5px] m-0 mb-6">
              Bring visibility to every model call without changing your tools.
            </p>
            <div className="flex gap-[14px] justify-center flex-wrap">
              <a
                className="inline-flex items-center justify-center gap-2 font-semibold leading-none rounded-full cursor-pointer transition-all duration-200 whitespace-nowrap text-[15px] px-6 py-[14px] text-[#06121a] bg-[var(--color-acc-2)] shadow-[0_8px_30px_-10px_rgba(124,140,255,0.6)] hover:bg-[#93a0ff] hover:-translate-y-0.5 hover:shadow-[0_14px_40px_-10px_rgba(124,140,255,0.7)]"
                href="mailto:abdullah@alfain.tech?subject=NanoAgent%20Gateway%20-%20Enterprise%20enquiry"
              >
                Talk to sales
              </a>
              <a
                className="inline-flex items-center justify-center gap-2 font-semibold leading-none rounded-full cursor-pointer transition-all duration-200 whitespace-nowrap text-[15px] px-6 py-[14px] text-[var(--color-text)] bg-[rgba(255,255,255,0.04)] border-[var(--color-border-2)] border hover:bg-[rgba(255,255,255,0.08)] hover:-translate-y-0.5"
                href="https://app.getnanoai.com"
                target="_blank"
                rel="noopener noreferrer"
              >
                Book a demo
              </a>
            </div>
          </div>
        </Container>
      </section>

      <div className="pb-16" />
    </>
  );
}
