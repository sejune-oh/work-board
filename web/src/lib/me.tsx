"use client";

import type { Session } from "@supabase/supabase-js";
import { createContext, useContext, useEffect, useState } from "react";
import { DEV_USER_EVENT, type DevIdentity, type Loadable, getDevIdentity } from "./api";
import { authEnabled } from "./auth";
import type { Me } from "./types";

const MeContext = createContext<Loadable<Me> | null>(null);
const SessionContext = createContext<Session | null | undefined>(null);

/** The gate loads `/api/me` once for the whole app and provides it only once it has succeeded. */
export function MeProvider({ value, children }: { value: Loadable<Me>; children: React.ReactNode }) {
  return <MeContext.Provider value={value}>{children}</MeContext.Provider>;
}

export function SessionProvider({ value, children }: { value: Session | null | undefined; children: React.ReactNode }) {
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

/** Always `null` when GitHub sign-in is off; `undefined` while the stored session is being read. */
export function useAuthSession(): Session | null | undefined {
  return useContext(SessionContext);
}

/** `undefined` until the stored dev user has been read. */
export function useDevIdentity(): DevIdentity | null | undefined {
  const [identity, setIdentity] = useState<DevIdentity | null | undefined>(undefined);
  useEffect(() => {
    const sync = () => setIdentity(getDevIdentity());
    sync();
    window.addEventListener(DEV_USER_EVENT, sync);
    return () => window.removeEventListener(DEV_USER_EVENT, sync);
  }, []);
  return identity;
}

/** A GitHub session, or a picked dev user when sign-in is off. `undefined` while that is still being read. */
export function useSignedIn(): boolean | undefined {
  const session = useAuthSession();
  const dev = useDevIdentity();
  const current = authEnabled ? session : dev;
  return current === undefined ? undefined : current !== null;
}

/** The GitHub login, or the dev user's login. */
export function useLogin(): string | null {
  const session = useAuthSession();
  const dev = useDevIdentity();
  if (!authEnabled) return dev?.login ?? null;
  const meta = session?.user.user_metadata as { user_name?: unknown } | undefined;
  return typeof meta?.user_name === "string" ? meta.user_name : (session?.user.email ?? null);
}

export function useMe(): Loadable<Me> {
  const me = useContext(MeContext);
  if (!me) throw new Error("useMe must be used inside MeProvider");
  return me;
}

const ALL_OFF: Me["settings"] = { sprintsEnabled: false, sizeEnabled: false, priorityEnabled: false };

/** Advanced fields stay hidden until the settings say they are on. */
export function useSettings(): Me["settings"] {
  return useMe().data?.settings ?? ALL_OFF;
}
