"use client";

interface SectionHeaderProps {
  eyebrow: string;
  title: string;
  description?: string;
  className?: string;
}

export default function SectionHeader({ eyebrow, title, description, className = "" }: SectionHeaderProps) {
  return (
    <header className={`max-w-[680px] mx-auto mb-[46px] text-center ${className}`}>
      <span className="inline-block text-[13px] font-bold tracking-[0.08em] uppercase text-[var(--color-acc-1)]">
        {eyebrow}
      </span>
      <h2 className="text-[clamp(26px,3.4vw,38px)] leading-[1.12] tracking-[-0.02em] font-extrabold mt-3 mx-0 mb-0">
        {title}
      </h2>
      {description && (
        <p className="mt-4 text-[16.5px] text-[var(--color-text-mut)] mx-auto max-w-[640px]">
          {description}
        </p>
      )}
    </header>
  );
}
