"use client";

import { ReactNode } from "react";

interface CardProps {
  icon?: string;
  title: string | ReactNode;
  description?: string;
  children?: ReactNode;
  className?: string;
}

export default function Card({ icon, title, description, children, className = "" }: CardProps) {
  return (
    <div
      className={`bg-gradient-to-b from-[var(--color-surface)] to-[var(--color-bg-2)] border border-[var(--color-border)] rounded-[var(--radius)] p-[26px_24px] transition-all duration-200 hover:-translate-y-1 hover:border-[var(--color-border-2)] hover:shadow-[0_20px_50px_-30px_rgba(124,140,255,0.5)] ${className}`}
    >
      {icon && (
        <div className="w-[46px] h-[46px] grid place-items-center text-[22px] rounded-xl bg-[rgba(124,140,255,0.1)] border border-[var(--color-border)] mb-4">
          {icon}
        </div>
      )}
      {typeof title === "string" ? (
        <h3 className="m-0 mb-2 text-[17px] tracking-[-0.01em]">{title}</h3>
      ) : (
        title
      )}
      {description && <p className="m-0 text-[14.5px] text-[var(--color-text-mut)]">{description}</p>}
      {children}
    </div>
  );
}
