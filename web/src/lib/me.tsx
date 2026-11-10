"use client";

import { createContext, useContext } from "react";
import { type Loadable, useApi } from "./api";
import type { Me } from "./types";

const MeContext = createContext<Loadable<Me> | null>(null);

/** Loads `/api/me` once for the whole app; it refetches only when the dev user changes. */
export function MeProvider({ children }: { children: React.ReactNode }) {
  const me = useApi<Me>("/api/me");
  return <MeContext.Provider value={me}>{children}</MeContext.Provider>;
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
