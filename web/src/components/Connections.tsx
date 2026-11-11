"use client";

import { useFormat, useT } from "@/lib/i18n";
import type { GithubConnection } from "@/lib/types";
import { GitHubMark } from "./Auth";

const APP_SLUG = process.env.NEXT_PUBLIC_GITHUB_APP_SLUG ?? "";

/** GitHub's install screen for the App, or `null` when the slug is not configured. */
export const githubInstallUrl: string | null = APP_SLUG
  ? `https://github.com/apps/${encodeURIComponent(APP_SLUG)}/installations/new`
  : null;

export const ROW_LINE = "relative after:absolute after:inset-x-5 after:top-0 after:h-px after:bg-rule first:after:hidden";

/** Account login, type, repository access and when it was connected; a disconnected one steps back. */
export function ConnectionRow({ connection: c, className = "" }: { connection: GithubConnection; className?: string }) {
  const t = useT();
  const fmt = useFormat();
  const ct = t.connections;
  const off = c.disconnectedAt !== null;
  const selection = c.repositorySelection === "all" ? ct.all : c.repositorySelection === "selected" ? ct.selected : ct.unknownSelection;
  return (
    <li className={`flex flex-wrap items-center gap-x-4 gap-y-1 px-5 py-4 ${off ? "text-muted" : ""} ${className}`}>
      <span className="flex min-w-0 items-center gap-2.5">
        <span className={off ? "text-muted" : "text-ink"}>
          <GitHubMark size={16} />
        </span>
        <span className={`truncate font-mono text-ui font-medium ${off ? "" : "text-ink"}`}>{c.accountLogin}</span>
      </span>
      <span className="text-small text-ink-soft">
        {c.accountType === "organization" ? ct.organization : ct.user}
        <span aria-hidden className="mx-1.5 text-faint">
          ·
        </span>
        {selection}
      </span>
      <span className="w-full whitespace-nowrap text-small tabular-nums text-muted min-[640px]:ml-auto min-[640px]:w-auto">
        {off ? ct.disconnectedOn(fmt.date(c.disconnectedAt!)) : ct.connectedOn(fmt.date(c.createdAt))}
      </span>
    </li>
  );
}
