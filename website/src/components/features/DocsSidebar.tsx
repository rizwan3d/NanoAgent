"use client";

import { useCallback, useEffect, useState } from "react";
import { docsSidebar, docSections } from "@/lib/data";

export default function DocsSidebar() {
  const [activeId, setActiveId] = useState("install");

  useEffect(() => {
    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            setActiveId(entry.target.id);
          }
        });
      },
      { rootMargin: "-90px 0px -68% 0px", threshold: 0 }
    );

    docSections.forEach(({ id }) => {
      const el = document.getElementById(id);
      if (el) observer.observe(el);
    });

    return () => observer.disconnect();
  }, []);

  const handleClick = useCallback((e: React.MouseEvent<HTMLAnchorElement>, href: string) => {
    e.preventDefault();
    const id = href.replace("#", "");
    const el = document.getElementById(id);
    if (el) {
      el.scrollIntoView({ behavior: "smooth" });
    }
  }, []);

  return (
    <aside
      className="sticky top-[92px] self-start max-h-[calc(100vh-112px)] overflow-y-auto pr-2 scrollbar-thin max-md:static max-md:max-h-none max-md:overflow-visible max-md:border max-md:border-[var(--color-border)] max-md:rounded-[var(--radius)] max-md:bg-gradient-to-b max-md:from-[var(--color-surface)] max-md:to-[var(--color-bg-2)] max-md:p-[18px_20px] max-md:mb-7"
      aria-label="Documentation contents"
    >
      {docsSidebar.map((section) => (
        <div key={section.title}>
          <p className="text-[11.5px] font-bold tracking-[0.08em] uppercase text-[var(--color-text-dim)] mt-[22px] mb-2 first:mt-0">
            {section.title}
          </p>
          <ul className="list-none m-0 p-0 grid gap-[1px] border-l border-[var(--color-border)]">
            {section.items.map((item) => (
              <li key={item.label}>
                <a
                  href={item.href}
                  onClick={(e) => handleClick(e, item.href)}
                  className={`block text-[13.5px] py-[6px] pl-[14px] border-l-2 -ml-px transition-all duration-150 ${
                    activeId === item.href.replace("#", "")
                      ? "text-[var(--color-acc-1)] border-l-[var(--color-acc-1)] font-semibold"
                      : "text-[var(--color-text-mut)] border-l-transparent hover:text-[var(--color-text)]"
                  }`}
                >
                  {item.label}
                </a>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </aside>
  );
}
