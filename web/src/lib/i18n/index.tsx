"use client";

import { createContext, useCallback, useContext, useLayoutEffect, useMemo, useState } from "react";
import { en } from "./en";
import { type Format, makeFormat } from "./format";
import { type Dict, ko } from "./ko";
import { LANG_KEY, LANG_PENDING_ATTR, type Lang, isLang } from "./lang";

export type { Dict } from "./ko";
export type { Format } from "./format";
export { LANGS, type Lang } from "./lang";

export const DICTS: Record<Lang, Dict> = { ko, en };

type I18n = { lang: Lang; setLang: (lang: Lang) => void; t: Dict; fmt: Format };

const I18nContext = createContext<I18n | null>(null);

/** The init script has already resolved the language onto `<html lang>`; this picks it up before paint. */
function initialLang(): Lang {
  const l = document.documentElement.lang;
  return isLang(l) ? l : "ko";
}

/**
 * Pages are prerendered in Korean. The first layout effect swaps to the chosen language before the
 * browser paints, and lifts the hide the init script put on a non-Korean first paint.
 */
export function I18nProvider({ children }: { children: React.ReactNode }) {
  const [lang, setLangState] = useState<Lang>("ko");

  useLayoutEffect(() => {
    setLangState(initialLang());
    document.documentElement.removeAttribute(LANG_PENDING_ATTR);
  }, []);

  useLayoutEffect(() => {
    document.documentElement.lang = lang;
    const title = DICTS[lang].app.title;
    document.title = title;
    // Next re-applies the static (Korean) metadata title on client navigation.
    const observer = new MutationObserver(() => {
      if (document.title !== title) document.title = title;
    });
    observer.observe(document.head, { childList: true, subtree: true, characterData: true });
    return () => observer.disconnect();
  }, [lang]);

  const setLang = useCallback((next: Lang) => {
    setLangState(next);
    try {
      window.localStorage.setItem(LANG_KEY, next);
    } catch {
      // Without storage the choice lasts until reload.
    }
  }, []);

  const value = useMemo<I18n>(() => {
    const t = DICTS[lang];
    return { lang, setLang, t, fmt: makeFormat(lang, t) };
  }, [lang, setLang]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18n {
  const ctx = useContext(I18nContext);
  if (!ctx) throw new Error("useI18n must be used inside I18nProvider");
  return ctx;
}

/** The dictionary for the chosen language: `t.nav.overview`, `t.me.stale(3)`. */
export function useT(): Dict {
  return useI18n().t;
}

/** Dates, relative times, numbers and names in the chosen language. */
export function useFormat(): Format {
  return useI18n().fmt;
}
