"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";

/**
 * `g` then a letter jumps to a screen. The nav shows the same letters. Keys are matched by
 * physical position (`KeyboardEvent.code`) so they also work while the Korean IME is on.
 */
export const GO_KEYS: Record<string, string> = {
  KeyO: "/",
  KeyM: "/me",
  KeyP: "/people",
  KeyB: "/projects",
  KeyN: "/notes",
  KeyS: "/settings",
};

/** Lists mark each focusable row with this attribute so j/k can walk them. */
export const ROW_ATTR = "data-row";

const GO_TIMEOUT_MS = 1200;

function isTyping(el: EventTarget | null) {
  if (!(el instanceof HTMLElement)) return false;
  return el.tagName === "INPUT" || el.tagName === "TEXTAREA" || el.tagName === "SELECT" || el.isContentEditable;
}

/** Returns false when there is no row to move to, so the key falls through untouched. */
function moveRow(step: 1 | -1): boolean {
  const rows = [...document.querySelectorAll<HTMLElement>(`main [${ROW_ATTR}]`)].filter((r) => r.offsetParent !== null);
  if (rows.length === 0) return false;
  const i = rows.indexOf(document.activeElement as HTMLElement);
  const next = i < 0 ? (step === 1 ? 0 : rows.length - 1) : Math.min(rows.length - 1, Math.max(0, i + step));
  rows[next].focus();
  rows[next].scrollIntoView({ block: "nearest" });
  return true;
}

/**
 * `/` search, `g`+letter screens, `j`/`k` rows. Off while typing, with a modifier held,
 * or while the issue panel (a dialog) is open; Esc for the panel lives in IssuePanel.
 */
export function useShortcuts(focusSearch: () => void) {
  const router = useRouter();

  useEffect(() => {
    let goPending = false;
    let goTimer: ReturnType<typeof setTimeout> | undefined;

    const onKey = (e: KeyboardEvent) => {
      if (e.defaultPrevented || e.metaKey || e.ctrlKey || e.altKey || e.isComposing || e.keyCode === 229 || isTyping(e.target)) return;
      if (document.querySelector('[role="dialog"]')) return;

      if (goPending) {
        goPending = false;
        clearTimeout(goTimer);
        const href = GO_KEYS[e.code];
        if (href) {
          e.preventDefault();
          router.push(href);
        }
        return;
      }

      if (e.key === "/" || (e.code === "Slash" && !e.shiftKey)) {
        e.preventDefault();
        focusSearch();
        return;
      }
      if (e.shiftKey) return;
      switch (e.code) {
        case "KeyG":
          goPending = true;
          goTimer = setTimeout(() => (goPending = false), GO_TIMEOUT_MS);
          break;
        case "KeyJ":
          if (moveRow(1)) e.preventDefault();
          break;
        case "KeyK":
          if (moveRow(-1)) e.preventDefault();
          break;
      }
    };

    window.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("keydown", onKey);
      clearTimeout(goTimer);
    };
  }, [router, focusSearch]);
}
