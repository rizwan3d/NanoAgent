"use client";

import { useCallback } from "react";

interface SectionNavProps {
  items: { id: string; label: string }[];
}

export default function SectionNav({ items }: SectionNavProps) {
  const handleClick = useCallback((e: React.MouseEvent<HTMLAnchorElement>, id: string) => {
    e.preventDefault();
    const el = document.getElementById(id);
    if (el) {
      el.scrollIntoView({ behavior: "smooth" });
    }
  }, []);

  return (
    <nav
      className="sticky top-[67px] z-40 bg-[rgba(0,0,0,0.82)] backdrop-blur-[14px] border-b border-[var(--color-border)]"
      aria-label="Feature categories"
    >
      <div className="w-full max-w-[1160px] mx-auto px-6 flex gap-2 overflow-x-auto py-3 scrollbar-hide">
        {items.map((item) => (
          <a
            key={item.id}
            href={`#${item.id}`}
            onClick={(e) => handleClick(e, item.id)}
            className="flex-none text-[13px] font-semibold text-[var(--color-text-mut)] px-[14px] py-2 rounded-full border border-[var(--color-border)] bg-[var(--color-surface)] whitespace-nowrap transition-all duration-150 hover:text-[var(--color-text)] hover:border-[var(--color-border-2)]"
          >
            {item.label}
          </a>
        ))}
      </div>
    </nav>
  );
}
