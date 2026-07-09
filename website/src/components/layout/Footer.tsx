"use client";

import Link from "next/link";
import Brand from "@/components/ui/Brand";
import { footerColumns, siteConfig } from "@/lib/data";

export default function Footer() {
  return (
    <footer className="mt-[110px] border-t border-[var(--color-border)] bg-[var(--color-bg-2)]">
      <div className="w-full max-w-[1160px] mx-auto px-6 grid grid-cols-[1.4fr_2fr] max-md:grid-cols-1 gap-10 pt-14 pb-10">
        <div className="footer__brand">
          <Brand size="sm" />
          <p className="text-[var(--color-text-mut)] text-[14px] max-w-[280px] mt-[14px]">
            Your AI coding agent for desktop, terminal, and editor workflows.
          </p>
        </div>
        <nav className="grid grid-cols-3 max-md:grid-cols-2 gap-6" aria-label="Footer">
          {footerColumns.map((col) => (
            <div key={col.title}>
              <h4 className="text-[13px] uppercase tracking-[0.06em] text-[var(--color-text-dim)] m-0 mb-[14px]">
                {col.title}
              </h4>
              {col.links.map((link) => {
                const isExternal = link.href.startsWith("http");
                if (isExternal) {
                  return (
                    <a
                      key={link.label}
                      href={link.href}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="block text-[var(--color-text-mut)] text-[14px] py-[5px] transition-colors duration-150 hover:text-[var(--color-text)]"
                    >
                      {link.label}
                    </a>
                  );
                }
                return (
                  <Link
                    key={link.label}
                    href={link.href}
                    className="block text-[var(--color-text-mut)] text-[14px] py-[5px] transition-colors duration-150 hover:text-[var(--color-text)]"
                  >
                    {link.label}
                  </Link>
                );
              })}
            </div>
          ))}
        </nav>
      </div>
      <div className="w-full max-w-[1160px] mx-auto px-6 flex justify-between flex-wrap gap-[10px] pt-[22px] pb-8 border-t border-[var(--color-border)] text-[13px] text-[var(--color-text-dim)]">
        <span>© {new Date().getFullYear()} NanoAgent · Apache-2.0 · Built with ❤ for the open-source community.</span>
        <span className="footer__sponsor">
          Sponsored by{" "}
          <a href={siteConfig.sponsor} target="_blank" rel="noopener noreferrer" className="text-[var(--color-text-mut)] hover:text-[var(--color-acc-1)]">
            {siteConfig.sponsorName}
          </a>
        </span>
      </div>
    </footer>
  );
}
