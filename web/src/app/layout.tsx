import type { Metadata } from "next";
import { Suspense } from "react";
import { AppShell } from "@/components/AppShell";
import { MeProvider } from "@/lib/me";
import "./globals.css";

export const metadata: Metadata = {
  title: "업무 보드",
  description: "누가, 어떤 프로젝트에서, 무엇을, 어디까지 했는지 한 화면에서",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="ko">
      <body className="antialiased">
        <Suspense fallback={null}>
          <MeProvider>
            <AppShell>{children}</AppShell>
          </MeProvider>
        </Suspense>
      </body>
    </html>
  );
}
