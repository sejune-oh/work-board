"use client";

import { DICTS, LANGS, useI18n } from "@/lib/i18n";

/** `한국어 | English`, each name in its own language; same segmented look as the theme switch. */
export function LangToggle() {
  const { lang, setLang, t } = useI18n();

  return (
    <div role="group" aria-label={t.lang.group} className="inline-flex shrink-0 overflow-hidden rounded-control border border-rule">
      {LANGS.map((l) => (
        <button
          key={l}
          type="button"
          lang={l}
          aria-pressed={lang === l}
          onClick={() => setLang(l)}
          className={`h-[26px] cursor-pointer px-2 text-meta transition-colors duration-[120ms] ease-tool ${
            lang === l ? "bg-sunken font-semibold text-ink" : "text-muted hover:text-ink"
          }`}
        >
          <span aria-hidden className="font-mono uppercase min-[641px]:hidden">
            {l}
          </span>
          <span className="max-[640px]:sr-only">{DICTS[l].langName}</span>
        </button>
      ))}
    </div>
  );
}
