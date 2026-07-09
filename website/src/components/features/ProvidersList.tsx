"use client";

import { providers } from "@/lib/data";

interface ProvidersListProps {
  items?: string[];
}

export default function ProvidersList({ items = providers }: ProvidersListProps) {
  return (
    <ul className="list-none m-0 p-0 flex flex-wrap gap-3 justify-center">
      {items.map((provider) => (
        <li
          key={provider}
          className="text-[14.5px] font-medium text-[var(--color-text-mut)] px-[18px] py-[11px] rounded-full bg-[var(--color-surface)] border border-[var(--color-border)] transition-all duration-150 hover:-translate-y-0.5 hover:border-[var(--color-border-2)] hover:text-[var(--color-text)]"
        >
          {provider}
        </li>
      ))}
    </ul>
  );
}
