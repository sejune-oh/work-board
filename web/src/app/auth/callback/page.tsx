"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { CheckingScreen, GateFrame, Notice, SignInButton } from "@/components/Auth";
import { SignInError, authEnabled, finishSignIn } from "@/lib/auth";
import { useT } from "@/lib/i18n";

type Failure = SignInError["kind"];

/** GitHub sends the browser back here with `?code=`; the code becomes a session, then we go back. */
export default function AuthCallbackPage() {
  const t = useT();
  const router = useRouter();
  const params = useSearchParams();
  const [failure, setFailure] = useState<Failure | null>(null);

  useEffect(() => {
    let cancelled = false;
    finishSignIn(new URLSearchParams(params.toString()))
      .then((returnTo) => {
        if (!cancelled) router.replace(returnTo);
      })
      .catch((e: unknown) => {
        if (!cancelled) setFailure(e instanceof SignInError ? e.kind : "exchange");
      });
    return () => {
      cancelled = true;
    };
  }, [params, router]);

  if (!failure) return <CheckingScreen title={t.auth.callback.working} />;

  const reason = {
    denied: t.auth.callback.denied,
    missing_code: t.auth.callback.missingCode,
    exchange: t.auth.callback.exchange,
    disabled: t.auth.callback.disabled,
  }[failure];

  return (
    <GateFrame kicker={t.gate.login.kicker} title={t.auth.callback.failedTitle}>
      <Notice>
        <p>{reason}</p>
        {authEnabled && <p className="mt-1 text-ink-soft">{t.auth.callback.fix}</p>}
      </Notice>
      <div className="mt-4 flex flex-col items-center gap-3">
        {authEnabled && (
          <div className="w-full">
            <SignInButton label={t.auth.callback.retry} />
          </div>
        )}
        <Link href="/" className="link text-ui">
          {t.auth.callback.home}
        </Link>
      </div>
    </GateFrame>
  );
}
