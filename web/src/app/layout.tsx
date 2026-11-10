import type { Metadata } from "next";
import { Hahmlet, IBM_Plex_Mono, IBM_Plex_Sans_KR } from "next/font/google";
import { Suspense } from "react";
import { AppShell } from "@/components/AppShell";
import { THEME_INIT_SCRIPT } from "@/lib/theme";
import { MeProvider } from "@/lib/me";
import "./globals.css";

// Korean glyphs come from the unicode-range slices Google serves; `subsets` only picks what is preloaded.
const hahmlet = Hahmlet({ subsets: ["latin"], weight: ["500", "600", "700"], variable: "--font-hahmlet", display: "swap" });
const plexKr = IBM_Plex_Sans_KR({ subsets: ["latin"], weight: ["400", "500", "600"], variable: "--font-plex-kr", display: "swap" });
const plexMono = IBM_Plex_Mono({ subsets: ["latin"], weight: ["400", "500"], variable: "--font-plex-mono", display: "swap" });

export const metadata: Metadata = {
  title: "업무 보드",
  description: "누가, 어느 프로젝트에서, 무엇을, 어디까지 했는지 한 화면에서",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="ko" className={`${hahmlet.variable} ${plexKr.variable} ${plexMono.variable}`} suppressHydrationWarning>
      <head>
        <script dangerouslySetInnerHTML={{ __html: THEME_INIT_SCRIPT }} />
      </head>
      <body>
        <Suspense fallback={null}>
          <MeProvider>
            <AppShell>{children}</AppShell>
          </MeProvider>
        </Suspense>
      </body>
    </html>
  );
}
