"use client";

import { type Session, type SupabaseClient, createClient } from "@supabase/supabase-js";
import { useEffect, useState } from "react";

const SUPABASE_URL = process.env.NEXT_PUBLIC_SUPABASE_URL;
const SUPABASE_ANON_KEY = process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY;
const RETURN_TO_KEY = "workboard.returnTo";
const TENANT_KEY = "workboard.tenant";

/** Without the Supabase env vars the app runs on the dev user picker instead of GitHub sign-in. */
export const authEnabled = Boolean(SUPABASE_URL && SUPABASE_ANON_KEY);

/** Fired when the signed-in person or the chosen tenant changes, so data is fetched again. */
export const AUTH_EVENT = "workboard:auth";

let client: SupabaseClient | null = null;

export function supabase(): SupabaseClient | null {
  if (!authEnabled) return null;
  client ??= createClient(SUPABASE_URL!, SUPABASE_ANON_KEY!, {
    auth: { flowType: "pkce", detectSessionInUrl: false, persistSession: true, autoRefreshToken: true },
  });
  return client;
}

function read(storage: () => Storage, key: string): string | null {
  try {
    return storage().getItem(key);
  } catch {
    return null;
  }
}

function write(storage: () => Storage, key: string, value: string | null) {
  try {
    if (value) storage().setItem(key, value);
    else storage().removeItem(key);
  } catch {
    // Without storage the value lasts only for this page.
  }
}

const local = () => window.localStorage;
const session = () => window.sessionStorage;

/** Only same-site paths, so the callback can never send someone to another origin. */
function safePath(path: string | null): string {
  return path && path.startsWith("/") && !path.startsWith("//") && !path.startsWith("/auth/") ? path : "/";
}

/** `getSession` refreshes an expired access token before returning it. */
export async function getAccessToken(): Promise<string | null> {
  const sb = supabase();
  if (!sb) return null;
  const { data } = await sb.auth.getSession();
  return data.session?.access_token ?? null;
}

export async function signInWithGitHub() {
  const sb = supabase();
  if (!sb) return;
  if (!window.location.pathname.startsWith("/auth/")) {
    write(session, RETURN_TO_KEY, safePath(window.location.pathname + window.location.search));
  }
  const redirectTo = new URL("/auth/callback", window.location.origin).toString();
  const { error } = await sb.auth.signInWithOAuth({ provider: "github", options: { redirectTo } });
  if (error) throw error;
}

export class SignInError extends Error {
  constructor(public kind: "disabled" | "denied" | "missing_code" | "exchange", message: string) {
    super(message);
  }
}

let finishing: Promise<string> | null = null;

/**
 * Trades the `?code=` from GitHub for a session and returns the path the person started from.
 * The code is single-use, so a second call (React runs effects twice in dev) shares the first.
 */
export function finishSignIn(search: URLSearchParams): Promise<string> {
  finishing ??= exchange(search);
  return finishing;
}

async function exchange(search: URLSearchParams): Promise<string> {
  const sb = supabase();
  if (!sb) throw new SignInError("disabled", "GitHub sign-in is not configured");
  const providerError = search.get("error");
  if (providerError) throw new SignInError("denied", search.get("error_description") ?? providerError);
  const code = search.get("code");
  if (!code) throw new SignInError("missing_code", "No code in the callback URL");
  const { error } = await sb.auth.exchangeCodeForSession(code);
  if (error) throw new SignInError("exchange", error.message);
  const returnTo = safePath(read(session, RETURN_TO_KEY));
  write(session, RETURN_TO_KEY, null);
  return returnTo;
}

export async function signOut() {
  write(local, TENANT_KEY, null);
  await supabase()?.auth.signOut({ scope: "local" });
}

/** Drops the session the API rejected, unless a newer one has replaced it meanwhile. */
export async function signOutIfStill(rejectedToken: string) {
  if ((await getAccessToken()) === rejectedToken) await signOut();
}

export function getTenant(): string | null {
  return read(local, TENANT_KEY);
}

export function setTenant(tenantId: string | null) {
  write(local, TENANT_KEY, tenantId);
  window.dispatchEvent(new Event(AUTH_EVENT));
}

/** `undefined` while the stored session is still being read. */
export function useSession(): Session | null | undefined {
  const [current, setCurrent] = useState<Session | null | undefined>(authEnabled ? undefined : null);

  useEffect(() => {
    const sb = supabase();
    if (!sb) return;
    let userId: string | null | undefined;
    const { data } = sb.auth.onAuthStateChange((_event, next) => {
      setCurrent(next);
      const nextId = next?.user.id ?? null;
      if (userId !== undefined && nextId !== userId) window.dispatchEvent(new Event(AUTH_EVENT));
      userId = nextId;
    });
    return () => data.subscription.unsubscribe();
  }, []);

  return current;
}
