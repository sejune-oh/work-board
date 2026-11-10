import type { Metadata } from "next";
import { IBM_Plex_Mono, IBM_Plex_Sans_KR } from "next/font/google";
import { Suspense } from "react";
import { AppShell } from "@/components/AppShell";
import { THEME_INIT_SCRIPT } from "@/lib/theme";
import { I18nProvider } from "@/lib/i18n";
import { ko } from "@/lib/i18n/ko";
import { LANG_INIT_SCRIPT } from "@/lib/i18n/lang";
import { MeProvider } from "@/lib/me";
import "./globals.css";

// Korean glyphs come from the unicode-range slices Google serves; `subsets` only picks what is preloaded.
const plexKr = IBM_Plex_Sans_KR({ subsets: ["latin"], weight: ["400", "500", "600", "700"], variable: "--font-plex-kr", display: "swap" });
const plexMono = IBM_Plex_Mono({ subsets: ["latin"], weight: ["400", "500", "600"], variable: "--font-plex-mono", display: "swap" });

export const metadata: Metadata = {
  title: ko.app.title,
  description: ko.app.description,
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="ko" className={`${plexKr.variable} ${plexMono.variable}`} suppressHydrationWarning>
      <head suppressHydrationWarning>
        <script dangerouslySetInnerHTML={{ __html: `${THEME_INIT_SCRIPT};${LANG_INIT_SCRIPT}` }} />
      </head>
      <body>
        <Suspense fallback={null}>
          <I18nProvider>
            <MeProvider>
              <AppShell>{children}</AppShell>
            </MeProvider>
          </I18nProvider>
        </Suspense>
      </body>
    </html>
  );
}
