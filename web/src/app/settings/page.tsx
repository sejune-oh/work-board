"use client";

import { Empty, ErrorBox, Loading } from "@/components/Feedback";
import { Card, PageHeader } from "@/components/ui";
import { useApi } from "@/lib/api";
import { useT } from "@/lib/i18n";
import { useMe } from "@/lib/me";
import type { Project } from "@/lib/types";

export default function SettingsPage() {
  const me = useMe();
  const t = useT();
  const st = t.settings;
  const projects = useApi<Project[]>("/api/projects");

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
    <div className="flex max-w-[720px] flex-col gap-4">
      <PageHeader meta={me.data.tenant.githubOrgLogin} title={st.title}>
        {st.lead}
      </PageHeader>

      <Card title={st.advanced} hint={st.advancedHint} className="reveal mt-2 [animation-delay:30ms]">
        <ul>
          {features.map((f) => (
            <li key={f.name} className="flex items-start justify-between gap-4 border-b border-rule px-3.5 py-2.5 last:border-b-0">
              <div>
                <p className="text-ui font-medium">{f.name}</p>
                <p className="text-meta text-muted">{f.hint}</p>
              </div>
              <span className={`inline-flex shrink-0 items-center gap-1.5 text-meta font-semibold ${f.on ? "text-ink" : "text-muted"}`}>
                <span aria-hidden className={`h-[7px] w-[7px] rounded-full ${f.on ? "bg-status-done" : "border border-muted"}`} />
                {f.on ? st.on : st.off}
              </span>
            </li>
          ))}
        </ul>
      </Card>

      <Card title={st.projects} count={projects.data.length} hint={st.projectsHint} className="reveal [animation-delay:60ms]">
        {projects.data.length === 0 ? (
          <div className="p-3.5">
            <Empty>{st.noProjects}</Empty>
          </div>
        ) : (
          <ul>
            {projects.data.map((p) => (
              <li key={p.id} className="flex items-center gap-2.5 border-b border-rule px-3.5 py-2.5 last:border-b-0">
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
