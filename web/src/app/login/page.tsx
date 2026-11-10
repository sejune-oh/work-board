"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useMemo } from "react";
import { CheckingScreen, SignInScreen } from "@/components/Auth";
import { StartScreen } from "@/components/StartScreen";
import { authEnabled, markLeaving, safePath } from "@/lib/auth";
import { useSignedIn } from "@/lib/me";

/** Standalone sign-in. Someone already signed in goes straight on to `next`. */
export default function LoginPage() {
  const router = useRouter();
  const params = useSearchParams();
  const signedIn = useSignedIn();
  const next = useMemo(() => (typeof window === "undefined" ? "/" : safePath(params.get("next"))), [params]);
  const ended = params.get("reason") === "ended";

  useEffect(() => {
    markLeaving(false);
  }, []);

  useEffect(() => {
    if (signedIn) router.replace(next);
  }, [signedIn, next, router]);

  if (signedIn !== false) return <CheckingScreen />;
  return authEnabled ? <SignInScreen next={next} ended={ended} /> : <StartScreen />;
}
