"use client";

import { gatewayPricingPlans } from "@/lib/data";
import Container from "@/components/ui/Container";
import Button from "@/components/ui/Button";

function PricingCheck({ included }: { included: boolean }) {
  return (
    <span
      className={`w-[18px] h-[18px] inline-flex items-center justify-center flex-none rounded-full text-[11px] font-bold ${
        included
          ? "bg-[rgba(110,231,255,0.15)] text-[var(--color-acc-1)]"
          : "bg-white/5 text-[var(--color-text-dim)]"
      }`}
    >
      {included ? "✓" : "–"}
    </span>
  );
}

export default function GatewayPricing() {
  return (
    <section className="section featcat pt-24 pb-0" id="pricing">
      <Container>
        <div className="flex items-center gap-[14px] mb-[46px]">
          <div className="w-[46px] h-[46px] flex-none grid place-items-center text-[22px] rounded-xl bg-[rgba(124,140,255,0.1)] border border-[var(--color-border)]">💵</div>
          <div>
            <h2 className="text-[var(--color-acc-1)]">Pricing</h2>
            <p>
              Start free, then grow into broader operational visibility and
              control as adoption expands.
            </p>
          </div>
        </div>

        <div className="flex gap-4 text-center items-center mb-8">
          <p className="m-0 text-[var(--color-text)] font-bold text-[15px]">
            Compare every plan at a glance with full feature checklists inside each card:
          </p>
          <span className="text-[var(--color-text-dim)] text-sm">
            Included features are highlighted and unavailable ones stay visible
            but muted.
          </span>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-[18px]">
          {gatewayPricingPlans.map((plan) => (
            <div
              key={plan.name}
              className={`relative flex flex-col border bg-gradient-to-b from-[var(--color-surface)] to-[var(--color-bg-2)] p-8 max-md:p-[32px_24px_28px] transition-all duration-200 hover:-translate-y-1 hover:border-[var(--color-border-2)] ${
                plan.featured
                  ? "border-[var(--color-acc-1)] border-2 shadow-[0_0_30px_rgba(110,231,255,0.15)]"
                  : "border-[var(--color-border)]"
              }`}
            >
              {plan.featured && (
                <span className="absolute top-[-12px] left-1/2 -translate-x-1/2 px-[14px] py-1 bg-[var(--color-acc-1)] text-[#041017] text-[11px] font-extrabold tracking-[0.06em] uppercase whitespace-nowrap">
                  Most Popular
                </span>
              )}

              {/* ── Plan name + description, same line ── */}
              <div className="flex items-baseline gap-2 flex-wrap mb-4">
                <h3 className="m-0 text-[20px] tracking-[-0.02em] whitespace-nowrap">
                  {plan.name}
                </h3>
                <p className="m-0 text-[var(--color-text-mut)] text-sm leading-normal">
                  {plan.description}
                </p>
              </div>

              {/* ── Price ── */}
              <p className="m-0 text-[36px] font-extrabold tracking-[-0.03em] leading-none">
                {plan.monthlyPrice}
                <span className="text-sm font-medium text-[var(--color-text-mut)]">/mo</span>
              </p>
              {plan.annualPrice !== plan.monthlyPrice && (
                <p className="text-[var(--color-text-dim)] text-[13px] mt-1 mb-0">
                  {plan.annualPrice}/mo billed annually
                </p>
              )}

              {/* ── Feature checklist ── */}
              <ul className="list-none m-0 p-0 flex flex-col gap-2.5 flex-1 mt-6">
                {plan.features.map((feature) => (
                  <li
                    key={feature.name}
                    className={`flex items-center gap-2.5 text-[13.5px] ${
                      feature.included
                        ? "text-[var(--color-text)]"
                        : "text-[var(--color-text-mut)] opacity-60"
                    }`}
                  >
                    <PricingCheck included={feature.included} />
                    <span>{feature.name}</span>
                  </li>
                ))}
              </ul>

              {/* ── CTA ── */}
              <div className="mt-6">
                <Button
                  variant={plan.featured ? "primary" : "ghost"}
                  href={plan.ctaHref}
                  className="w-full justify-center"
                >
                  {plan.ctaLabel}
                </Button>
              </div>
            </div>
          ))}
        </div>
      </Container>
    </section>
  );
}
