import type { Metadata } from "next";
import "./globals.css";
import { Space_Grotesk } from "next/font/google";
import { siteConfig } from "@/lib/data";
import ClientLayout from "./client-layout";

const spaceGrotesk = Space_Grotesk({
  subsets: ["latin"],
  display: "swap",
  variable: "--font-sans",
});

export const metadata: Metadata = {
  title: siteConfig.tagline,
  description: siteConfig.description,
  icons: { icon: "/assets/logo.png" },
  openGraph: {
    title: siteConfig.tagline,
    description: siteConfig.description,
    type: "website",
    images: [{ url: siteConfig.ogImage }],
  },
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
      <html lang="en" className={spaceGrotesk.variable}>
      <body>
        <ClientLayout>{children}</ClientLayout>
      </body>
    </html>
  );
}
