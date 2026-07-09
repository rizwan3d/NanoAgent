"use client";

import { ReactNode } from "react";

interface ContainerProps {
  children: ReactNode;
  className?: string;
}

export default function Container({ children, className = "" }: ContainerProps) {
  return (
    <div className={`w-full max-w-[1160px] mx-auto px-6 ${className}`}>
      {children}
    </div>
  );
}
