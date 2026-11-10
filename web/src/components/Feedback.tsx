import { ApiError } from "@/lib/api";

export function Loading({ label = "불러오는 중" }: { label?: string }) {
  return <p className="px-3.5 py-4 text-ui text-muted">{label}…</p>;
}

/** Errors are about the app, not the work, so they stay in ink: no vermilion here. */
export function ErrorBox({ error }: { error: Error }) {
  const message =
    error instanceof ApiError && error.status === 401
      ? "오른쪽 위에서 개발용 사용자를 선택하세요."
      : error instanceof ApiError && error.status === 404
        ? "찾을 수 없거나 볼 권한이 없습니다."
        : "API 서버에 연결할 수 없습니다. server 폴더에서 API를 실행했는지 확인하세요.";
  return (
    <div role="alert" className="rounded-control border border-rule border-l-2 border-l-ink-soft bg-surface px-4 py-3 text-ui text-ink">
      {message}
    </div>
  );
}

export function Empty({ children }: { children: React.ReactNode }) {
  return <p className="rounded-control border border-dashed border-rule-strong px-3 py-2.5 text-meta text-muted">{children}</p>;
}
