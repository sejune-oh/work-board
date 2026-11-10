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

  const options: { value: Theme; icon: string; label: string }[] = [
    { value: "light", icon: "☼", label: t.theme.light },
    { value: "dark", icon: "☾", label: t.theme.dark },
  ];

  return (
    <div role="group" aria-label={t.theme.group} className="inline-flex shrink-0 overflow-hidden rounded-control border border-rule">
      {options.map((o) => (
        <button
          key={o.value}
          type="button"
          aria-pressed={theme === o.value}
          onClick={() => pick(o.value)}
          className={`h-[26px] cursor-pointer px-2 text-meta transition-colors duration-[120ms] ease-tool ${
            theme === o.value ? "bg-sunken font-semibold text-ink" : "text-muted hover:text-ink"
          }`}
        >
          <span aria-hidden>{o.icon}</span>
          <span className="max-[640px]:sr-only"> {o.label}</span>
        </button>
      ))}
    </div>
  );
}
