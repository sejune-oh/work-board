"use client";

import { useEffect, useState } from "react";
import { useT } from "@/lib/i18n";
import { THEME_KEY } from "@/lib/theme";

type Theme = "light" | "dark";

function currentTheme(): Theme {
  const pinned = document.documentElement.dataset.theme;
  if (pinned === "light" || pinned === "dark") return pinned;
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

/** Two-way switch that pins a theme and remembers it; until one is picked, the OS setting wins. */
export function ThemeToggle() {
  const t = useT();
  const [theme, setTheme] = useState<Theme | null>(null);

  useEffect(() => {
    setTheme(currentTheme());
    const mq = window.matchMedia("(prefers-color-scheme: dark)");
    const onChange = () => setTheme(currentTheme());
    mq.addEventListener("change", onChange);
    return () => mq.removeEventListener("change", onChange);
  }, []);

  const pick = (next: Theme) => {
    document.documentElement.dataset.theme = next;
    setTheme(next);
    try {
      window.localStorage.setItem(THEME_KEY, next);
    } catch {
      // Without storage the choice lasts until reload.
    }
  };

  const options: { value: Theme; icon: React.ReactNode; label: string }[] = [
    {
      value: "light",
      label: t.theme.light,
      icon: (
        <>
          <circle cx="8" cy="8" r="3" />
          <path d="M8 1.5v1.5M8 13v1.5M1.5 8H3M13 8h1.5M3.4 3.4l1 1M11.6 11.6l1 1M3.4 12.6l1-1M11.6 4.4l1-1" />
        </>
      ),
    },
    { value: "dark", label: t.theme.dark, icon: <path d="M13.5 9.5A5.5 5.5 0 0 1 6.5 2.5a5.5 5.5 0 1 0 7 7Z" /> },
  ];

  return (
    <div role="group" aria-label={t.theme.group} className="seg">
      {options.map((o) => (
        <button key={o.value} type="button" aria-pressed={theme === o.value} aria-label={o.label} title={o.label} onClick={() => pick(o.value)}>
          <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden>
            {o.icon}
          </svg>
        </button>
      ))}
    </div>
  );
}
