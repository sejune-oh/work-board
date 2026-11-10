"use client";

import { ErrorBox, Loading } from "@/components/Feedback";
import { useApi } from "@/lib/api";
import type { Me, Project } from "@/lib/types";

export default function SettingsPage() {
  const me = useApi<Me>("/api/me");
  const projects = useApi<Project[]>("/api/projects");

  const error = me.error ?? projects.error;
  if (error) return <ErrorBox error={error} />;
  if (!me.data || !projects.data) return <Loading />;
  const s = me.data.settings;

  return (
    <div className="flex max-w-2xl flex-col gap-6">
      <h1 className="text-xl font-bold">설정</h1>
      <p className="text-sm text-slate-500">읽기 전용입니다. 변경 기능은 이후 단계에서 추가합니다.</p>

      <section>
        <h2 className="mb-2 text-sm font-semibold">고급 기능</h2>
        <ul className="text-sm">
          <li>이번 주기(스프린트): {s.sprintsEnabled ? "켜짐" : "꺼짐"}</li>
          <li>크기: {s.sizeEnabled ? "켜짐" : "꺼짐"}</li>
          <li>우선순위: {s.priorityEnabled ? "켜짐" : "꺼짐"}</li>
        </ul>
      </section>

      <section>
        <h2 className="mb-2 text-sm font-semibold">프로젝트 (저장소 묶음)</h2>
        <ul className="flex flex-col gap-1 text-sm">
          {projects.data.map((p) => (
            <li key={p.id}>
              <span className="font-medium">{p.name}</span>
              <span className="text-slate-500"> · {p.repositories.map((r) => r.name).join(", ")}</span>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
