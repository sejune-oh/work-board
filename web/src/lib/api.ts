"use client";

import { useCallback, useEffect, useState } from "react";
import { AUTH_EVENT, GATE_EVENT, authEnabled, getAccessToken, getTenant, signOutIfStill } from "./auth";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080";
const DEV_USER_KEY = "workboard.devUser";
const DEV_TENANT_KEY = "workboard.devTenant";
export const DEV_USER_EVENT = "workboard:dev-user";

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
  window.dispatchEvent(new Event(DEV_USER_EVENT));
}

export class ApiError extends Error {
  /** `code` is the `error` field of the response body, e.g. `tenant_required`. */
  constructor(public status: number, message: string, public code: string | null = null, public body: unknown = null) {
    super(message);
  }
}

async function readBody(res: Response): Promise<{ code: string | null; body: unknown }> {
  try {
    const body = (await res.json()) as { error?: unknown } | null;
    return { code: typeof body?.error === "string" ? body.error : null, body };
  } catch {
    return { code: null, body: null };
  }
}

async function authHeaders(): Promise<Record<string, string>> {
  const headers: Record<string, string> = {};
  if (authEnabled) {
    const token = await getAccessToken();
    if (token) headers.Authorization = `Bearer ${token}`;
    const tenant = getTenant();
    if (tenant) headers["X-Tenant"] = tenant;
    return headers;
  }
  const identity = getDevIdentity();
  if (identity) {
    headers["X-Dev-User"] = identity.login;
    if (identity.tenantId) headers["X-Dev-Tenant"] = identity.tenantId;
  }
  return headers;
}

export async function apiGet<T>(path: string): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json", ...(await authHeaders()) };
  const res = await fetch(`${API_URL}${path}`, { headers });
  if (!res.ok) {
    const { code, body } = await readBody(res);
    if (res.status === 401 && code === "unauthenticated" && headers.Authorization) {
      // After the caller has stored this error, so the sign-in screen can say why it appeared.
      const token = headers.Authorization.slice("Bearer ".length);
      setTimeout(() => void signOutIfStill(token), 0);
    }
    const refused =
      (res.status === 401 && (code === "tenant_required" || !authEnabled)) || (res.status === 403 && code === "no_membership");
    if (refused && path !== "/api/me") setTimeout(() => window.dispatchEvent(new Event(GATE_EVENT)), 0);
    throw new ApiError(res.status, `${res.status} ${res.statusText}`, code, body);
  }
  return (await res.json()) as T;
}

export type Loadable<T> = { data: T | null; error: ApiError | Error | null; loading: boolean; reload: () => void };

/**
 * Fetches `path` and refetches when it changes or the person or tenant switches. A switch drops the
 * previous person's data first. `null` skips the request.
 */
export function useApi<T>(path: string | null): Loadable<T> {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const [loading, setLoading] = useState(path !== null);
  const [tick, setTick] = useState(0);
  const reload = useCallback(() => setTick((t) => t + 1), []);

  useEffect(() => {
    const onUser = () => {
      setData(null);
      setError(null);
      reload();
    };
    window.addEventListener(DEV_USER_EVENT, onUser);
    window.addEventListener(AUTH_EVENT, onUser);
    return () => {
      window.removeEventListener(DEV_USER_EVENT, onUser);
      window.removeEventListener(AUTH_EVENT, onUser);
    };
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
