"use client";

import Link from "next/link";

interface BrandProps {
  size?: "sm" | "md";
}

export default function Brand({ size = "md" }: BrandProps) {
  const imgSize = size === "sm" ? 26 : 28;
  const fontSize = size === "sm" ? "text-[17px]" : "text-[17px]";

  return (
    <Link href="/" className="inline-flex items-center gap-[10px] font-bold tracking-tight" aria-label="NanoAgent home">
      <img
        src="/assets/logo.png"
        alt=""
        width={imgSize}
        height={imgSize}
        className="rounded-[7px]"
        style={{ width: imgSize, height: imgSize }}
      />
      <span className={`${fontSize} font-bold`}>NanoAgent</span>
    </Link>
  );
}
