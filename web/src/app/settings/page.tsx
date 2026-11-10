"use client";

import { Empty, ErrorBox, Loading } from "@/components/Feedback";
import { PageHeader, SectionHead } from "@/components/ui";
import { useApi } from "@/lib/api";
import { useMe } from "@/lib/me";
import type { Project } from "@/lib/types";

export default function SettingsPage() {
  const me = useMe();
  const projects = useApi<Project[]>("/api/projects");

  const error = me.error ?? projects.error;
  if (error) return <ErrorBox error={error} />;
  if (!me.data || !projects.data) return <Loading />;
  const s = me.data.settings;
  const features = [
    { name: "이번 주기", hint: "보드와 개요에 주기 이름과 남은 날을 보여 줍니다.", on: s.sprintsEnabled },
    { name: "크기", hint: "이슈 상세에 작음 / 보통 / 큼을 보여 줍니다.", on: s.sizeEnabled },
    { name: "우선순위", hint: "긴급인 일에 도장을 찍고, 개요의 손봐야 할 일에 올립니다.", on: s.priorityEnabled },
  ];

  return (
    <div className="flex max-w-[720px] flex-col gap-10">
      <PageHeader eyebrow={me.data.tenant.githubOrgLogin} title="설정">
        읽기 전용입니다. 바꾸는 기능은 이후 단계에서 추가합니다.
      </PageHeader>

      <section className="reveal [animation-delay:40ms]">
        <SectionHead eyebrow="기본은 꺼 둡니다" title="고급 기능" />
        <ul>
          {features.map((f) => (
            <li key={f.name} className="flex items-start justify-between gap-4 border-b border-rule py-3 last:border-b-0">
              <div>
                <p className="text-ui font-medium">{f.name}</p>
                <p className="text-meta text-muted">{f.hint}</p>
              </div>
              <span className={`shrink-0 text-meta font-semibold ${f.on ? "text-ink" : "text-muted"}`}>{f.on ? "켜짐" : "꺼짐"}</span>
            </li>
          ))}
        </ul>
      </section>

      <section className="reveal [animation-delay:80ms]">
        <SectionHead eyebrow="저장소 묶음" title="프로젝트" />
        {projects.data.length === 0 ? (
          <Empty>프로젝트(저장소 묶음)가 없습니다.</Empty>
        ) : (
          <ul>
            {projects.data.map((p) => (
              <li key={p.id} className="flex items-center gap-2.5 border-b border-rule py-3 last:border-b-0">
                <span aria-hidden className="h-2 w-2 shrink-0 rounded-mark" style={{ background: p.color ?? "var(--wb-muted)" }} />
                <span className="text-ui font-medium">{p.name}</span>
                <span className="truncate font-mono text-meta text-muted">{p.repositories.map((r) => r.name).join(" · ")}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
