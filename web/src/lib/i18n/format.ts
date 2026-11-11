import { daysSince, personName } from "../labels";
import type { Dict } from "./ko";
import type { Lang } from "./lang";

type Person = { displayName: string | null; githubLogin: string | null };

export type Format = {
  num: (n: number) => string;
  /** "11월 10일 (화)" / "Tue, November 10". */
  date: (iso: string) => string;
  /** "11.10" / "11/10". */
  shortDate: (iso: string) => string;
  /** "오늘 · 11월 10일", "어제 · 11월 9일", "11월 8일". */
  dayLabel: (iso: string, now?: Date) => string;
  /** "오늘", "3일 전" / "today", "3 days ago"; `compact` gives "3d ago" for narrow columns. */
  relativeDays: (iso: string, compact?: boolean) => string;
  /** "방금", "3시간 전", "2일 전" for activity lines; `compact` as above. */
  relativeTime: (iso: string, compact?: boolean) => string;
  person: (p: Person) => string;
  issueType: (type: string) => string;
};

const LOCALE: Record<Lang, string> = { ko: "ko-KR", en: "en-US" };

export function makeFormat(lang: Lang, t: Dict): Format {
  const locale = LOCALE[lang];
  const number = new Intl.NumberFormat(locale);
  const longDate = new Intl.DateTimeFormat(locale, { month: "long", day: "numeric", weekday: "short" });
  const monthDay = new Intl.DateTimeFormat(locale, { month: "long", day: "numeric" });
  const shortEn = new Intl.DateTimeFormat(locale, { month: "2-digit", day: "2-digit" });
  const rel = new Intl.RelativeTimeFormat(locale, { numeric: "always" });
  const relNarrow = new Intl.RelativeTimeFormat(locale, { numeric: "always", style: "narrow" });
  const ago = (n: number, unit: Intl.RelativeTimeFormatUnit, compact = false) => (compact ? relNarrow : rel).format(-n, unit);

  return {
    num: (n) => number.format(n),
    date: (iso) => longDate.format(new Date(iso)),
    shortDate: (iso) => {
      const d = new Date(iso);
      if (lang !== "ko") return shortEn.format(d);
      return `${String(d.getMonth() + 1).padStart(2, "0")}.${String(d.getDate()).padStart(2, "0")}`;
    },
    dayLabel: (iso, now = new Date()) => {
      const d = new Date(iso);
      const base = monthDay.format(d);
      const startOf = (x: Date) => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
      const diff = Math.round((startOf(now) - startOf(d)) / 86_400_000);
      if (diff === 0) return t.time.dayWithDate(t.time.todayHeading, base);
      if (diff === 1) return t.time.dayWithDate(t.time.yesterday, base);
      return base;
    },
    relativeDays: (iso, compact) => {
      const d = daysSince(iso);
      return d === 0 ? t.time.today : ago(d, "day", compact);
    },
    relativeTime: (iso, compact) => {
      const minutes = Math.max(0, Math.floor((Date.now() - Date.parse(iso)) / 60_000));
      if (minutes < 1) return t.time.justNow;
      if (minutes < 60) return ago(minutes, "minute", compact);
      const hours = Math.floor(minutes / 60);
      if (hours < 24) return ago(hours, "hour", compact);
      return ago(Math.floor(hours / 24), "day", compact);
    },
    person: (p) => personName(p, t.common.unknownPerson),
    issueType: (type) => (t.issueType as Record<string, string>)[type] ?? type,
  };
}
