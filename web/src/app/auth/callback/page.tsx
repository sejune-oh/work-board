"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { Notice, Screen, SignInButton } from "@/components/Auth";
import { Loading } from "@/components/Feedback";
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

  if (!failure) return <Loading label={t.auth.callback.working} />;

  const reason = {
    denied: t.auth.callback.denied,
    missing_code: t.auth.callback.missingCode,
    exchange: t.auth.callback.exchange,
    disabled: t.auth.callback.disabled,
  }[failure];

  return (
    <Screen>
      <div>
        <p className="font-mono text-meta text-muted">{t.app.title}</p>
        <h1 className="mt-1 text-display font-semibold tracking-[-0.015em]">{t.auth.callback.failedTitle}</h1>
      </div>
      <Notice>
        <p>{reason}</p>
        {authEnabled && <p className="mt-1 text-ink-soft">{t.auth.callback.fix}</p>}
      </Notice>
      <div className="flex flex-wrap items-center gap-4">
        {authEnabled && <SignInButton label={t.auth.callback.retry} />}
        <Link href="/" className="link text-ui">
          {t.auth.callback.home}
        </Link>
      </div>
    </Screen>
  );
}
