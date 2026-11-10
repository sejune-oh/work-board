"use client";

import { useT } from "@/lib/i18n";
import { GateFrame } from "./Auth";
import { UserPicker } from "./UserPicker";

const RUN_STEPS = [
  "docker compose up -d",
  "cd server/src/WorkBoard.Api && dotnet run",
  "cd web && pnpm install && pnpm dev",
];

/** `/login` without GitHub sign-in: pick a dev user instead. */
export function StartScreen() {
  const t = useT();
  return (
    <GateFrame kicker={t.gate.login.kicker} title={t.start.title} lead={t.start.lead}>
      <p className="mb-4 text-ui text-ink-soft">{t.start.pick}</p>
      <UserPicker large />
      <details className="mt-5 border-t border-rule pt-4 text-small text-muted">
        <summary className="cursor-pointer hover:text-ink-soft">{t.start.runLocally}</summary>
        <ol className="mt-3 flex list-decimal flex-col gap-2 pl-5">
          {RUN_STEPS.map((step) => (
            <li key={step}>
              <code className="break-all rounded-flag bg-sunken px-1.5 py-0.5 font-mono text-meta text-ink-soft">{step}</code>
            </li>
          ))}
        </ol>
        <p className="mt-3">{t.start.emptyListHint}</p>
      </details>
    </GateFrame>
  );
}
