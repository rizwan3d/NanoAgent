"use client";

import { useState, useEffect, useCallback } from "react";
import Link from "next/link";
import Brand from "@/components/ui/Brand";
import { siteConfig } from "@/lib/data";

export default function Nav() {
  const [scrolled, setScrolled] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 8);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

  const closeMenu = useCallback(() => {
    setMenuOpen(false);
    document.getElementById("burger")?.setAttribute("aria-expanded", "false");
  }, []);

  const toggleMenu = useCallback(() => {
    setMenuOpen((prev) => {
      const next = !prev;
      document.getElementById("burger")?.setAttribute("aria-expanded", String(next));
      return next;
    });
  }, []);

  const openApp = useCallback(() => {
    window.open(siteConfig.appUrl, '_blank');
  }, []);

  return (
    <header
      className={`sticky top-0 z-50 backdrop-blur-[14px] transition-all duration-250 border-b border-transparent ${
        scrolled ? "bg-[rgba(0,0,0,0.85)] border-[var(--color-border)]" : "bg-[rgba(0,0,0,0.55)]"
      }`}
      id="nav"
    >
      <div className="w-full max-w-[1160px] mx-auto px-6 flex items-center gap-7 h-[68px] relative">
        <Brand />

        {/* Desktop nav links - centered */}
        <nav
          className={`nav__links items-center gap-[26px] ${
            menuOpen
              ? "is-open flex flex-col absolute top-full right-6 left-auto w-[240px] z-[100] bg-[rgba(10,12,18,0.96)] backdrop-blur-[14px] border border-[var(--color-border-2)] rounded-[var(--radius)] p-4 gap-1 shadow-[0_10px_30px_rgba(0,0,0,0.5)]"
              : "hidden md:flex absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2"
          }`}
          aria-label="Primary"
        >
          <Link href="/" className="text-[var(--color-text-mut)] text-[14px] font-medium transition-colors duration-150 hover:text-[var(--color-text)]" onClick={closeMenu}>
            Home
          </Link>

          {/* Product — native <details> mega menu (matches getnanoai.com) */}
          <details className="mega">
            <summary>Product</summary>
            <div className="mega__panel">
              <div className="mega__grid">
                <div className="mega__col">
                  <a className="mfeat" href="https://github.com/rizwan3d/NanoAgent" target="_blank" rel="noopener noreferrer" onClick={closeMenu}>
                    <span className="mfeat__ico">⌘</span>
                    <span><strong>NanoAgent</strong><span>Open-source coding agent</span></span>
                  </a>
                  <div className="mega__links">
                    <a href="/docs#desktop" onClick={closeMenu}>Desktop</a>
                    <a href="/docs#vscode" onClick={closeMenu}>VS Code</a>
                    <a href="/docs#visual-studio" onClick={closeMenu}>Visual Studio</a>
                    <a href="/docs#review" onClick={closeMenu}>CodeReview</a>
                    <a href="/docs#review" onClick={closeMenu}>CI</a>
                  </div>
                </div>
                <div className="mega__col">
                  <a className="mfeat" href="/gateway" onClick={closeMenu}>
                    <span className="mfeat__ico">⇄</span>
                    <span><strong>Gateway</strong><span>Model routing &amp; control</span></span>
                  </a>
                  <a className="mfeat" href="/nanoforge" onClick={closeMenu}>
                    <span className="mfeat__ico">▤</span>
                    <span><strong>NanoForge</strong><span>Full-stack app builder</span></span>
                  </a>
                </div>
              </div>
            </div>
          </details>

          <Link href="/features" className="text-[var(--color-text-mut)] text-[14px] font-medium transition-colors duration-150 hover:text-[var(--color-text)]" onClick={closeMenu}>
            Features
          </Link>
          <Link href="/docs" className="text-[var(--color-text-mut)] text-[14px] font-medium transition-colors duration-150 hover:text-[var(--color-text)]" onClick={closeMenu}>
            Docs
          </Link>

          {/* Mobile auth buttons */}
          <button
            className="md:hidden inline-flex items-center justify-center font-semibold text-[13.5px] leading-none px-4 py-3 rounded-full border border-[var(--color-border)] text-[var(--color-text-mut)] bg-transparent hover:text-[var(--color-text)] transition-all duration-200 mt-2 w-full"
            onClick={() => { closeMenu(); openApp(); }}
          >
            Login
          </button>
          <button
            className="md:hidden inline-flex items-center justify-center font-semibold text-[13.5px] leading-none px-4 py-3 rounded-full border border-transparent text-[#06121a] bg-[var(--color-acc-2)] shadow-[0_4px_15px_-5px_rgba(110,231,255,0.4)] hover:-translate-y-0.5 transition-all duration-200 w-full"
            onClick={() => { closeMenu(); openApp(); }}
          >
            Register
          </button>
        </nav>

        {/* Actions */}
        <div className="flex items-center gap-3 ml-auto">
          <a
            href={siteConfig.github}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-[7px] text-[var(--color-text-mut)] text-[13px] font-semibold px-3 py-2 rounded-full border border-[var(--color-border)] transition-all duration-150 hover:text-[var(--color-text)] hover:border-[var(--color-border-2)]"
            aria-label="NanoAgent stars on GitHub"
          >
            <svg viewBox="0 0 16 16" width="16" height="16" aria-hidden="true" fill="currentColor">
              <path d="M8 .25a.75.75 0 0 1 .673.418l1.882 3.815 4.21.612a.75.75 0 0 1 .416 1.279l-3.046 2.97.719 4.192a.75.75 0 0 1-1.088.791L8 12.347l-3.766 1.98a.75.75 0 0 1-1.088-.79l.72-4.194L.818 6.374a.75.75 0 0 1 .416-1.28l4.21-.611L7.327.668A.75.75 0 0 1 8 .25Z" />
            </svg>
            <span className="gh-stars hidden md:inline">Star</span>
          </a>
          {/* <a
            href={siteConfig.commits}
            target="_blank"
            rel="noopener noreferrer"
            className="hidden lg:inline-flex items-center gap-[7px] text-[var(--color-text-mut)] text-[13px] font-semibold px-3 py-2 rounded-full border border-[var(--color-border)] transition-all duration-150 hover:text-[var(--color-text)] hover:border-[var(--color-border-2)]"
            aria-label="NanoAgent commits on GitHub"
          >
            <svg viewBox="0 0 16 16" width="16" height="16" aria-hidden="true" fill="currentColor">
              <path d="M11.93 8.5a4.002 4.002 0 0 1-7.86 0H.75a.75.75 0 0 1 0-1.5h3.32a4.002 4.002 0 0 1 7.86 0h3.32a.75.75 0 0 1 0 1.5Zm-1.43-.75a2.5 2.5 0 1 0-5 0 2.5 2.5 0 0 0 5 0Z" />
            </svg>
          </a> */}
          <button
            className="hidden md:inline-flex items-center justify-center font-semibold text-[13.5px] leading-none px-4 py-3 rounded-full border border-[var(--color-border)] text-[var(--color-text-mut)] bg-transparent hover:text-[var(--color-text)] transition-all duration-200"
            onClick={openApp}
          >
            Login
          </button>
          <button
            className="hidden md:inline-flex items-center justify-center font-semibold text-[13.5px] leading-none px-4 py-3 rounded-full border border-transparent text-[#06121a] bg-[var(--color-acc-2)] shadow-[0_4px_15px_-5px_rgba(110,231,255,0.4)] hover:-translate-y-0.5 transition-all duration-200"
            onClick={openApp}
          >
            Register
          </button>

          {/* Burger */}
          <button
            className="flex md:hidden flex-col gap-[5px] bg-none border-0 cursor-pointer p-2"
            id="burger"
            aria-label="Toggle menu"
            aria-expanded="false"
            onClick={toggleMenu}
          >
            <span className="w-[22px] h-[2px] bg-[var(--color-text)] rounded-[2px] transition-all duration-250" />
            <span className="w-[22px] h-[2px] bg-[var(--color-text)] rounded-[2px] transition-all duration-250" />
            <span className="w-[22px] h-[2px] bg-[var(--color-text)] rounded-[2px] transition-all duration-250" />
          </button>
        </div>
      </div>
    </header>
  );
}
