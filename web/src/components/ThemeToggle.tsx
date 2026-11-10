"use client";

import { THEME_KEY } from "@/lib/theme";

/** Flips between light and dark and remembers the choice; without one, the OS setting wins. */
export function ThemeToggle() {
  const toggle = () => {
    const root = document.documentElement;
    const dark = root.dataset.theme
      ? root.dataset.theme === "dark"
      : window.matchMedia("(prefers-color-scheme: dark)").matches;
    const next = dark ? "light" : "dark";
    root.dataset.theme = next;
    try {
      window.localStorage.setItem(THEME_KEY, next);
    } catch {
      // Without storage the choice lasts until reload.
    }
  };

  return (
    <button
      type="button"
      onClick={toggle}
      aria-label="밝기 바꾸기"
      className="h-8 shrink-0 cursor-pointer rounded-card border border-rule bg-surface px-2.5 text-[13px] text-ink-soft transition-colors duration-[120ms] ease-paper hover:border-ink-soft"
    >
      밝게 · 어둡게
    </button>
  );
}
