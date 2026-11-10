"use client";

import { useCallback, useEffect, useState } from "react";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080";
const DEV_USER_KEY = "workboard.devUser";

export function getDevUser(): string | null {
  try {
    return window.localStorage.getItem(DEV_USER_KEY);
  } catch {
    return null;
  }
}

export function setDevUser(login: string | null) {
  try {
    if (login) window.localStorage.setItem(DEV_USER_KEY, login);
    else window.localStorage.removeItem(DEV_USER_KEY);
  } catch {
    // Storage can be unavailable (private mode); the picker simply asks again next time.
  }
  window.dispatchEvent(new Event("workboard:dev-user"));
}

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

export async function apiGet<T>(path: string): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json" };
  const user = getDevUser();
  if (user) headers["X-Dev-User"] = user;

  const res = await fetch(`${API_URL}${path}`, { headers });
  if (!res.ok) throw new ApiError(res.status, `${res.status} ${res.statusText}`);
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
