import { ApiError } from "@/lib/api";

export function Loading({ label = "불러오는 중" }: { label?: string }) {
  return <p className="py-8 text-center text-sm text-slate-500">{label}…</p>;
}

export function ErrorBox({ error }: { error: Error }) {
  const message =
    error instanceof ApiError && error.status === 401
      ? "오른쪽 위에서 개발용 사용자를 선택하세요."
      : error instanceof ApiError && error.status === 404
        ? "찾을 수 없거나 볼 권한이 없습니다."
        : "API 서버에 연결할 수 없습니다. server 폴더에서 API를 실행했는지 확인하세요.";
  return (
    <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300">
      {message}
    </div>
  );
}

export function Empty({ children }: { children: React.ReactNode }) {
  return <p className="py-6 text-center text-sm text-slate-400">{children}</p>;
}
