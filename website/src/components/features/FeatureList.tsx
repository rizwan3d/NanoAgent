"use client";

import { FeatureItem } from "@/lib/data";

interface FeatureListProps {
  items: FeatureItem[];
}

export default function FeatureList({ items }: FeatureListProps) {
  return (
    <ul className="list-none m-0 p-0">
      {items.map((item) => (
        <li
          key={item.title}
          className="flex gap-3 items-start px-1 py-[14px] border-t border-[var(--color-border)] last:border-b transition-colors duration-200 hover:bg-[rgba(124,140,255,0.04)]"
        >
          <div className="flex-none w-[26px] h-[26] text-base border-none bg-none">
            {item.icon}
          </div>
          <div className="feature-list__text">
            <h3 className="m-0 mb-[2px] text-[14.5px] tracking-[-0.01em]">{item.title}</h3>
            <p className="m-0 text-[13px] text-[var(--color-text-mut)] leading-[1.45]">{item.description}</p>
          </div>
        </li>
      ))}
    </ul>
  );
}
