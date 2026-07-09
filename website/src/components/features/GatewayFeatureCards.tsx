"use client";

import { gatewayFeatures } from "@/lib/data";
import Container from "@/components/ui/Container";
import Card from "@/components/ui/Card";

export default function GatewayFeatureCards() {
  return (
    <section className="pt-[96px] pb-0 featcat gateway-features">
      <Container>
        <div className="flex items-center gap-[14px] mb-[46px]">
          <div className="w-[46px] h-[46px] flex-none grid place-items-center text-[22px] rounded-xl bg-[rgba(124,140,255,0.1)] border border-[var(--color-border)]">★</div>
          <div>
            <h2>Why Nano Gateway?</h2>
            <p>
              One control plane between your apps and every model without
              rewriting a line of client code.
            </p>
          </div>
        </div>
        <div className="grid grid-cols-3 max-md:grid-cols-2 max-sm:grid-cols-1 gap-[18px]">
          {gatewayFeatures.map((feature) => (
            <Card
              key={feature.title}
              icon={feature.icon}
              title={feature.title}
              description={feature.description}
            />
          ))}
        </div>
      </Container>
    </section>
  );
}
