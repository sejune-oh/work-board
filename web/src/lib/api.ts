"use client";

import { useCallback, useEffect, useState } from "react";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080";
const DEV_USER_KEY = "workboard.devUser";
const DEV_TENANT_KEY = "workboard.devTenant";

/** The same login can belong to several tenants, so a dev identity is the pair. */
export type DevIdentity = { login: string; tenantId: string | null };

function readStorage(key: string): string | null {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeStorage(key: string, value: string | null) {
  try {
    if (value) window.localStorage.setItem(key, value);
    else window.localStorage.removeItem(key);
  } catch {
    // Storage can be unavailable (private mode); the picker simply asks again next time.
  }
}

export function getDevIdentity(): DevIdentity | null {
  const login = readStorage(DEV_USER_KEY);
  return login ? { login, tenantId: readStorage(DEV_TENANT_KEY) } : null;
}

export function setDevIdentity(identity: DevIdentity | null) {
  writeStorage(DEV_USER_KEY, identity?.login ?? null);
  writeStorage(DEV_TENANT_KEY, identity?.tenantId ?? null);
  window.dispatchEvent(new Event("workboard:dev-user"));
}

export class ApiError extends Error {
  /** The `error` field of the response body, e.g. `tenant_required`. */
  constructor(public status: number, message: string, public code: string | null = null) {
    super(message);
  }
}

async function errorCode(res: Response): Promise<string | null> {
  try {
    const body = (await res.json()) as { error?: unknown };
    return typeof body.error === "string" ? body.error : null;
  } catch {
    return null;
  }
}

export async function apiGet<T>(path: string): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json" };
  const identity = getDevIdentity();
  if (identity) {
    headers["X-Dev-User"] = identity.login;
    if (identity.tenantId) headers["X-Dev-Tenant"] = identity.tenantId;
  }

  const res = await fetch(`${API_URL}${path}`, { headers });
  if (!res.ok) throw new ApiError(res.status, `${res.status} ${res.statusText}`, await errorCode(res));
  return (await res.json()) as T;
}

export type Loadable<T> = { data: T | null; error: ApiError | Error | null; loading: boolean; reload: () => void };

/** Fetches `path` and refetches when it changes or the dev user switches. `null` skips the request. */
export function useApi<T>(path: string | null): Loadable<T> {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const [loading, setLoading] = useState(path !== null);
  const [tick, setTick] = useState(0);
  const reload = useCallback(() => setTick((t) => t + 1), []);

  useEffect(() => {
    const onUser = () => reload();
    window.addEventListener("workboard:dev-user", onUser);
    return () => window.removeEventListener("workboard:dev-user", onUser);
  }, [reload]);

  useEffect(() => {
    if (path === null) {
      setLoading(false);
      return;
    }
    let cancelled = false;
    setLoading(true);
    apiGet<T>(path)
      .then((d) => {
        if (!cancelled) {
          setData(d);
          setError(null);
        }
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [path, tick]);

  return { data, error, loading, reload };
}
