"use client";

import { ReactNode } from "react";
import Link from "next/link";

interface ButtonProps {
  children: ReactNode;
  variant?: "primary" | "ghost";
  size?: "md" | "lg";
  href?: string;
  className?: string;
  type?: "button" | "submit";
  onClick?: () => void;
}

export default function Button({
  children,
  variant = "primary",
  size = "md",
  href,
  className = "",
  type = "button",
  onClick,
}: ButtonProps) {
  const base =
    "inline-flex items-center justify-center gap-2 font-semibold leading-none rounded-full border border-transparent cursor-pointer transition-all duration-200 whitespace-nowrap";

  const sizeClasses = size === "lg" ? "text-[15px] px-6 py-[14px]" : "text-[14px] px-[18px] py-[11px]";

  const variantClasses =
    variant === "primary"
      ? "text-[#06121a] bg-[var(--color-acc-2)] shadow-[0_8px_30px_-10px_rgba(124,140,255,0.6)] hover:bg-[#93a0ff] hover:-translate-y-0.5 hover:shadow-[0_14px_40px_-10px_rgba(124,140,255,0.7)]"
      : "text-[var(--color-text)] bg-[rgba(255,255,255,0.04)] border-[var(--color-border-2)] hover:bg-[rgba(255,255,255,0.08)] hover:-translate-y-0.5";

  if (href) {
    const isExternal = href.startsWith("http");
    if (isExternal) {
      return (
        <a
          href={href}
          target="_blank"
          rel="noopener noreferrer"
          className={`${base} ${sizeClasses} ${variantClasses} ${className}`}
        >
          {children}
        </a>
      );
    }
    return (
      <Link href={href} className={`${base} ${sizeClasses} ${variantClasses} ${className}`}>
        {children}
      </Link>
    );
  }

  return (
    <button type={type} onClick={onClick} className={`${base} ${sizeClasses} ${variantClasses} ${className}`}>
      {children}
    </button>
  );
}
