"use client";

import { useRouter } from "next/navigation";
import { type RefObject, useEffect } from "react";

/** `g` then a letter jumps to a screen. The nav shows the same letters. */
export const GO_KEYS: Record<string, string> = {
  o: "/",
  m: "/me",
  p: "/people",
  b: "/projects",
  n: "/notes",
  s: "/settings",
};

/** Lists mark each focusable row with this attribute so j/k can walk them. */
export const ROW_ATTR = "data-row";

const GO_TIMEOUT_MS = 1200;

function isTyping(el: EventTarget | null) {
  if (!(el instanceof HTMLElement)) return false;
  return el.tagName === "INPUT" || el.tagName === "TEXTAREA" || el.tagName === "SELECT" || el.isContentEditable;
}

function moveRow(step: 1 | -1) {
  const rows = [...document.querySelectorAll<HTMLElement>(`main [${ROW_ATTR}]`)].filter((r) => r.offsetParent !== null);
  if (rows.length === 0) return;
  const i = rows.indexOf(document.activeElement as HTMLElement);
  const next = i < 0 ? (step === 1 ? 0 : rows.length - 1) : Math.min(rows.length - 1, Math.max(0, i + step));
  rows[next].focus();
  rows[next].scrollIntoView({ block: "nearest" });
}

/**
 * `/` search, `g`+letter screens, `j`/`k` rows. Off while typing, with a modifier held,
 * or while the issue panel (a dialog) is open; Esc for the panel lives in IssuePanel.
 */
export function useShortcuts(searchRef: RefObject<HTMLInputElement | null>) {
  const router = useRouter();

  useEffect(() => {
    let goPending = false;
    let goTimer: ReturnType<typeof setTimeout> | undefined;

    const onKey = (e: KeyboardEvent) => {
      if (e.defaultPrevented || e.metaKey || e.ctrlKey || e.altKey || isTyping(e.target)) return;
      if (document.querySelector('[role="dialog"]')) return;

      if (goPending) {
        goPending = false;
        clearTimeout(goTimer);
        const href = GO_KEYS[e.key];
        if (href) {
          e.preventDefault();
          router.push(href);
        }
        return;
      }

      switch (e.key) {
        case "/":
          e.preventDefault();
          searchRef.current?.focus();
          break;
        case "g":
          goPending = true;
          goTimer = setTimeout(() => (goPending = false), GO_TIMEOUT_MS);
          break;
        case "j":
          e.preventDefault();
          moveRow(1);
          break;
        case "k":
          e.preventDefault();
          moveRow(-1);
          break;
      }
    };

    window.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("keydown", onKey);
      clearTimeout(goTimer);
    };
  }, [router, searchRef]);
}
