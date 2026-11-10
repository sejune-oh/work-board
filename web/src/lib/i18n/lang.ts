export type Lang = "ko" | "en";

export const LANGS: Lang[] = ["ko", "en"];
export const LANG_KEY = "workboard.lang";
export const LANG_PENDING_ATTR = "data-lang-pending";

export function isLang(v: unknown): v is Lang {
  return v === "ko" || v === "en";
}

/**
 * Runs before paint: the stored choice, else Korean when the browser asks for it, else English.
 * A non-Korean page stays hidden until React has swapped the prerendered Korean text (with a
 * fallback in case the app never boots).
 */
export const LANG_INIT_SCRIPT = `(function(){var d=document.documentElement,l=null;try{l=localStorage.getItem("${LANG_KEY}")}catch(e){}if(l!=="ko"&&l!=="en")l=/^ko/i.test(navigator.language||"")?"ko":"en";d.lang=l;if(l!=="ko"){d.setAttribute("${LANG_PENDING_ATTR}","");setTimeout(function(){d.removeAttribute("${LANG_PENDING_ATTR}")},1500)}})()`;
