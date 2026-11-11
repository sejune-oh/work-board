"use client";

import { DICTS, LANGS, useI18n } from "@/lib/i18n";

/** `한국어 | English`, each name in its own language; `KO | EN` on phones. Same track as the theme switch. */
export function LangToggle() {
  const { lang, setLang, t } = useI18n();

  return (
    <div role="group" aria-label={t.lang.group} className="seg">
      {LANGS.map((l) => (
        <button key={l} type="button" lang={l} aria-pressed={lang === l} onClick={() => setLang(l)}>
          <span aria-hidden className="font-mono uppercase min-[640px]:hidden">
            {l}
          </span>
          <span className="max-[639px]:sr-only">{DICTS[l].langName}</span>
        </button>
      ))}
    </div>
  );
}
