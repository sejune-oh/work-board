"use client";

import { Empty, ErrorBox, Loading } from "@/components/Feedback";
import { GitHubMark } from "@/components/Auth";
import { ConnectionRow, ROW_LINE, githubInstallUrl } from "@/components/Connections";
import { Card, Count, PageHeader } from "@/components/ui";
import { type Loadable, useApi } from "@/lib/api";
import { useT } from "@/lib/i18n";
import { useMe } from "@/lib/me";
import type { GithubConnection, Project } from "@/lib/types";

export default function SettingsPage() {
  const me = useMe();
  const t = useT();
  const st = t.settings;
  const projects = useApi<Project[]>("/api/projects");
  const connections = useApi<GithubConnection[]>("/api/connections");

  const error = me.error ?? projects.error;
  if (error) return <ErrorBox error={error} />;
  if (!me.data || !projects.data) return <Loading />;
  const s = me.data.settings;
  const features = [
    { name: st.sprints, hint: st.sprintsHint, on: s.sprintsEnabled },
    { name: st.size, hint: st.sizeHint, on: s.sizeEnabled },
    { name: st.priority, hint: st.priorityHint, on: s.priorityEnabled },
  ];

  return (
    <div className="flex max-w-[720px] flex-col gap-6">
      <PageHeader meta={me.data.tenant.name} title={st.title}>
        {st.lead}
      </PageHeader>

      <ConnectionsCard connections={connections} isAdmin={me.data.member.role === "admin"} />

      <Card title={st.advanced} hint={st.advancedHint} className="reveal [animation-delay:60ms]">
        <ul>
          {features.map((f) => (
            <li key={f.name} className="flex items-start justify-between gap-4 relative px-5 py-4 after:absolute after:inset-x-5 after:top-0 after:h-px after:bg-rule first:after:hidden">
              <div>
                <p className="text-ui font-medium">{f.name}</p>
                <p className="text-small text-muted">{f.hint}</p>
              </div>
              <span className={`inline-flex shrink-0 items-center gap-1.5 text-small font-semibold ${f.on ? "text-ink" : "text-muted"}`}>
                <span aria-hidden className={`h-[7px] w-[7px] rounded-full ${f.on ? "bg-status-done" : "border border-muted"}`} />
                {f.on ? st.on : st.off}
              </span>
            </li>
          ))}
        </ul>
      </Card>

      <Card title={st.projects} count={projects.data.length} hint={st.projectsHint} className="reveal [animation-delay:90ms]">
        {projects.data.length === 0 ? (
          <div className="px-5 pb-5">
            <Empty>{st.noProjects}</Empty>
          </div>
        ) : (
          <ul>
            {projects.data.map((p) => (
              <li key={p.id} className="flex items-center gap-2.5 relative px-5 py-4 after:absolute after:inset-x-5 after:top-0 after:h-px after:bg-rule first:after:hidden">
                <span aria-hidden className="h-2 w-2 shrink-0 rounded-mark" style={{ background: p.color ?? "var(--wb-muted)" }} />
                <span className="text-ui font-medium">{p.name}</span>
                <span className="truncate font-mono text-meta text-muted">{p.repositories.map((r) => r.name).join(" · ")}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  );
}

function ConnectionsCard({ connections, isAdmin }: { connections: Loadable<GithubConnection[]>; isAdmin: boolean }) {
  const t = useT();
  const ct = t.connections;
  const list = connections.data;
  const action =
    isAdmin && githubInstallUrl ? (
      <a href={githubInstallUrl} className="btn btn-primary">
        <GitHubMark />
        {list && list.length > 0 ? ct.reconnect : ct.connect}
      </a>
    ) : null;

  return (
    <section aria-labelledby="connections-title" className="card reveal mt-2 [animation-delay:30ms]">
      <div className="flex min-h-[60px] flex-wrap items-center gap-x-3 gap-y-3 px-5 pb-2 pt-4">
        <div className="flex min-w-0 items-center gap-3">
          <h2 id="connections-title" className="whitespace-nowrap text-heading font-semibold">
            {ct.title}
          </h2>
          {list && <Count n={list.length} />}
          <span className="min-w-0 truncate text-small text-muted">{ct.hint}</span>
        </div>
        {action && <div className="ml-auto shrink-0">{action}</div>}
      </div>
      {connections.error ? (
        <div className="px-5 pb-5">
          <ErrorBox error={connections.error} />
        </div>
      ) : !list ? (
        <Loading />
      ) : list.length === 0 ? (
        <div className="px-5 pb-5">
          <Empty>{isAdmin ? ct.none : ct.noneMember}</Empty>
        </div>
      ) : (
        <ul aria-label={ct.listLabel}>
          {list.map((c) => (
            <ConnectionRow key={c.id} connection={c} className={ROW_LINE} />
          ))}
        </ul>
      )}
      {isAdmin && !githubInstallUrl && <p className="px-5 pb-5 text-small text-muted">{ct.notConfigured}</p>}
    </section>
  );
}
