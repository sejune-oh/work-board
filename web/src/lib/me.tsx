"use client";

import type { Session } from "@supabase/supabase-js";
import { usePathname } from "next/navigation";
import { createContext, useContext } from "react";
import { type Loadable, useApi } from "./api";
import { authEnabled, useSession } from "./auth";
import type { Me } from "./types";

const MeContext = createContext<Loadable<Me> | null>(null);
const SessionContext = createContext<Session | null | undefined>(null);

/**
 * Loads `/api/me` once for the whole app; it refetches only when the person or tenant changes.
 * With GitHub sign-in on, nothing is requested until there is a session.
 */
export function MeProvider({ children }: { children: React.ReactNode }) {
  const session = useSession();
  const onAuthPage = usePathname().startsWith("/auth/");
  const me = useApi<Me>(onAuthPage || (authEnabled && !session) ? null : "/api/me");
  return (
    <SessionContext.Provider value={session}>
      <MeContext.Provider value={me}>{children}</MeContext.Provider>
    </SessionContext.Provider>
  );
}

/** Always `null` when GitHub sign-in is off; `undefined` while the stored session is being read. */
export function useAuthSession(): Session | null | undefined {
  return useContext(SessionContext);
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
