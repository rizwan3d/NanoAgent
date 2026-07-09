"use client";

import Container from "@/components/ui/Container";
import { siteConfig } from "@/lib/data";

export default function NuGetCard() {
  return (
    <section className="pt-24 pb-0" id="sdk">
      <Container>
        <div
          className="grid grid-cols-[auto_minmax(0,1fr)] gap-7 items-start p-12 max-md:grid-cols-1 max-md:p-9 max-sm:p-7
                     border border-[rgba(255,255,255,0.08)] rounded-[22px]
                     bg-[radial-gradient(circle_at_top_right,rgba(33,191,234,0.08),transparent_38%),linear-gradient(135deg,rgba(10,11,16,0.98),rgba(5,7,12,0.98))]
                     shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]"
        >
          {/* Package icon */}
          <div
            className="w-16 h-16 grid place-items-center rounded-[18px] text-[#19d3ff] bg-[rgba(21,194,240,0.12)] max-md:row-start-1"
            aria-hidden="true"
          >
            <svg viewBox="0 0 24 24" width="30" height="30" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
              <path d="M12 3.5 18.8 7v10L12 20.5 5.2 17V7L12 3.5Z" />
              <path d="M5.2 7 12 11l6.8-4" />
              <path d="M12 11v9.5" />
            </svg>
          </div>

          {/* Body */}
          <div className="min-w-0">
            <h2 className="text-[clamp(30px,4vw,34px)] leading-[1.05] tracking-[-0.03em] font-extrabold m-0">
              NuGet library
            </h2>
            <p className="max-w-[760px] mt-[14px] text-[16px] text-[var(--color-text-mut)] leading-[1.7]">
              The core NanoAgent package also ships on NuGet. Tagged releases publish the
              NanoAgent library to NuGet.org alongside the desktop and CLI assets. The end-user command-line experience is
              still distributed through the release installers.
            </p>

            {/* Actions */}
            <div className="flex flex-wrap gap-[14px] mt-[26px]">
              <a
                href={siteConfig.nuget}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center justify-center gap-2 font-semibold leading-none rounded-[10px] border border-transparent cursor-pointer transition-all duration-200 whitespace-nowrap min-h-[48px] px-[22px] text-[15px] text-[#071019] bg-[#22c7ea] shadow-[0_14px_40px_-18px_rgba(34,199,234,0.75)] hover:bg-[#3ad0ee] hover:shadow-[0_18px_46px_-18px_rgba(34,199,234,0.8)] hover:-translate-y-0.5"
              >
                <span className="inline-flex items-center justify-center" aria-hidden="true">
                  <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M12 4.5 17.5 7.5v7L12 17.5l-5.5-3v-7L12 4.5Z" />
                    <path d="M6.5 7.5 12 10.5l5.5-3" />
                    <path d="M12 10.5v7" />
                  </svg>
                </span>
                View on NuGet
                <span className="inline-flex items-center justify-center" aria-hidden="true">
                  <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M14 5h5v5" />
                    <path d="M10 14 19 5" />
                    <path d="M19 14v4a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1h4" />
                  </svg>
                </span>
              </a>
              <a
                href="/docs"
                className="inline-flex items-center justify-center gap-2 font-semibold leading-none rounded-[10px] border cursor-pointer transition-all duration-200 whitespace-nowrap min-h-[48px] px-[22px] text-[15px] text-[var(--color-text)] bg-[rgba(255,255,255,0.02)] border-[rgba(255,255,255,0.12)] hover:bg-[rgba(255,255,255,0.08)] hover:-translate-y-0.5"
              >
                <span className="inline-flex items-center justify-center" aria-hidden="true">
                  <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M4 6.5A2.5 2.5 0 0 1 6.5 4H19v14.5a1.5 1.5 0 0 0-1.5-1.5H6.5A2.5 2.5 0 0 0 4 19.5Z" />
                    <path d="M19 4v14.5" />
                    <path d="M8 8h7" />
                  </svg>
                </span>
                See install docs
              </a>
            </div>

            {/* Command */}
            <div className="mt-6 px-5 py-[15px] border border-[rgba(255,255,255,0.07)] rounded-xl bg-[rgba(3,4,8,0.7)] overflow-x-auto">
              <code className="block font-[var(--font-mono)] text-[15px] text-[#b8becd] whitespace-nowrap">
                dotnet add package <span className="text-[#19d3ff] font-semibold">NanoAgent</span>
              </code>
            </div>
          </div>
        </div>
      </Container>
    </section>
  );
}
