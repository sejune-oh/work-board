"use client";

import { usePathname, useRouter } from "next/navigation";
import { Suspense, useEffect, useRef } from "react";
import { ApiError, type Loadable, setDevIdentity, useApi } from "@/lib/api";
import { GATE_EVENT, LOGIN_PATH, authEnabled, isLeaving, loginHref, signOut, useSession } from "@/lib/auth";
import { MeProvider, SessionProvider, useSignedIn } from "@/lib/me";
import type { Me } from "@/lib/types";
import { AppShell } from "./AppShell";
import { CheckingScreen, GateErrorScreen, NoMembershipScreen, TenantChoiceScreen } from "./Auth";
import { Loading } from "./Feedback";

type GateState = "checking" | "signedOut" | "resolving" | "chooseTenant" | "noMembership" | "error" | "ready";

/** `/login` and the OAuth callback render on their own; everything else waits for the gate. */
export function isPublicPath(pathname: string) {
  return pathname === LOGIN_PATH || pathname.startsWith(`${LOGIN_PATH}/`) || pathname.startsWith("/auth/");
}

/** The API turned the person away: the session (or dev user) is gone or no longer valid. */
function rejected(error: Error | null): boolean {
  if (!(error instanceof ApiError) || error.status !== 401) return false;
  return !authEnabled || error.code !== "tenant_required";
}

function gateState(signedIn: boolean | undefined, me: Loadable<Me>): GateState {
  if (signedIn === undefined) return "checking";
  if (!signedIn || rejected(me.error)) return "signedOut";
  if (me.error instanceof ApiError && me.error.status === 401) return "chooseTenant";
  if (me.error instanceof ApiError && me.error.status === 403 && me.error.code === "no_membership") return "noMembership";
  if (me.error) return "error";
  return me.data ? "ready" : "resolving";
}

/**
 * Wraps the whole app. Until the session (or dev user) and `/api/me` both check out it shows only a
 * standalone screen, so the sidebar, top bar, issue panel, shortcuts and pages never mount early.
 */
export function AuthGate({ children }: { children: React.ReactNode }) {
  const session = useSession();
  const publicPage = isPublicPath(usePathname());
  return (
    <SessionProvider value={session}>
      {publicPage ? <Suspense fallback={<CheckingScreen />}>{children}</Suspense> : <Gate>{children}</Gate>}
    </SessionProvider>
  );
}

function Gate({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const signedIn = useSignedIn();
  const me = useApi<Me>(signedIn ? "/api/me" : null);
  const state = gateState(signedIn, me);
  const wasSignedIn = useRef(false);
  const redirecting = useRef(false);
  const { reload } = me;

  useEffect(() => {
    window.addEventListener(GATE_EVENT, reload);
    return () => window.removeEventListener(GATE_EVENT, reload);
  }, [reload]);

  useEffect(() => {
    if (state !== "signedOut") {
      if (signedIn) wasSignedIn.current = true;
      return;
    }
    if (redirecting.current) return;
    redirecting.current = true;
    const refusedByApi = rejected(me.error);
    // A session that was working here and then went away (or was refused) ended under the person.
    const ended = authEnabled && (refusedByApi || wasSignedIn.current);
    void (async () => {
      if (refusedByApi) {
        if (authEnabled) await signOut();
        else setDevIdentity(null);
      }
      const here = window.location.pathname + window.location.search;
      router.replace(isLeaving() ? LOGIN_PATH : loginHref(here, ended));
    })();
  }, [state, signedIn, me.error, router]);

  if (state === "chooseTenant" && me.error instanceof ApiError) return <TenantChoiceScreen error={me.error} />;
  if (state === "noMembership") return <NoMembershipScreen onRecheck={reload} busy={me.loading} />;
  if (state === "error" && me.error) return <GateErrorScreen error={me.error} onRetry={reload} busy={me.loading} />;
  if (state !== "ready") return <CheckingScreen />;

  return (
    <MeProvider value={me}>
      <Suspense fallback={<CheckingScreen />}>
        <AppShell>
          <Suspense fallback={<Loading />}>{children}</Suspense>
        </AppShell>
      </Suspense>
    </MeProvider>
  );
}
