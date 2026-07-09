"use client";

import { ReactNode } from "react";
import Nav from "./Nav";
import Footer from "./Footer";
import BgEffects from "@/components/ui/BgEffects";

interface LayoutProps {
  children: ReactNode;
}

export default function Layout({ children }: LayoutProps) {
  return (
    <>
      <BgEffects />
      <Nav />
      <main>{children}</main>
      <Footer />
    </>
  );
}
