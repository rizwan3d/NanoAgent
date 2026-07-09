"use client";

import { quickstartSteps } from "@/lib/data";
import CodeBlock from "@/components/ui/CodeBlock";

export default function Quickstart() {
  return (
    <div className="grid gap-5 max-w-[860px] mx-auto">
      {quickstartSteps.map((step) => (
        <div key={step.num} className="grid grid-cols-[auto_1fr] gap-5 items-start max-md:grid-cols-1">
          <div className="w-[38px] h-[38px] shrink-0 grid place-items-center font-extrabold text-[16px] text-[#06121a] bg-[var(--color-acc-2)] rounded-xl max-md:hidden">
            {step.num}
          </div>
          <div className="min-w-0">
            <h3 className="m-0 mb-[14px] mt-1 text-[19px]">{step.title}</h3>
            {step.note && <p className="m-0 mb-3 text-[14.5px] text-[var(--color-text-mut)]">{step.note}</p>}
            <CodeBlock tabs={step.tabs} />
          </div>
        </div>
      ))}
    </div>
  );
}
