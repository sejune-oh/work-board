"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ApiError, SYNC_STATUS_PATH, runSync, useApi } from "@/lib/api";
import { useFormat, useT } from "@/lib/i18n";
import { useMe } from "@/lib/me";
import type { RunSyncError, SyncStatus as Status } from "@/lib/types";

const IDLE_POLL_MS = 60_000;
const RUNNING_POLL_MS = 5_000;
const NOTICE_MS = 6_000;

/**
 * "Synced 3 min ago" for the top bar, refreshed every minute (every 5 s while a sync runs).
 * Members see nothing until GitHub is connected; admins get a link to settings and a "sync now" button.
 */
export function SyncStatus({ className = "" }: { className?: string }) {
  const { data, reload } = useApi<Status>(SYNC_STATUS_PATH);
  const admin = useMe().data?.member.role === "admin";
  const t = useT();
  const s = t.sync;
  const fmt = useFormat();
  const [starting, setStarting] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const running = data?.running === true || starting;

  useEffect(() => {
    const id = setInterval(reload, running ? RUNNING_POLL_MS : IDLE_POLL_MS);
    return () => clearInterval(id);
  }, [reload, running]);

  useEffect(() => {
    if (!notice) return;
    const id = setTimeout(() => setNotice(null), NOTICE_MS);
    return () => clearTimeout(id);
  }, [notice]);

  if (!data) return null;

  if (!data.connected) {
    if (!admin) return null;
    return (
      <p className={`items-center gap-2 whitespace-nowrap text-small text-muted ${className}`}>
        <span>{s.notConnected}</span>
        <Link href="/settings" className="link text-ink-soft">
          {s.connect}
        </Link>
      </p>
    );
  }

  const start = async () => {
    setStarting(true);
    setNotice(null);
    try {
      await runSync();
      setNotice(s.started);
    } catch (e) {
      const code = (e instanceof ApiError ? e.code : null) as RunSyncError | null;
      const messages: Record<RunSyncError, string> = {
        sync_running: s.alreadyRunning,
        not_connected: s.notConnectedError,
        github_app_not_configured: s.notConfigured,
        admin_only: s.adminOnly,
      };
      setNotice(code && code in messages ? messages[code] : s.runFailed);
    } finally {
      setStarting(false);
      reload();
    }
  };

  const failed = !data.running && data.lastResult === "failed";
  const reason = data.lastError ?? s.unknownReason;
  let label: string;
  let title: string | undefined;
  if (data.running) label = s.running;
  else if (failed) {
    label = s.failed;
    title = data.lastSyncedAt ? s.failedTitle(fmt.relativeTime(data.lastSyncedAt), reason) : reason;
  } else if (data.lastSyncedAt) label = s.syncedAgo(fmt.relativeTime(data.lastSyncedAt));
  else label = s.never;

  return (
    <div className={`min-w-0 items-center gap-2 text-small ${className}`}>
      <p className="flex min-w-0 items-center gap-1.5 whitespace-nowrap" title={title}>
        <span
          aria-hidden
          className={`inline-block h-1.5 w-1.5 shrink-0 rounded-full ${data.running ? "animate-pulse bg-ink-soft" : failed ? "bg-ink" : "bg-faint"}`}
        />
        <span className={failed && !notice ? "font-medium text-ink" : "text-muted"}>{notice ?? label}</span>
        {failed && !notice && <span className="max-w-[16rem] truncate text-muted max-[1279px]:sr-only">· {reason}</span>}
      </p>
      <span aria-live="polite" className="sr-only">
        {notice}
      </span>
      {admin && (
        <button type="button" onClick={start} disabled={running} className="btn h-8 shrink-0 px-2.5 text-meta font-medium text-ink-soft hover:text-ink">
          {s.runNow}
        </button>
      )}
    </div>
  );
}
