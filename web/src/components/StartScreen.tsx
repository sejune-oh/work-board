"use client";

import { useT } from "@/lib/i18n";
import { UserPicker } from "./UserPicker";

const RUN_STEPS = [
  "docker compose up -d",
  "cd server/src/WorkBoard.Api && dotnet run",
  "cd web && pnpm install && pnpm dev",
];

/** Shown instead of the boards until someone picks who they are. */
export function StartScreen({ tenantRequired = false }: { tenantRequired?: boolean }) {
  const t = useT();
  return (
    <div className="reveal mx-auto flex max-w-[560px] flex-col gap-6 py-6 min-[900px]:py-12">
      <div>
        <p className="font-mono text-meta text-muted">{t.app.title}</p>
        <h1 className="mt-1 text-display font-semibold tracking-[-0.015em]">{t.start.title}</h1>
        <p className="mt-1 text-body text-ink-soft">{t.start.lead}</p>
      </div>

      <div className="card p-4">
        <p className="mb-3 text-ui text-ink-soft">
          {tenantRequired ? t.start.tenantRequired : t.start.pick}
        </p>
        <UserPicker large />
      </div>

      <details className="card px-4 py-3 text-meta text-muted">
        <summary className="cursor-pointer hover:text-ink-soft">{t.start.runLocally}</summary>
        <ol className="mt-3 flex list-decimal flex-col gap-1.5 pl-5">
          {RUN_STEPS.map((step) => (
            <li key={step}>
              <code className="rounded-control bg-sunken px-1.5 py-0.5 font-mono text-meta text-ink-soft">{step}</code>
            </li>
          ))}
        </ol>
        <p className="mt-2">{t.start.emptyListHint}</p>
      </details>
    </div>
  );
}
